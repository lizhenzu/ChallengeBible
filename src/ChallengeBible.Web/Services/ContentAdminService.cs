using System.Text.Json;
using ChallengeBible.Domain.Missions;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ChallengeBible.Web.Services;

public enum ContentKind { Word, Quiz, Prayer, Recite }

public sealed record VerseRange(byte Book, short StartChapter, short StartVerse, short EndChapter, short EndVerse)
{
    public string Text => BibleBooks.Range(Book, StartChapter, StartVerse, EndChapter, EndVerse);
}

/// <summary>편집 칸 값. 종류마다 쓰는 칸만 채운다.</summary>
public sealed class ContentInput
{
    public DateOnly Date { get; set; }
    public byte WeekNo { get; set; }   // 암송만(0052)
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public string? Commentary { get; set; }
    public string? TraitName { get; set; }
    public string? TraitNameEn { get; set; }
    public string? Paragraphs { get; set; }   // 한 줄 = 한 문단
    public string? Question { get; set; }
    public string? Background { get; set; }
    public string Format { get; set; } = QuizFormat.Choice;
    public List<string> Choices { get; set; } = ["", "", "", ""];
    public int CorrectIndex { get; set; } = -1;
    public string? AcceptedAnswers { get; set; }   // 주관식: 한 줄에 하나
    public string? Explanation { get; set; }
    public short? LessonNo { get; set; }
    public string? LessonSummary { get; set; }
    public string? LessonPoint { get; set; }
    public string? LessonApply { get; set; }
    public byte Book { get; set; } = 1;
    public short StartChapter { get; set; } = 1;
    public short StartVerse { get; set; } = 1;
    public short EndChapter { get; set; } = 1;
    public short EndVerse { get; set; } = 1;
}

public sealed record ContentDay(DateOnly Date, int? Id, string Status, string? Title);

public sealed record ContentWeek(byte No, int? Id, string Status, string? Title);

/// <summary>
/// 플랫폼 공용 콘텐츠(TenantId NULL) 편집(API 계약 4-5, R-120 ~ R-124). 공용은 시스템 연결로만 쓴다(RLS fn_SharedWrite).
/// - 초안은 그 자리에서 고친다.
/// - 공개된 것을 고치면 새 개정 행을 만들고(IsCurrent), 예전 행은 보관한다 — 이미 한 기록은 예전 행을 가리킨 채 그대로(R-124).
/// </summary>
public sealed class ContentAdminService(ISystemDb systemDb, TimeProvider time)
{
    /// <summary>DB 에 한글을 그대로 남긴다(\uXXXX 로 바꾸지 않음 — 운영자가 SQL 로 읽을 수 있게).</summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
    };

    private async Task<AppDbContext> OpenAsync(int actor, CancellationToken ct)
    {
        var ctx = systemDb.Create(actor);
        if (!await ctx.Accounts.AnyAsync(a => a.Id == actor && a.IsPlatformAdmin && a.Status == "Active", ct))
        {
            await ctx.DisposeAsync();
            throw new UnauthorizedAccessException("플랫폼 관리자만 할 수 있어요.");
        }
        return ctx;
    }

    /// <summary>날짜 단위 콘텐츠(말씀 · 기도 · 퀴즈)의 한 달. 암송은 <see cref="WeeksAsync"/>.</summary>
    public async Task<List<ContentDay>> MonthAsync(int actor, ContentKind kind, int year, int month, CancellationToken ct = default)
    {
        if (kind == ContentKind.Recite) throw new ArgumentException("암송은 주 번호로 해요(WeeksAsync).", nameof(kind));
        await using var ctx = await OpenAsync(actor, ct);
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var rows = kind switch
        {
            ContentKind.Word => await Shared(ctx.DailyWords).Where(x => x.ExposeDate >= from && x.ExposeDate <= to).Select(x => new { D = x.ExposeDate, x.Id, x.Status, T = x.Title }).ToListAsync(ct),
            ContentKind.Prayer => await Shared(ctx.DailyPrayers).Where(x => x.ExposeDate >= from && x.ExposeDate <= to).Select(x => new { D = x.ExposeDate, x.Id, x.Status, T = x.TraitName }).ToListAsync(ct),
            _ => await Shared(ctx.DailyQuizzes).Where(x => x.ExposeDate >= from && x.ExposeDate <= to).Select(x => new { D = x.ExposeDate, x.Id, x.Status, T = x.Question }).ToListAsync(ct),
        };
        return Enumerable.Range(0, to.Day).Select(from.AddDays)
            .Select(d => rows.FirstOrDefault(r => r.D == d) is { } r ? new ContentDay(d, r.Id, r.Status, r.T) : new ContentDay(d, null, "None", null)).ToList();
    }

    /// <summary>공용 암송 1 ~ 55주(0052). 해마다 1주부터 같은 순서로 쓰인다.</summary>
    public async Task<List<ContentWeek>> WeeksAsync(int actor, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var rows = await Shared(ctx.WeeklyVerses).Select(x => new { x.WeekNo, x.Id, x.Status, x.Title }).ToListAsync(ct);
        return Enumerable.Range(1, WeeklyVerse.MaxWeekNo).Select(n => (byte)n)
            .Select(n => rows.FirstOrDefault(r => r.WeekNo == n) is { } r ? new ContentWeek(n, r.Id, r.Status, r.Title) : new ContentWeek(n, null, "None", null)).ToList();
    }

    private static IQueryable<T> Shared<T>(IQueryable<T> q) where T : ContentBase => q.Where(x => x.TenantId == null && x.IsCurrent);

    /// <summary>편집 칸에 넣을 지금 값(없으면 빈 칸).</summary>
    public async Task<(ContentInput Input, string Status, int? Id)> LoadAsync(int actor, ContentKind kind, DateOnly date, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var i = new ContentInput { Date = date };
        switch (kind)
        {
            case ContentKind.Word when await Shared(ctx.DailyWords).SingleOrDefaultAsync(x => x.ExposeDate == date, ct) is { } w:
                (i.Title, i.Summary, i.Commentary) = (w.Title, w.Summary, w.Commentary);
                SetRange(i, w);
                return (i, w.Status, w.Id);
            case ContentKind.Prayer when await Shared(ctx.DailyPrayers).SingleOrDefaultAsync(x => x.ExposeDate == date, ct) is { } p:
                (i.TraitName, i.TraitNameEn) = (p.TraitName, p.TraitNameEn);
                i.Paragraphs = string.Join("\n", JsonSerializer.Deserialize<List<string>>(p.Paragraphs) ?? []);
                SetRange(i, p);
                return (i, p.Status, p.Id);
            case ContentKind.Quiz when await Shared(ctx.DailyQuizzes).Include(x => x.Choices).Include(x => x.AcceptedAnswers).SingleOrDefaultAsync(x => x.ExposeDate == date, ct) is { } q:
                (i.Question, i.Background, i.Format, i.Explanation) = (q.Question, q.Background, q.Format, q.Explanation);
                var ch = q.Choices.OrderBy(c => c.SortOrder).ToList();
                i.Choices = ch.Select(c => c.Text).Concat(Enumerable.Repeat("", Math.Max(0, 4 - ch.Count))).ToList();
                i.CorrectIndex = ch.FindIndex(c => c.Id == q.CorrectChoiceId);
                i.AcceptedAnswers = string.Join("\n", q.AcceptedAnswers.Select(a => a.NormalizedText));
                return (i, q.Status, q.Id);
        }
        return (i, "None", null);
    }

    /// <summary>공용 암송 no 주의 지금 값(없으면 빈 칸).</summary>
    public async Task<(ContentInput Input, string Status, int? Id)> LoadWeekAsync(int actor, byte no, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var i = new ContentInput { WeekNo = no };
        if (await Shared(ctx.WeeklyVerses).SingleOrDefaultAsync(x => x.WeekNo == no, ct) is not { } v) return (i, "None", null);
        (i.Title, i.LessonNo, i.LessonSummary, i.LessonPoint, i.LessonApply) = (v.Title, v.LessonNo, v.LessonSummary, v.LessonPoint, v.LessonApply);
        SetRange(i, v);
        return (i, v.Status, v.Id);
    }

    private static void SetRange(ContentInput i, RangedContent r) =>
        (i.Book, i.StartChapter, i.StartVerse, i.EndChapter, i.EndVerse) = (r.BookId, r.StartChapter, r.StartVerse, r.EndChapter, r.EndVerse);

    public static FieldErrors Validate(ContentKind kind, ContentInput i)
    {
        var e = new FieldErrors();
        static bool Blank(string? s) => string.IsNullOrWhiteSpace(s);
        if (kind != ContentKind.Quiz)
        {
            var book = BibleBooks.All().FirstOrDefault(b => b.Id == i.Book);
            if (book is null) e["Range"] = "책을 골라 주세요.";
            else if (i.StartChapter < 1 || i.EndChapter > book.ChapterCount || i.StartVerse < 1 || i.EndVerse < 1)
                e["Range"] = $"{book.Name}은(는) 1 ~ {book.ChapterCount}장이에요.";
            else if (i.EndChapter < i.StartChapter || (i.EndChapter == i.StartChapter && i.EndVerse < i.StartVerse))
                e["Range"] = "끝이 시작보다 앞이에요.";
        }
        switch (kind)
        {
            case ContentKind.Word:
                if (Blank(i.Title)) e["Title"] = "제목을 써 주세요.";
                if (Blank(i.Summary)) e["Summary"] = "오늘 이야기를 써 주세요.";
                break;
            case ContentKind.Prayer:
                if (Blank(i.TraitName)) e["TraitName"] = "성품 이름을 써 주세요.";
                if (Lines(i.Paragraphs).Count == 0) e["Paragraphs"] = "기도문을 한 줄 이상 써 주세요.";
                break;
            case ContentKind.Quiz:
                if (Blank(i.Question)) e["Question"] = "문제를 써 주세요.";
                if (i.Format == QuizFormat.Choice)
                {
                    var filled = i.Choices.Count(c => !Blank(c));
                    if (filled < 2) e["Choices"] = "보기를 두 개 이상 써 주세요.";
                    else if (i.CorrectIndex < 0 || i.CorrectIndex >= i.Choices.Count || Blank(i.Choices[i.CorrectIndex])) e["Choices"] = "정답 보기에 동그라미를 해 주세요.";
                }
                else if (Lines(i.AcceptedAnswers).Count == 0) e["AcceptedAnswers"] = "맞는 답을 한 줄에 하나씩 써 주세요.";
                break;
            case ContentKind.Recite:
                if (Blank(i.Title)) e["Title"] = "제목(질문)을 써 주세요.";
                if (i.WeekNo is < 1 or > WeeklyVerse.MaxWeekNo) e["Date"] = $"암송은 1 ~ {WeeklyVerse.MaxWeekNo}주 가운데 골라요.";
                break;
        }
        return e;
    }

    private static List<string> Lines(string? s) =>
        (s ?? "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

    /// <summary>저장. publish 면 공개, 아니면 초안. 공개된 것을 고치면 새 개정(R-124).</summary>
    public async Task<FieldErrors> SaveAsync(int actor, ContentKind kind, ContentInput input, bool publish, CancellationToken ct = default)
    {
        var errors = Validate(kind, input);
        if (!errors.Ok) return errors;
        await using var ctx = await OpenAsync(actor, ct);
        await using var tx = ctx.Database.IsRelational() ? await ctx.Database.BeginTransactionAsync(ct) : null;
        var now = time.GetUtcNow().UtcDateTime;
        var status = publish ? ContentStatus.Published : ContentStatus.Draft;

        ContentBase? current = kind switch
        {
            ContentKind.Word => await Shared(ctx.DailyWords).SingleOrDefaultAsync(x => x.ExposeDate == input.Date, ct),
            ContentKind.Prayer => await Shared(ctx.DailyPrayers).SingleOrDefaultAsync(x => x.ExposeDate == input.Date, ct),
            ContentKind.Quiz => await Shared(ctx.DailyQuizzes).Include(x => x.Choices).Include(x => x.AcceptedAnswers).SingleOrDefaultAsync(x => x.ExposeDate == input.Date, ct),
            _ => await Shared(ctx.WeeklyVerses).SingleOrDefaultAsync(x => x.WeekNo == input.WeekNo, ct),
        };

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
            target = kind switch
            {
                ContentKind.Word => new DailyWord { Title = "", Summary = "" },
                ContentKind.Prayer => new DailyPrayer { TraitName = "", Paragraphs = "[]" },
                ContentKind.Quiz => new DailyQuiz { Question = "" },
                _ => new WeeklyVerse { Title = "" },
            };
            target.Revision = (current?.Revision ?? 0) + 1;
            target.ReplacesId = current?.Id;
            target.CreatedUtc = now;
            target.CreatedByAccountId = actor;
            ctx.Add(target);
        }
        else target = current;   // 초안은 그 자리에서

        // 공개된 객관식은 정답 보기가 있어야 해서(CK_DailyQuiz_Correct) 퀴즈는 초안으로 저장 → 보기 · 정답 → 상태
        target.Status = target is DailyQuiz ? ContentStatus.Draft : status;
        Apply(kind, target, input);
        await ctx.SaveChangesAsync(ct);
        if (target is DailyQuiz quiz)
        {
            await ApplyQuizAsync(ctx, quiz, input, ct);
            quiz.Status = status;
            await ctx.SaveChangesAsync(ct);
        }
        if (tx is not null) await tx.CommitAsync(ct);
        return errors;
    }

    private static void Apply(ContentKind kind, ContentBase t, ContentInput i)
    {
        if (t is RangedContent r) (r.BookId, r.StartChapter, r.StartVerse, r.EndChapter, r.EndVerse) = (i.Book, i.StartChapter, i.StartVerse, i.EndChapter, i.EndVerse);
        switch (t)
        {
            case DailyWord w:
                (w.ExposeDate, w.Title, w.Summary, w.Commentary) = (i.Date, i.Title!.Trim(), i.Summary!.Trim(), Trim(i.Commentary));
                break;
            case DailyPrayer p:
                (p.ExposeDate, p.TraitName, p.TraitNameEn) = (i.Date, i.TraitName!.Trim(), Trim(i.TraitNameEn));
                p.Paragraphs = JsonSerializer.Serialize(Lines(i.Paragraphs), Json);
                break;
            case DailyQuiz q:
                (q.ExposeDate, q.Question, q.Background, q.Format, q.Explanation) = (i.Date, i.Question!.Trim(), Trim(i.Background), i.Format, Trim(i.Explanation));
                break;
            case WeeklyVerse v:
                (v.WeekNo, v.Title) = (i.WeekNo, i.Title!.Trim());   // 공용은 날짜 없이 주 번호만(0052)
                (v.LessonNo, v.LessonSummary, v.LessonPoint, v.LessonApply) = (i.LessonNo, Trim(i.LessonSummary), Trim(i.LessonPoint), Trim(i.LessonApply));
                break;
        }
    }

    /// <summary>보기 · 정답 · 허용 답. 정답 보기는 그 퀴즈의 보기라서(복합 FK) 보기를 먼저 저장한 뒤 정답을 건다.</summary>
    private static async Task ApplyQuizAsync(AppDbContext ctx, DailyQuiz q, ContentInput i, CancellationToken ct)
    {
        q.CorrectChoiceId = null;
        await ctx.SaveChangesAsync(ct);   // 정답 연결을 먼저 끊어야 보기를 지울 수 있다(복합 FK)
        ctx.DailyQuizChoices.RemoveRange(q.Choices);
        ctx.DailyQuizAcceptedAnswers.RemoveRange(q.AcceptedAnswers);
        await ctx.SaveChangesAsync(ct);
        q.Choices.Clear();
        q.AcceptedAnswers.Clear();

        if (q.Format == QuizFormat.Choice)
        {
            DailyQuizChoice? correct = null;
            byte order = 1;
            for (var k = 0; k < i.Choices.Count; k++)
            {
                if (string.IsNullOrWhiteSpace(i.Choices[k])) continue;
                var c = new DailyQuizChoice { DailyQuizId = q.Id, SortOrder = order++, Text = i.Choices[k].Trim() };
                q.Choices.Add(c);
                if (k == i.CorrectIndex) correct = c;
            }
            await ctx.SaveChangesAsync(ct);
            q.CorrectChoiceId = correct?.Id;
        }
        else
        {
            foreach (var a in Lines(i.AcceptedAnswers).Select(QuizPolicy.Normalize).Distinct())
                q.AcceptedAnswers.Add(new DailyQuizAcceptedAnswer { DailyQuizId = q.Id, NormalizedText = a });
        }
        await ctx.SaveChangesAsync(ct);
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>고른 초안들을 한꺼번에 공개. 공개할 수 없는(칸이 빈) 초안은 건너뛰고 수를 돌려준다.</summary>
    public async Task<(int Published, int Skipped)> PublishManyAsync(int actor, ContentKind kind, IReadOnlyCollection<DateOnly> dates, CancellationToken ct = default)
    {
        var (ok, skip) = (0, 0);
        foreach (var d in dates)
        {
            var (input, status, _) = await LoadAsync(actor, kind, d, ct);
            if (status != ContentStatus.Draft) continue;
            var e = await SaveAsync(actor, kind, input, publish: true, ct);
            if (e.Ok) ok++; else skip++;
        }
        return (ok, skip);
    }

    /// <summary>고른 암송 초안(주 번호)들을 한꺼번에 공개.</summary>
    public async Task<(int Published, int Skipped)> PublishWeeksAsync(int actor, IReadOnlyCollection<byte> weeks, CancellationToken ct = default)
    {
        var (ok, skip) = (0, 0);
        foreach (var n in weeks)
        {
            var (input, status, _) = await LoadWeekAsync(actor, n, ct);
            if (status != ContentStatus.Draft) continue;
            var e = await SaveAsync(actor, ContentKind.Recite, input, publish: true, ct);
            if (e.Ok) ok++; else skip++;
        }
        return (ok, skip);
    }
}
