using System.Text.Json;
using ChallengeBible.Domain.Missions;
using ChallengeBible.Domain.Points;
using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Seasons;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record WordView(int Id, string Title, string Range, string Summary, BibleRef Ref);
public sealed record PrayerView(int Id, string Trait, string? TraitEn, string Range, IReadOnlyList<string> Paragraphs, BibleRef Ref);
public sealed record QuizChoiceView(int Id, string Text);
public sealed record QuizView(int Id, string Question, string? Background, string Format, IReadOnlyList<QuizChoiceView> Choices, string? Explanation);
public sealed record ReciteView(int Id, string Title, string Range, DateOnly Start, DateOnly End, short? LessonNo, string? Summary, string? Point, string? Apply, BibleRef Ref);

public sealed record QuizStateView(int AttemptsUsed, int MaxAttempts, bool Solved, IReadOnlyList<int?> MyChoices)
{
    public QuizState State => QuizPolicy.State(AttemptsUsed, Solved, MaxAttempts);
}

/// <summary>그날 미션 화면에 필요한 것 전부(R-12: 지난 날짜도 보기는 된다).</summary>
public sealed record MissionDay(
    DateOnly Date, DateOnly Today, WindowResult Window,
    WordView? Word, PrayerView? Prayer, QuizView? Quiz, ReciteView? Recite,
    IReadOnlySet<MissionItem> Done, bool RecitedThisWeek, QuizStateView QuizState,
    int DayPoints, int SeasonPoints, RuleSet Rules);

public sealed record DayScore(DateOnly Date, int Word, int Prayer, int Quiz, int Recite, bool Complete)
{
    public int Total => Word + Prayer + Quiz + Recite;
}

public sealed record MissionResult(bool Ok, int Added, int SeasonPoints, IReadOnlyList<int> NewMilestones, string Message, bool? Correct = null);

/// <summary>
/// 미션 수행(03 문서 2 · 3절). 서버에서 모든 조건을 검사한다(R-13): 인정 기간, 시즌 시작일, 항목 사용 여부,
/// 그날 콘텐츠(공개 · 현재 개정, 덮어쓰기 우선 R-120), 하루 · 항목 1건(R-21 · R-61), 암송 주 1회(R-51).
/// 점수는 수행 기록과 원장에 그때 점수로 남긴다(R-63 · R-64).
/// </summary>
public sealed class MissionService(IAppDb db, TenantContext tenant, TenantClock clock, TimeProvider time, RuleService ruleService)
{
    // 1차는 플랫폼 기본 규칙(모든 부서 같음). 미션 날짜에 적용되는 버전을 그 요청 처음에 읽는다(ADR 0060).
    // 주 시작 요일은 1차에 바꾸지 않으므로 날짜를 모르는 곳(달력 · 이어온 날)도 같은 값을 쓴다.
    private RuleSet Rules { get; set; } = RuleSet.Default;

    private async Task UseRulesForAsync(DateOnly date, CancellationToken ct) => Rules = await ruleService.ForDateAsync(date, ct);

    public DateOnly Today => clock.Today(tenant.Current.TimeZoneId);

    /// <summary>이 소속의 참여자 Id. 참여자 역할이 없으면 null(보기만).</summary>
    public async Task<int?> ParticipantIdAsync(CancellationToken ct = default)
    {
        if (tenant.MembershipId is not int mid || !tenant.Has(Roles.Participant)) return null;
        await using var ctx = db.Create();
        return await ctx.Participants.Where(p => p.MembershipId == mid && p.IsActive).Select(p => (int?)p.Id).SingleOrDefaultAsync(ct);
    }

    private async Task<Season> SeasonAsync(AppDbContext ctx, int year, CancellationToken ct)
    {
        var start = await ctx.TenantSeasons.Where(s => s.Year == year).Select(s => (DateOnly?)s.StartDate).SingleOrDefaultAsync(ct);
        return start is { } d ? Season.Create(year, d) : Season.DefaultFor(year);
    }

    public async Task<MissionDay> GetDayAsync(int participantId, DateOnly date, CancellationToken ct = default)
    {
        await UseRulesForAsync(date, ct);
        var today = Today;
        await using var ctx = db.Create();
        var season = await SeasonAsync(ctx, date.Year, ct);
        var window = MissionWindow.Check(today, date, Rules.GraceDays, season.StartDate);

        var word = await Current(ctx.DailyWords.Where(w => w.ExposeDate == date)).FirstOrDefaultAsync(ct);
        var prayer = await Current(Prayers(ctx, date)).FirstOrDefaultAsync(ct);
        var quiz = await Current(ctx.DailyQuizzes.Include(q => q.Choices).Where(q => q.ExposeDate == date)).FirstOrDefaultAsync(ct);
        var (verse, week) = await VerseAsync(ctx, date, ct);

        var acts = await ctx.Activities.AsNoTracking().Where(a => a.ParticipantId == participantId && a.MissionDate == date).ToListAsync(ct);
        var recited = verse is not null && await ctx.Activities.AnyAsync(a => a.ParticipantId == participantId
            && a.MissionItem == "Recite" && a.ReciteStartDate == week.Start, ct);
        var attempts = await ctx.QuizAttempts.AsNoTracking().Where(a => a.ParticipantId == participantId && a.MissionDate == date)
            .OrderBy(a => a.AttemptNo).ToListAsync(ct);
        var dayPoints = await ctx.PointLedgers.Where(l => l.ParticipantId == participantId && l.MissionDate == date).SumAsync(l => (int?)l.Points, ct) ?? 0;
        var seasonPoints = await SeasonPointsAsync(ctx, participantId, today, ct);

        return new MissionDay(date, today, window,
            word is null ? null : new WordView(word.Id, word.Title, BibleBooks.Range(word.BookId, word.StartChapter, word.StartVerse, word.EndChapter, word.EndVerse), word.Summary,
                new BibleRef(word.BookId, word.StartChapter, word.StartVerse, word.EndChapter, word.EndVerse)),
            prayer is null ? null : new PrayerView(prayer.Id, prayer.TraitName, prayer.TraitNameEn,
                BibleBooks.Range(prayer.BookId, prayer.StartChapter, prayer.StartVerse, prayer.EndChapter, prayer.EndVerse),
                JsonSerializer.Deserialize<List<string>>(prayer.Paragraphs) ?? [],
                new BibleRef(prayer.BookId, prayer.StartChapter, prayer.StartVerse, prayer.EndChapter, prayer.EndVerse)),
            quiz is null ? null : new QuizView(quiz.Id, quiz.Question, quiz.Background, quiz.Format,
                quiz.Choices.OrderBy(c => c.SortOrder).Select(c => new QuizChoiceView(c.Id, c.Text)).ToList(),
                attempts.Any(a => a.IsCorrect) || attempts.Count >= Rules.QuizMaxAttempts ? quiz.Explanation : null),   // 해설은 끝난 뒤에
            verse is null ? null : new ReciteView(verse.Id, verse.Title,
                BibleBooks.Range(verse.BookId, verse.StartChapter, verse.StartVerse, verse.EndChapter, verse.EndVerse),
                week.Start, week.End, verse.LessonNo, verse.LessonSummary, verse.LessonPoint, verse.LessonApply,
                new BibleRef(verse.BookId, verse.StartChapter, verse.StartVerse, verse.EndChapter, verse.EndVerse)),
            acts.Select(a => MissionItemCodes.Parse(a.MissionItem)).ToHashSet(), recited,
            new QuizStateView(attempts.Count, Rules.QuizMaxAttempts, attempts.Any(a => a.IsCorrect), attempts.Select(a => a.SelectedChoiceId).ToList()),
            dayPoints, seasonPoints, Rules);
    }

    /// <summary>R-120: 공개된 현재 개정 중 내 영역 덮어쓰기가 먼저, 없으면 공용.</summary>
    private static IQueryable<T> Current<T>(IQueryable<T> q) where T : ContentBase =>
        q.Where(c => c.IsCurrent && c.Status == ContentStatus.Published).OrderByDescending(c => c.TenantId != null);

    /// <summary>그날 기도 후보(0053): 내 부서의 그날 기도 + 그달 날짜 번호의 공용 기도. 부서 것이 먼저(<see cref="Current{T}"/>).</summary>
    private static IQueryable<DailyPrayer> Prayers(AppDbContext ctx, DateOnly date)
    {
        var day = (byte)date.Day;
        return ctx.DailyPrayers.Where(p => (p.TenantId != null && p.ExposeDate == date) || (p.TenantId == null && p.DayNo == day));
    }

    /// <summary>
    /// 그날의 암송(0052): 그 주의 내 부서 암송이 있으면 그것, 없으면 그해 몇째 주인지로 공용 암송.
    /// 부서 암송은 실제 날짜라 해가 바뀌면 저절로 공용으로 돌아간다.
    /// </summary>
    private async Task<(WeeklyVerse? Verse, Week Week)> VerseAsync(AppDbContext ctx, DateOnly date, CancellationToken ct)
    {
        var week = Week.Of(date, Rules.WeekStart);
        var mine = await Current(ctx.WeeklyVerses.Where(v => v.TenantId != null && v.StartDate == week.Start)).FirstOrDefaultAsync(ct);
        if (mine is not null) return (mine, week);
        var no = (byte)week.NumberInYear;
        return (await Current(ctx.WeeklyVerses.Where(v => v.TenantId == null && v.WeekNo == no)).FirstOrDefaultAsync(ct), week);
    }

    private async Task<int> SeasonPointsAsync(AppDbContext ctx, int participantId, DateOnly today, CancellationToken ct)
    {
        var season = await SeasonAsync(ctx, today.Year, ct);
        var (from, to) = season.ScoringRange(today);
        return await ctx.PointLedgers.Where(l => l.ParticipantId == participantId && l.MissionDate >= from && l.MissionDate <= to)
            .SumAsync(l => (int?)l.Points, ct) ?? 0;
    }

    public static string WindowMessage(WindowResult w, int graceDays) => w switch
    {
        WindowResult.Future => "아직 오지 않은 날이에요.",
        WindowResult.TooOld => $"이 날짜는 할 수 없어요 (오늘 포함 {graceDays}일 안만 가능).",
        WindowResult.BeforeSeason => "곧 시작해요. 시즌 시작일부터 미션을 할 수 있어요.",
        _ => "",
    };

    /// <summary>말씀 · 기도 · 암송 항목 누르기. 한 번 켠 항목은 꺼지지 않고, 다시 눌러도 점수는 늘지 않는다(R-21 · R-31 · R-51).</summary>
    public async Task<MissionResult> PressAsync(int participantId, MissionItem item, DateOnly date, CancellationToken ct = default)
    {
        if (item == MissionItem.QuizCorrect) throw new ArgumentException("퀴즈는 AnswerQuizAsync 로 해요.", nameof(item));
        await UseRulesForAsync(date, ct);
        var today = Today;
        await using var ctx = db.Create();
        var season = await SeasonAsync(ctx, date.Year, ct);
        var w = MissionWindow.Check(today, date, Rules.GraceDays, season.StartDate);
        if (w != WindowResult.Open) return Fail(WindowMessage(w, Rules.GraceDays));
        var rule = Rules.Item(item);
        if (!rule.Enabled) return Fail("이 부서에서는 쓰지 않는 항목이에요.");

        var act = new Activity { TenantId = tenant.Current.Id, ParticipantId = participantId, MissionItem = item.Code(), MissionDate = date,
            Points = rule.Points, PerformedUtc = time.GetUtcNow().UtcDateTime };
        switch (item.Mission())
        {
            case Mission.Word:
                act.DailyWordId = await Current(ctx.DailyWords.Where(x => x.ExposeDate == date)).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
                if (act.DailyWordId is null) return Fail("오늘은 말씀이 없어요.");   // R-22
                break;
            case Mission.Prayer:
                act.DailyPrayerId = await Current(Prayers(ctx, date)).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
                if (act.DailyPrayerId is null) return Fail("오늘은 기도가 없어요.");
                break;
            case Mission.Recite:
                var (v, week) = await VerseAsync(ctx, date, ct);
                if (v is null) return Fail("이번 주 암송 말씀이 없어요.");
                act.WeeklyVerseId = v.Id;
                act.ReciteStartDate = week.Start;
                if (await ctx.Activities.AnyAsync(a => a.ParticipantId == participantId && a.MissionItem == "Recite" && a.ReciteStartDate == week.Start, ct))
                    return await Already(ctx, participantId, today, "이번 주 암송은 이미 완료했어요.", ct);
                break;
        }
        if (item.Mission() != Mission.Recite &&
            await ctx.Activities.AnyAsync(a => a.ParticipantId == participantId && a.MissionDate == date && a.MissionItem == act.MissionItem, ct))
            return await Already(ctx, participantId, today, "이미 했어요.", ct);

        return await RecordAsync(ctx, act, today, ct);
    }

    /// <summary>오늘 이 항목을 취소할 수 있나(R-24): 말씀 · 기도는 오늘, 암송은 이번 주 날짜에서. 퀴즈는 없음.</summary>
    public bool CanUndo(MissionItem item, DateOnly date)
    {
        var today = Today;
        return item.Mission() switch
        {
            Mission.Quiz => false,
            Mission.Recite => Week.Of(date, Rules.WeekStart).Start == Week.Of(today, Rules.WeekStart).Start,
            _ => date == today,
        };
    }

    /// <summary>
    /// 완료 취소(R-24, ADR 0070): 수행 기록 · 원장 줄을 지워 점수를 빼고, 줄어든 시즌 점수 아래로 내려간 축하 단계 도달 기록도 지운다.
    /// 이미 취소된 것을 또 보내면 0점으로 성공(멱등). Added 는 빠진 점수(0 이하).
    /// </summary>
    public async Task<MissionResult> UndoAsync(int participantId, MissionItem item, DateOnly date, CancellationToken ct = default)
    {
        if (item == MissionItem.QuizCorrect) throw new ArgumentException("퀴즈는 취소할 수 없어요.", nameof(item));
        await UseRulesForAsync(date, ct);
        var today = Today;
        if (!CanUndo(item, date)) return Fail(item == MissionItem.Recite ? "이번 주 암송만 취소할 수 있어요." : "오늘 한 것만 취소할 수 있어요.");
        await using var ctx = db.Create();
        var code = item.Code();
        var q = ctx.Activities.Where(a => a.ParticipantId == participantId && a.MissionItem == code);
        var weekStart = Week.Of(date, Rules.WeekStart).Start;
        var act = item == MissionItem.Recite
            ? await q.FirstOrDefaultAsync(a => a.ReciteStartDate == weekStart, ct)
            : await q.FirstOrDefaultAsync(a => a.MissionDate == date, ct);
        if (act is null) return await Already(ctx, participantId, today, "이미 취소했어요.", ct);

        var lines = await ctx.PointLedgers.Where(l => l.ActivityId == act.Id).ToListAsync(ct);
        var points = lines.Sum(l => l.Points);
        ctx.PointLedgers.RemoveRange(lines);   // 원장이 수행 기록을 가리키므로 원장 먼저
        await ctx.SaveChangesAsync(ct);
        ctx.Activities.Remove(act);
        await ctx.SaveChangesAsync(ct);

        var total = await SeasonPointsAsync(ctx, participantId, today, ct);
        var year = (short)today.Year;
        var above = await ctx.MilestonesReached.Where(m => m.ParticipantId == participantId && m.Year == year && m.Threshold > total).ToListAsync(ct);
        if (above.Count > 0)
        {
            ctx.MilestonesReached.RemoveRange(above);   // 다시 넘으면 또 축하(R-101)
            await ctx.SaveChangesAsync(ct);
        }
        return new MissionResult(true, -points, total, [], points > 0 ? $"취소했어요 · -{points}점" : "취소했어요");
    }

    /// <summary>퀴즈 답하기(R-40 ~ R-44). 정답 판정은 서버에서 저장된 정답과 비교한다.</summary>
    public async Task<MissionResult> AnswerQuizAsync(int participantId, DateOnly date, int? choiceId, string? text, CancellationToken ct = default)
    {
        await UseRulesForAsync(date, ct);
        var today = Today;
        await using var ctx = db.Create();
        var season = await SeasonAsync(ctx, date.Year, ct);
        var w = MissionWindow.Check(today, date, Rules.GraceDays, season.StartDate);
        if (w != WindowResult.Open) return Fail(WindowMessage(w, Rules.GraceDays));
        var rule = Rules.Item(MissionItem.QuizCorrect);
        if (!rule.Enabled) return Fail("이 부서에서는 쓰지 않는 항목이에요.");

        var quiz = await Current(ctx.DailyQuizzes.Include(q => q.AcceptedAnswers).Where(q => q.ExposeDate == date)).FirstOrDefaultAsync(ct);
        if (quiz is null) return Fail("오늘은 퀴즈가 없어요.");
        var attempts = await ctx.QuizAttempts.Where(a => a.ParticipantId == participantId && a.MissionDate == date).ToListAsync(ct);
        var max = Rules.QuizMaxAttempts;
        switch (QuizPolicy.State(attempts.Count, attempts.Any(a => a.IsCorrect), max))
        {
            case QuizState.Solved: return await Already(ctx, participantId, today, "이미 맞혔어요.", ct);
            case QuizState.OutOfAttempts: return Fail($"오늘은 {max}번 도전했어요. 내일 새 퀴즈에 도전해요.");
        }

        bool correct;
        if (quiz.Format == QuizFormat.Choice)
        {
            if (choiceId is null) return Fail("답을 골라 주세요.");
            correct = choiceId == quiz.CorrectChoiceId;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(text)) return Fail("답을 써 주세요.");
            correct = quiz.AcceptedAnswers.Any(a => a.NormalizedText == QuizPolicy.Normalize(text));
        }

        var no = (byte)(attempts.Count + 1);
        ctx.QuizAttempts.Add(new QuizAttempt { TenantId = tenant.Current.Id, ParticipantId = participantId, MissionDate = date, AttemptNo = no,
            DailyQuizId = quiz.Id, SelectedChoiceId = choiceId, AnswerText = quiz.Format == QuizFormat.Text ? text?.Trim() : null,
            IsCorrect = correct, AttemptedUtc = time.GetUtcNow().UtcDateTime });

        if (!correct)
        {
            try { await ctx.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Fail("방금 다른 곳에서 답했어요. 화면을 새로 고쳐 주세요."); }   // UQ_QuizAttempt_No
            var msg = no < max ? $"오답이에요 ({no}/{max}) 한 번 더 도전해요." : $"오늘은 {max}번 도전했어요. 내일 새 퀴즈에 도전해요.";
            return new MissionResult(true, 0, await SeasonPointsAsync(ctx, participantId, today, ct), [], msg, Correct: false);
        }

        var act = new Activity { TenantId = tenant.Current.Id, ParticipantId = participantId, MissionItem = MissionItem.QuizCorrect.Code(),
            MissionDate = date, DailyQuizId = quiz.Id, Points = rule.Points, PerformedUtc = time.GetUtcNow().UtcDateTime };
        var r = await RecordAsync(ctx, act, today, ct);
        return r with { Correct = true, Message = r.Added > 0 ? $"정답! +{r.Added}점" : "정답!" };
    }

    /// <summary>끝난 퀴즈의 정답 보기(화면에 "정답" 표시용). 끝나기 전에는 부르지 않는다.</summary>
    public async Task<int?> CorrectChoiceAsync(int quizId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return await ctx.DailyQuizzes.Where(q => q.Id == quizId).Select(q => q.CorrectChoiceId).SingleOrDefaultAsync(ct);
    }

    /// <summary>최근 7일(고른 날 포함, R-74) 하루 점수.</summary>
    public async Task<IReadOnlyList<(DateOnly Date, int Points)>> RecentDaysAsync(int participantId, DateOnly end, CancellationToken ct = default)
    {
        var start = end.AddDays(-6);
        await using var ctx = db.Create();
        var rows = await ctx.PointLedgers.Where(l => l.ParticipantId == participantId && l.MissionDate >= start && l.MissionDate <= end)
            .GroupBy(l => l.MissionDate).Select(g => new { g.Key, P = g.Sum(x => x.Points) }).ToListAsync(ct);
        return Enumerable.Range(0, 7).Select(i => start.AddDays(i)).Select(d => (d, rows.FirstOrDefault(r => r.Key == d)?.P ?? 0)).ToList();
    }

    /// <summary>
    /// 한 달 날짜별 점수(R-71 · R-76)와 완주일(R-72: 말씀 · 기도 · 퀴즈 · 그 주 암송을 모두 함).
    /// 이번 달이면 오늘까지만(R-73).
    /// </summary>
    public async Task<IReadOnlyList<DayScore>> MonthAsync(int participantId, int year, int month, CancellationToken ct = default)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var end = last < Today ? last : Today;
        if (end < first) return [];
        await using var ctx = db.Create();
        var acts = await ctx.Activities.AsNoTracking()
            .Where(a => a.ParticipantId == participantId && a.MissionDate >= first.AddDays(-6) && a.MissionDate <= end)
            .Select(a => new { a.MissionItem, a.MissionDate, a.Points, a.ReciteStartDate }).ToListAsync(ct);
        var reciteWeeks = await ctx.Activities.Where(a => a.ParticipantId == participantId && a.MissionItem == "Recite")
            .Select(a => a.ReciteStartDate!.Value).ToListAsync(ct);
        var weekStarts = new List<DateOnly>();   // 이 달에 걸친 주 가운데 암송이 있는 주의 시작일
        for (var w = Week.Of(first, Rules.WeekStart); w.Start <= end; w = new Week(w.Start.AddDays(7)))
            if ((await VerseAsync(ctx, w.Start, ct)).Verse is not null) weekStarts.Add(w.Start);

        var list = new List<DayScore>();
        for (var d = first; d <= end; d = d.AddDays(1))
        {
            var day = acts.Where(a => a.MissionDate == d).ToList();
            int Sum(Mission m) => day.Where(a => MissionItemCodes.Parse(a.MissionItem).Mission() == m).Sum(a => a.Points);
            bool Has(Mission m) => day.Any(a => MissionItemCodes.Parse(a.MissionItem).Mission() == m);
            var ws = Week.Of(d, Rules.WeekStart).Start;
            var recited = weekStarts.Contains(ws) && reciteWeeks.Contains(ws);
            list.Add(new DayScore(d, Sum(Mission.Word), Sum(Mission.Prayer), Sum(Mission.Quiz), Sum(Mission.Recite),
                Has(Mission.Word) && Has(Mission.Prayer) && Has(Mission.Quiz) && recited));
        }
        return list;
    }

    /// <summary>
    /// 이어온 날 수: 끝날(보통 오늘)부터 거슬러 미션을 하나라도 한 날이 이어진 만큼.
    /// 끝날에 아직 한 게 없어도 어제까지 이어졌으면 끊기지 않은 것으로 센다(오늘 아직 안 했을 뿐).
    /// </summary>
    public async Task<int> StreakAsync(int participantId, DateOnly end, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var from = end.AddDays(-400);
        var days = (await ctx.Activities.AsNoTracking()
            .Where(a => a.ParticipantId == participantId && a.MissionDate >= from && a.MissionDate <= end)
            .Select(a => a.MissionDate).Distinct().ToListAsync(ct)).ToHashSet();
        var d = days.Contains(end) ? end : end.AddDays(-1);
        var n = 0;
        while (days.Contains(d)) { n++; d = d.AddDays(-1); }
        return n;
    }

    /// <summary>다음 축하 단계까지 남은 점수(없으면 null).</summary>
    public static int? NextMilestone(RuleSet rules, int seasonPoints) => rules.Milestones.Where(m => m > seasonPoints).Order().Cast<int?>().FirstOrDefault();

    public async Task<DateOnly> SeasonStartAsync(int year, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return (await SeasonAsync(ctx, year, ct)).StartDate;
    }

    private static MissionResult Fail(string message) => new(false, 0, 0, [], message);

    private async Task<MissionResult> Already(AppDbContext ctx, int participantId, DateOnly today, string message, CancellationToken ct) =>
        new(true, 0, await SeasonPointsAsync(ctx, participantId, today, ct), [], message);   // R-65: 0점이면 단계 검사도 안 함

    private async Task<MissionResult> RecordAsync(AppDbContext ctx, Activity act, DateOnly today, CancellationToken ct)
    {
        ctx.Activities.Add(act);
        try
        {
            await ctx.SaveChangesAsync(ct);   // 고유 인덱스가 중복을 막는다(R-61 · R-51)
        }
        catch (DbUpdateException)
        {
            ctx.ChangeTracker.Clear();
            return await Already(ctx, act.ParticipantId, today, "이미 했어요.", ct);
        }

        if (act.Points > 0)
        {
            ctx.PointLedgers.Add(new PointLedger { TenantId = act.TenantId, ParticipantId = act.ParticipantId, MissionDate = act.MissionDate,
                MissionItem = act.MissionItem, Points = act.Points, ActivityId = act.Id, RecordedUtc = act.PerformedUtc });
            await ctx.SaveChangesAsync(ct);
        }

        var total = await SeasonPointsAsync(ctx, act.ParticipantId, today, ct);
        var reached = act.Points > 0 ? await NewMilestonesAsync(ctx, act.ParticipantId, today.Year, total, ct) : [];
        return new MissionResult(true, act.Points, total, reached, act.Points > 0 ? $"+{act.Points}점" : "했어요");
    }

    /// <summary>R-101: 새로 넘은 단계를 시즌당 한 번 기록한다.</summary>
    private async Task<IReadOnlyList<int>> NewMilestonesAsync(AppDbContext ctx, int participantId, int year, int total, CancellationToken ct)
    {
        var done = await ctx.MilestonesReached.Where(m => m.ParticipantId == participantId && m.Year == year).Select(m => m.Threshold).ToListAsync(ct);
        var fresh = PointMath.NewMilestones(total, Rules.Milestones, done);
        if (fresh.Count == 0) return fresh;
        foreach (var t in fresh)
            ctx.MilestonesReached.Add(new MilestoneReached { TenantId = tenant.Current.Id, ParticipantId = participantId, Year = (short)year,
                Threshold = t, ReachedUtc = time.GetUtcNow().UtcDateTime });
        try { await ctx.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return []; }   // 동시에 다른 요청이 기록함
        return fresh;
    }
}
