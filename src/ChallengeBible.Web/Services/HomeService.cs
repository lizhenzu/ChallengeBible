using ChallengeBible.Domain.Missions;
using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>미션 네 가지의 오늘 상태(홈 말씀 카드 "미션 4칸").</summary>
public sealed record HomeMission(string Key, string Label, bool Has, bool Done, string State);
public sealed record HomeWeekDay(DateOnly Date, int MissionPoints, bool AllFour, int Chapters);
public sealed record HomeDept(int Members, int TodayPeople, int? YearChapters, int? MyOrderToday);

/// <summary>부서 홈 메인(확정 시안: 홈메인)에 필요한 것. 프로그램이 꺼졌거나 참여자가 아니면 그 부분은 null · 0.</summary>
public sealed class HomeView
{
    public DateOnly Today { get; init; }
    public int SeasonYear { get; init; }
    public int SeasonDay { get; init; }
    public bool Participant { get; init; }
    public int SeasonPoints { get; init; }
    public int Streak { get; init; }
    public int FullDays { get; init; }
    public int DayPoints { get; init; }
    public int? NextMilestone { get; init; }
    public RuleSet Rules { get; init; } = RuleSet.Default;
    // 오늘의 말씀 띠
    public string? VerseText { get; init; }
    public string? VerseRef { get; init; }
    public string? WordRange { get; init; }
    public string? ReciteRange { get; init; }
    public DateOnly? ReciteStart { get; init; }
    public DateOnly? ReciteEnd { get; init; }
    public string? PrayerTrait { get; init; }
    // 말씀 카드
    public IReadOnlyList<HomeMission> Missions { get; init; } = [];
    public HomeMission? NextMission { get; init; }
    // 통독 카드(시작 전이면 null)
    public ReadingSummary? Reading { get; init; }
    public string? ResumeText { get; init; }
    public IReadOnlyList<HomeWeekDay> Week { get; init; } = [];
    public HomeDept? Dept { get; init; }
}

/// <summary>
/// 부서 홈 메인(확정 시안: 홈메인, 사용자 확정 8가지). 이미 있는 계산을 그대로 쓴다:
/// 미션 · 점수 = MissionService, 통독 = ReadingService(시작한 사람만 — 홈을 연다고 회차를 열지 않는다), 말씀 첫 절 = BibleTextService.
/// 부서 숫자 중 "올해 함께 읽은 성경"은 참여자 현황이 공개된 부서(또는 관리자 · 교사)에만 보인다(R-159와 같은 기준).
/// </summary>
public sealed class HomeService(IAppDb db, TenantContext tenant, MissionService missions, ReadingService reading, BibleTextService bible, RuleService rules)
{
    public async Task<HomeView> GetAsync(CancellationToken ct = default)
    {
        var t = tenant.Current;
        var today = missions.Today;
        var r = await rules.ForDateAsync(today, ct);
        var pid = t.MissionEnabled ? await missions.ParticipantIdAsync(ct) : null;
        var seasonStart = await missions.SeasonStartAsync(today.Year, ct);

        MissionDay? day = null;
        int streak = 0, fullDays = 0;
        var week = Week.Of(today, r.WeekStart);
        var weekScores = new Dictionary<DateOnly, DayScore>();
        if (t.MissionEnabled)
        {
            day = await missions.GetDayAsync(pid ?? 0, today, ct);   // 참여자가 아니어도 오늘 콘텐츠는 보인다
            if (pid is int p)
            {
                streak = await missions.StreakAsync(p, today, ct);
                for (var m = new DateOnly(seasonStart.Year, seasonStart.Month, 1); m <= today; m = m.AddMonths(1))
                    foreach (var s in await missions.MonthAsync(p, m.Year, m.Month, ct))
                    {
                        if (s.Complete && s.Date >= seasonStart) fullDays++;
                        if (s.Date >= week.Start && s.Date <= week.End) weekScores[s.Date] = s;
                    }
                // 이번 주가 지난달에 걸치면 그 날짜도
                if (week.Start.Month != today.Month || week.Start.Year != today.Year)
                    foreach (var s in await missions.MonthAsync(p, week.Start.Year, week.Start.Month, ct))
                        if (s.Date >= week.Start) weekScores[s.Date] = s;
            }
        }

        // 오늘의 말씀 띠: 말씀 첫 절(개역개정)
        string? verseText = null, verseRef = null;
        if (day?.Word is { } w)
        {
            var first = new BibleRef(w.Ref.BookId, w.Ref.StartChapter, w.Ref.StartVerse, w.Ref.StartChapter, w.Ref.StartVerse);
            verseText = (await bible.RangeAsync(first, [BibleTranslation.Krv], ct)).FirstOrDefault()?.Texts.GetValueOrDefault(BibleTranslation.Krv);
            verseRef = $"{BibleBooks.NameOf(w.Ref.BookId)} {w.Ref.StartChapter}:{w.Ref.StartVerse}";
        }

        var list = new List<HomeMission>();
        if (day is not null)
        {
            bool Did(Mission m) => day.Done.Any(i => i.Mission() == m);
            var quizDone = day.QuizState.State != QuizState.CanTry;
            list.Add(new("word", "말씀", day.Word is not null, Did(Mission.Word), day.Word is null ? "없음" : Did(Mission.Word) ? "완료" : "할 차례"));
            list.Add(new("quiz", "퀴즈", day.Quiz is not null, quizDone, day.Quiz is null ? "없음" : day.QuizState.Solved ? "완료" : quizDone ? "끝남" : "할 차례"));
            list.Add(new("prayer", "기도", day.Prayer is not null, Did(Mission.Prayer), day.Prayer is null ? "없음" : Did(Mission.Prayer) ? "완료" : "할 차례"));
            list.Add(new("recite", "암송", day.Recite is not null, day.RecitedThisWeek, day.Recite is null ? "없음" : day.RecitedThisWeek ? "이번 주 완료" : "할 차례"));
        }

        ReadingSummary? sum = null;
        string? resumeText = null;
        if (t.ReadingEnabled && await reading.StartedAsync(ct))
        {
            sum = await reading.SummaryAsync(ct);
            var rs = sum.Resume;
            resumeText = (await bible.RangeAsync(new BibleRef(rs.BookId, rs.Chapter, rs.Verse, rs.Chapter, rs.Verse), [BibleTranslation.Krv], ct))
                .FirstOrDefault()?.Texts.GetValueOrDefault(BibleTranslation.Krv);
        }

        var weekDays = Enumerable.Range(0, 7).Select(i => week.Start.AddDays(i)).Select(d =>
        {
            var s = weekScores.GetValueOrDefault(d);
            return new HomeWeekDay(d, s?.Total ?? 0, s?.Complete == true, sum?.Daily.GetValueOrDefault(d) ?? 0);
        }).ToList();

        return new HomeView
        {
            Today = today, SeasonYear = today.Year, SeasonDay = today >= seasonStart ? today.DayNumber - seasonStart.DayNumber + 1 : 0,
            Participant = pid is not null, SeasonPoints = day?.SeasonPoints ?? 0, Streak = streak, FullDays = fullDays, DayPoints = pid is null ? 0 : day?.DayPoints ?? 0,
            NextMilestone = MissionService.NextMilestone(r, day?.SeasonPoints ?? 0), Rules = r,
            VerseText = verseText, VerseRef = verseRef, WordRange = day?.Word?.Range,
            ReciteRange = day?.Recite?.Range, ReciteStart = day?.Recite?.Start, ReciteEnd = day?.Recite?.End, PrayerTrait = day?.Prayer?.Trait,
            Missions = list, NextMission = list.FirstOrDefault(m => m.Has && !m.Done),
            Reading = sum, ResumeText = resumeText, Week = weekDays,
            Dept = await DeptAsync(today, pid, ct),
        };
    }

    private async Task<HomeDept> DeptAsync(DateOnly today, int? pid, CancellationToken ct)
    {
        var t = tenant.Current;
        await using var ctx = db.Create();
        var members = await ctx.Memberships.CountAsync(m => m.Status == MembershipStatus.Approved, ct);
        var firsts = t.MissionEnabled
            ? await ctx.Activities.Where(a => a.MissionDate == today).GroupBy(a => a.ParticipantId)
                .Select(g => new { g.Key, First = g.Min(a => a.PerformedUtc) }).ToListAsync(ct)
            : [];
        int? order = null;
        if (pid is int p && firsts.FirstOrDefault(f => f.Key == p) is { } mine)
            order = firsts.Count(f => f.First < mine.First) + 1;

        int? chapters = null;
        if (t.ReadingEnabled && (t.ReadingBoardPublic || tenant.IsStaff))
        {
            // 통독 기록은 계정 범위 — 참여자 현황과 같은 기준(공개 또는 관리자 · 교사)에서만 이 부서 승인 소속원 것을 센다(DB 는 fn_ReadingRead)
            var ids = await ctx.Memberships.Where(m => m.Status == MembershipStatus.Approved).Select(m => m.AccountId).ToListAsync(ct);
            var from = TimeZoneInfo.ConvertTimeToUtc(new DateTime(today.Year, 1, 1), TimeZoneInfo.FindSystemTimeZoneById(t.TimeZoneId));   // 부서 시간 1월 1일 0시
            chapters = await ctx.ChapterReads.IgnoreQueryFilters().CountAsync(c => ids.Contains(c.AccountId) && c.ReadUtc >= from, ct);
        }
        return new HomeDept(members, firsts.Count, chapters, order);
    }
}
