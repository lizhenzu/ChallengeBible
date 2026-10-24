using System.Text.Json;
using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>부서 덮어쓰기 편집 화면에 보여 줄 것. Own = 이 부서 것이 있으면 그 상태(Draft/Published), 없으면 null(공용 내용을 채워 시작).</summary>
public sealed record TenantContentEdit(ContentKind Kind, DateOnly From, DateOnly To, ContentInput Input, string? Own, string? CommonTitle, int? CommonNo, bool Past);

/// <summary>
/// 부서 관리자의 암송 · 기도 덮어쓰기(확정 시안: 관리-암송기도입력, 0052 · 0053 · R-120 ~ R-124).
/// 기도는 그날 하루(ExposeDate), 암송은 그 주 일요일 ~ 토요일(StartDate · EndDate). 해 · 달이 바뀌면 저절로 공용으로 돌아간다.
/// 이 부서 행만 쓴다(앱 필터 + RLS fn_SharedWrite). 공개된 것을 고치면 새 개정(R-124), 되돌리기는 보관(지우지 않음 — 기록이 가리킴).
/// 지난 날짜(암송은 이미 끝난 주)는 고칠 수 없다.
/// </summary>
public sealed class TenantContentService(IAppDb db, TenantContext tenant, TenantClock clock, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
    };
    private static RuleSet Rules => RuleSet.Default;   // 주 시작 요일은 1차에 바꾸지 않는다(ADR 0060)

    public DateOnly Today => clock.Today(tenant.Current.TimeZoneId);

    public static bool Supported(ContentKind kind) => kind is ContentKind.Prayer or ContentKind.Recite;

    private static (DateOnly From, DateOnly To) Span(ContentKind kind, DateOnly date)
    {
        if (kind == ContentKind.Prayer) return (date, date);
        var w = Week.Of(date, Rules.WeekStart);
        return (w.Start, w.End);
    }

    private async Task<ContentBase?> OwnAsync(AppDbContext ctx, ContentKind kind, DateOnly from, CancellationToken ct) => kind == ContentKind.Prayer
        ? await ctx.DailyPrayers.SingleOrDefaultAsync(p => p.TenantId == tenant.Current.Id && p.IsCurrent && p.ExposeDate == from, ct)
        : await ctx.WeeklyVerses.SingleOrDefaultAsync(v => v.TenantId == tenant.Current.Id && v.IsCurrent && v.StartDate == from, ct);

    public async Task<TenantContentEdit> LoadAsync(ContentKind kind, DateOnly date, CancellationToken ct = default)
    {
        var (from, to) = Span(kind, date);
        await using var ctx = db.Create();
        var own = await OwnAsync(ctx, kind, from, ct);
        var commonNo = kind == ContentKind.Prayer ? from.Day : Week.Of(from, Rules.WeekStart).NumberInYear;
        ContentBase? common = kind == ContentKind.Prayer
            ? await ctx.DailyPrayers.AsNoTracking().FirstOrDefaultAsync(p => p.TenantId == null && p.IsCurrent && p.Status == ContentStatus.Published && p.DayNo == commonNo, ct)
            : await ctx.WeeklyVerses.AsNoTracking().FirstOrDefaultAsync(v => v.TenantId == null && v.IsCurrent && v.Status == ContentStatus.Published && v.WeekNo == commonNo, ct);
        var input = new ContentInput { Date = from, SlotNo = (byte)commonNo };
        Fill(input, own ?? common);
        var commonTitle = common switch { DailyPrayer p => p.TraitName, WeeklyVerse v => v.Title, _ => null };
        return new TenantContentEdit(kind, from, to, input, own?.Status, commonTitle, common is null ? null : commonNo, to < Today);
    }

    private static void Fill(ContentInput i, ContentBase? c)
    {
        if (c is RangedContent r) (i.Book, i.StartChapter, i.StartVerse, i.EndChapter, i.EndVerse) = (r.BookId, r.StartChapter, r.StartVerse, r.EndChapter, r.EndVerse);
        switch (c)
        {
            case DailyPrayer p:
                (i.TraitName, i.TraitNameEn) = (p.TraitName, p.TraitNameEn);
                i.Paragraphs = string.Join("\n", JsonSerializer.Deserialize<List<string>>(p.Paragraphs) ?? []);
                break;
            case WeeklyVerse v:
                (i.Title, i.LessonNo, i.LessonSummary, i.LessonPoint, i.LessonApply) = (v.Title, v.LessonNo, v.LessonSummary, v.LessonPoint, v.LessonApply);
                break;
        }
    }

    /// <summary>저장. publish 면 공개, 아니면 초안. 공개된 것을 고치면 새 개정(R-124).</summary>
    public async Task<FieldErrors> SaveAsync(int actor, ContentKind kind, DateOnly date, ContentInput input, bool publish, CancellationToken ct = default)
    {
        if (!Supported(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var (from, to) = Span(kind, date);
        input.Date = from;
        input.SlotNo = (byte)(kind == ContentKind.Prayer ? from.Day : Math.Clamp(Week.Of(from, Rules.WeekStart).NumberInYear, 1, WeeklyVerse.MaxWeekNo));
        var errors = ContentAdminService.Validate(kind, input);
        if (to < Today) errors["Date"] = "지난 날짜는 고칠 수 없어요.";
        if (!errors.Ok) return errors;

        await using var ctx = db.Create();
        var now = time.GetUtcNow().UtcDateTime;
        var current = await OwnAsync(ctx, kind, from, ct);
        ContentBase target;
        if (current is null || current.Status == ContentStatus.Published)
        {
            if (current is not null && !publish)
            {
                errors["Status"] = "공개된 콘텐츠는 바로 고친 내용으로 공개해요. 초안으로 되돌릴 수 없어요.";
                return errors;
            }
            if (current is not null)
            {
                current.IsCurrent = false;            // 예전 개정은 보관(기록이 가리킴)
                current.Status = ContentStatus.Archived;
                await ctx.SaveChangesAsync(ct);       // 날짜 고유 인덱스 때문에 먼저 내린다
            }
            target = kind == ContentKind.Prayer ? new DailyPrayer { TraitName = "", Paragraphs = "[]" } : new WeeklyVerse { Title = "" };
            target.TenantId = tenant.Current.Id;
            target.Revision = (current?.Revision ?? 0) + 1;
            target.ReplacesId = current?.Id;
            target.CreatedUtc = now;
            target.CreatedByAccountId = actor;
            ctx.Add(target);
        }
        else target = current;   // 초안은 그 자리에서

        target.Status = publish ? ContentStatus.Published : ContentStatus.Draft;
        var t = (RangedContent)target;
        (t.BookId, t.StartChapter, t.StartVerse, t.EndChapter, t.EndVerse) = (input.Book, input.StartChapter, input.StartVerse, input.EndChapter, input.EndVerse);
        switch (target)
        {
            case DailyPrayer p:   // 부서 기도는 날짜만(0053)
                (p.ExposeDate, p.DayNo, p.TraitName, p.TraitNameEn) = (from, null, input.TraitName!.Trim(), Trim(input.TraitNameEn));
                p.Paragraphs = JsonSerializer.Serialize((input.Paragraphs ?? "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(), Json);
                break;
            case WeeklyVerse v:   // 부서 암송은 그 주 일요일 ~ 토요일(0052)
                (v.StartDate, v.EndDate, v.WeekNo, v.Title) = (from, to, null, input.Title!.Trim());
                (v.LessonNo, v.LessonSummary, v.LessonPoint, v.LessonApply) = (input.LessonNo, Trim(input.LessonSummary), Trim(input.LessonPoint), Trim(input.LessonApply));
                break;
        }
        await ctx.SaveChangesAsync(ct);
        return errors;
    }

    /// <summary>공용으로 되돌리기: 이 부서 행을 보관한다(지우지 않음). 지난 날짜는 안 된다. 되돌린 것이 있으면 true.</summary>
    public async Task<bool> RevertAsync(ContentKind kind, DateOnly date, CancellationToken ct = default)
    {
        var (from, to) = Span(kind, date);
        if (to < Today) return false;
        await using var ctx = db.Create();
        var own = await OwnAsync(ctx, kind, from, ct);
        if (own is null) return false;
        own.IsCurrent = false;
        own.Status = ContentStatus.Archived;
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
