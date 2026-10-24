using System.Globalization;
using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public enum ContentSource { Common, Over, Draft, None }
/// <summary>By: 우리 부서 덮어쓰기면 "이은혜 · 12월 27일"(만든 사람 · 날짜), 아니면 null.</summary>
public sealed record ContentItem(string Kind, string Label, string Short, ContentSource Source, string? Title, string? Detail, string? By = null);
public sealed record CalendarDay(DateOnly Date, IReadOnlyList<ContentItem> Items)
{
    public bool HasNone => Items.Any(i => i.Source == ContentSource.None);
}
public sealed record ContentAlert(bool Bad, string Label, string Text, DateOnly Go);
public sealed record ContentMonth(DateOnly Today, DateOnly Month, DateOnly Min, DateOnly Max, IReadOnlyList<CalendarDay> Days, IReadOnlyList<ContentAlert> Alerts);

/// <summary>
/// 관리 · 콘텐츠 달력(확정 시안: 관리-콘텐츠달력, R-120 ~ R-123). 1차는 보기만.
/// 날마다 종류별로: 이 부서 공개본(덮어쓰기) → 공용 공개본 → 공개 전 초안 → 없음. 미션 화면과 같은 고르기(MissionService.Current)다.
/// 기도는 부서 것은 그날 날짜, 공용은 그달 날짜 번호(0053) · 암송은 부서 것은 그 주 시작일, 공용은 그해 몇째 주(0052).
/// </summary>
public sealed class ContentCalendarService(IAppDb db, TenantContext tenant, TenantClock clock)
{
    public const int SoonDays = 14;
    public const int MonthsAround = 12;
    public static readonly (string Kind, string Label, string Short)[] Kinds = [("w", "말씀", "말"), ("q", "퀴즈", "퀴"), ("p", "기도", "기"), ("r", "암송", "암")];
    private static readonly CultureInfo Ko = new("ko-KR");
    private static RuleSet Rules => RuleSet.Default;

    public static string Day(DateOnly d) => d.ToString("M월 d일(ddd)", Ko);

    private sealed record Cand(int? TenantId, string Status, string Title, string Detail, int By, DateTime ByUtc);

    public async Task<ContentMonth> GetAsync(DateOnly? month, CancellationToken ct = default)
    {
        var today = clock.Today(tenant.Current.TimeZoneId);
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var (min, max) = (thisMonth.AddMonths(-MonthsAround), thisMonth.AddMonths(MonthsAround));
        var m = month is { } x ? new DateOnly(x.Year, x.Month, 1) : thisMonth;
        if (m < min) m = min;
        if (m > max) m = max;
        var first = m;
        var last = m.AddMonths(1).AddDays(-1);
        var soonEnd = today.AddDays(SoonDays - 1);
        var from = first < today ? first : today;
        var to = last > soonEnd ? last : soonEnd;

        await using var ctx = db.Create();
        // 현재 개정만(공개 · 초안). 공용(TenantId null) + 이 부서 것은 전역 필터가 거른다.
        var words = (await ctx.DailyWords.AsNoTracking().Where(w => w.IsCurrent && w.ExposeDate >= from && w.ExposeDate <= to).ToListAsync(ct))
            .ToLookup(w => w.ExposeDate, w => new Cand(w.TenantId, w.Status, w.Title, BibleBooks.Range(w.BookId, w.StartChapter, w.StartVerse, w.EndChapter, w.EndVerse), w.CreatedByAccountId, w.CreatedUtc));
        var quizzes = (await ctx.DailyQuizzes.AsNoTracking().Include(q => q.Choices).Where(q => q.IsCurrent && q.ExposeDate >= from && q.ExposeDate <= to).ToListAsync(ct))
            .ToLookup(q => q.ExposeDate, q => new Cand(q.TenantId, q.Status, q.Question,
                q.Format == QuizFormat.Text ? "답 쓰기" : $"보기 {q.Choices.Count}개", q.CreatedByAccountId, q.CreatedUtc));
        var prayers = await ctx.DailyPrayers.AsNoTracking()
            .Where(p => p.IsCurrent && ((p.TenantId != null && p.ExposeDate >= from && p.ExposeDate <= to) || (p.TenantId == null && p.DayNo != null))).ToListAsync(ct);
        var weekFrom = Week.Of(from, Rules.WeekStart).Start;
        var verses = await ctx.WeeklyVerses.AsNoTracking()
            .Where(v => v.IsCurrent && ((v.TenantId != null && v.StartDate >= weekFrom && v.StartDate <= to) || (v.TenantId == null && v.WeekNo != null))).ToListAsync(ct);

        static string Range(RangedContent c) => BibleBooks.Range(c.BookId, c.StartChapter, c.StartVerse, c.EndChapter, c.EndVerse);
        IEnumerable<Cand> PrayersOn(DateOnly d) => prayers
            .Where(p => p.TenantId != null ? p.ExposeDate == d : p.DayNo == d.Day)
            .Select(p => new Cand(p.TenantId, p.Status, p.TraitName, Range(p), p.CreatedByAccountId, p.CreatedUtc));
        IEnumerable<Cand> VersesOn(DateOnly d)
        {
            var w = Week.Of(d, Rules.WeekStart);
            var span = $"{w.Start:M'/'d} ~ {w.End:M'/'d}";   // ko 문화권에서 "/" 는 "-" 로 바뀌므로 글자로
            return verses.Where(v => v.TenantId != null ? v.StartDate == w.Start : v.WeekNo == w.NumberInYear)
                .Select(v => new Cand(v.TenantId, v.Status, v.Title, $"{Range(v)} · {span}", v.CreatedByAccountId, v.CreatedUtc));
        }

        // 덮어쓰기 만든 사람 이름: 범위 안 부서 콘텐츠의 계정만(부서 관리자 화면이라 이 부서 사람)
        var ids = words.SelectMany(g => g).Concat(quizzes.SelectMany(g => g)).Where(c => c.TenantId != null).Select(c => c.By)
            .Concat(prayers.Where(p => p.TenantId != null).Select(p => p.CreatedByAccountId))
            .Concat(verses.Where(v => v.TenantId != null).Select(v => v.CreatedByAccountId)).Distinct().ToList();
        var names = await ctx.Accounts.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.DisplayName, ct);
        string By(Cand c) => $"{names.GetValueOrDefault(c.By) ?? "부서"} · {c.ByUtc.ToString("M월 d일", Ko)}";

        CalendarDay Build(DateOnly d)
        {
            var items = new List<ContentItem>();
            foreach (var (kind, label, shortL) in Kinds)
            {
                var cands = (kind switch { "w" => words[d], "q" => quizzes[d], "p" => PrayersOn(d), _ => VersesOn(d) }).ToList();
                var pick = cands.FirstOrDefault(c => c.TenantId != null && c.Status == ContentStatus.Published) is { } over ? (over, ContentSource.Over)
                    : cands.FirstOrDefault(c => c.TenantId == null && c.Status == ContentStatus.Published) is { } common ? (common, ContentSource.Common)
                    : cands.OrderByDescending(c => c.TenantId != null).FirstOrDefault() is { } draft ? (draft, ContentSource.Draft)
                    : ((Cand?)null, ContentSource.None);
                var (c, src) = pick;
                items.Add(new ContentItem(kind, label, shortL, src, c?.Title,
                    c is null ? null : src == ContentSource.Draft ? $"{(c.TenantId == null ? "공용" : "우리 부서")} · 초안(공개 전) · {c.Detail}" : c.Detail,
                    src == ContentSource.Over ? By(c!) : null));
            }
            return new CalendarDay(d, items);
        }

        var all = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => Build(from.AddDays(i))).ToDictionary(x => x.Date);
        var days = Enumerable.Range(0, last.Day).Select(i => all[first.AddDays(i)]).ToList();

        // 경고: 앞으로 14일 안 빈 칸(빨강) · 이 달 그 뒤의 빈 칸과 초안(노랑)
        var alerts = new List<ContentAlert>();
        var soon = Enumerable.Range(0, SoonDays).Select(i => all[today.AddDays(i)])
            .SelectMany(day => day.Items.Where(i => i.Source == ContentSource.None).Select(i => (day.Date, i.Label))).ToList();
        if (soon.Count > 0)
            alerts.Add(new(true, "곧 비어요", $"{List(soon)} — 앞으로 {SoonDays}일 안에 콘텐츠가 없어요.", soon[0].Date));
        var ahead = days.Where(day => day.Date >= today).ToList();
        var drafts = ahead.SelectMany(day => day.Items.Where(i => i.Source == ContentSource.Draft).Select(i => (day.Date, i.Label))).ToList();
        if (drafts.Count > 0)
            alerts.Add(new(false, "공개 전", $"{Day(drafts[0].Date)}부터 {drafts.Count}칸이 아직 초안이에요. 공개되기 전에는 아이들에게 보이지 않아요.", drafts[0].Date));
        var later = ahead.Where(day => day.Date > soonEnd)
            .SelectMany(day => day.Items.Where(i => i.Source == ContentSource.None).Select(i => (day.Date, i.Label))).ToList();
        if (later.Count > 0)
            alerts.Add(new(false, "없음", $"{List(later)} 이(가) 비어 있어요.", later[0].Date));

        return new(today, m, min, max, days, alerts);
    }

    private static string List(List<(DateOnly Date, string Label)> xs)
    {
        var shown = string.Join(" · ", xs.Take(5).Select(x => $"{Day(x.Date)} {x.Label}"));
        return xs.Count > 5 ? $"{shown} 외 {xs.Count - 5}칸" : shown;
    }
}
