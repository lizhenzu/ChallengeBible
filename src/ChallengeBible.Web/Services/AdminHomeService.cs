using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record PendingRequest(string Name, bool IsGuardian);
public sealed record EmptyContent(int Days, DateOnly First, bool Word, bool Quiz);
public sealed record MissingMember(string Name, string? Group, int? DaysAgo);
public sealed record AdminHome(
    DateOnly Today, DateTimeOffset NowLocal,
    int PendingCount, IReadOnlyList<PendingRequest> Pending, EmptyContent? Empty,
    int Members, int TodayCount, IReadOnlyDictionary<Mission, int> TodayByMission, DateOnly WeekStart,
    IReadOnlyList<(DateOnly Date, int Count)> Week,
    IReadOnlyList<MissingMember> Missing)
{
    public int WeekAverage => Week.Count == 0 ? 0 : (int)Math.Round(Week.Average(w => w.Count));
    public (DateOnly Date, int Count) Best => Week.Count == 0 ? default : Week.OrderByDescending(w => w.Count).ThenByDescending(w => w.Date).First();
}

/// <summary>
/// 부서 관리 홈(확정 시안: 관리-홈, API 계약 `/{slug}/admin`): 할 일 · 오늘 참여 · 최근 7일 · 오늘 안 한 친구.
/// 모든 조회는 이 부서 안에서만 된다(전역 필터 + RLS). "오늘"은 부서 시간대(R-01).
/// </summary>
public sealed class AdminHomeService(IAppDb db, TenantContext tenant, TenantClock clock)
{
    private const int LookAheadDays = 7;

    public async Task<AdminHome> GetAsync(CancellationToken ct = default)
    {
        var tz = tenant.Current.TimeZoneId;
        var today = clock.Today(tz);
        var nowLocal = TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(tz));
        await using var ctx = db.Create();

        // 소속 참여자: 승인된 소속의 활성 참여자(정지 · 비활성 제외)
        var members = await (from p in ctx.Participants
                             join m in ctx.Memberships on p.MembershipId equals m.Id
                             join a in ctx.Accounts on m.AccountId equals a.Id
                             where p.IsActive && m.Status == MembershipStatus.Approved
                             select new { p.Id, a.DisplayName, m.GroupLabel }).ToListAsync(ct);
        var memberIds = members.Select(m => m.Id).ToHashSet();

        // 기다리는 신청
        var requested = await (from m in ctx.Memberships.Include(x => x.Roles)
                               join a in ctx.Accounts on m.AccountId equals a.Id
                               where m.Status == MembershipStatus.Requested
                               orderby m.RequestedUtc
                               select new { a.DisplayName, m.Roles }).ToListAsync(ct);
        var pending = requested.Take(3).Select(r => new PendingRequest(r.DisplayName, r.Roles.Any(x => x.Role == Roles.Guardian) && r.Roles.All(x => x.Role != Roles.Participant))).ToList();

        // 최근 7일(오늘 포함) 참여: 날짜별 하루라도 한 참여자
        var weekFrom = today.AddDays(-6);
        var weekStart = today.AddDays(-(int)today.DayOfWeek);   // 암송은 한 주에 한 번이라 이번 주(일요일 시작) 누적
        var from = weekStart < weekFrom ? weekStart : weekFrom;
        var acts = (await ctx.Activities.AsNoTracking()
            .Where(a => a.MissionDate >= from && a.MissionDate <= today)
            .Select(a => new { a.ParticipantId, a.MissionDate, a.MissionItem }).ToListAsync(ct))
            .Where(a => memberIds.Contains(a.ParticipantId))
            .Select(a => (a.ParticipantId, a.MissionDate, Mission: MissionItemCodes.Parse(a.MissionItem).Mission()))
            .ToList();

        var week = Enumerable.Range(0, 7).Select(i => weekFrom.AddDays(i))
            .Select(d => (d, acts.Where(a => a.MissionDate == d).Select(a => a.ParticipantId).Distinct().Count())).ToList();
        var todayActs = acts.Where(a => a.MissionDate == today).ToList();
        var byMission = new Dictionary<Mission, int>
        {
            [Mission.Word] = todayActs.Where(a => a.Mission == Mission.Word).Select(a => a.ParticipantId).Distinct().Count(),
            [Mission.Quiz] = todayActs.Where(a => a.Mission == Mission.Quiz).Select(a => a.ParticipantId).Distinct().Count(),
            [Mission.Prayer] = todayActs.Where(a => a.Mission == Mission.Prayer).Select(a => a.ParticipantId).Distinct().Count(),
            [Mission.Recite] = acts.Where(a => a.MissionDate >= weekStart && a.Mission == Mission.Recite).Select(a => a.ParticipantId).Distinct().Count(),
        };
        var todayDone = todayActs.Select(a => a.ParticipantId).Distinct().ToHashSet();

        // 오늘 아직 안 한 친구: 마지막 미션이 오래된 순(한 번도 안 한 친구가 맨 앞)
        var lastDates = await ctx.Activities.AsNoTracking().Where(a => a.MissionDate <= today)
            .GroupBy(a => a.ParticipantId).Select(g => new { g.Key, Last = g.Max(x => x.MissionDate) }).ToListAsync(ct);
        var last = lastDates.ToDictionary(x => x.Key, x => x.Last);
        var missing = members.Where(m => !todayDone.Contains(m.Id))
            .Select(m => new MissingMember(m.DisplayName, m.GroupLabel, last.TryGetValue(m.Id, out var d) ? today.DayNumber - d.DayNumber : null))
            .OrderByDescending(m => m.DaysAgo ?? int.MaxValue).ThenBy(m => m.Name, StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false))
            .ToList();

        return new(today, nowLocal, requested.Count, pending, await EmptyContentAsync(ctx, today, ct),
            members.Count, todayDone.Count, byMission, weekStart, week, missing);
    }

    /// <summary>앞으로 7일 중 말씀 · 퀴즈 콘텐츠가 없는 날(공개된 현재 개정, 공용 또는 이 부서 것). 기도 · 암송은 번호로 반복해서 대상이 아니다(0052 · 0053).</summary>
    private static async Task<EmptyContent?> EmptyContentAsync(AppDbContext ctx, DateOnly today, CancellationToken ct)
    {
        var last = today.AddDays(LookAheadDays - 1);
        var words = (await ctx.DailyWords.AsNoTracking().Where(w => w.IsCurrent && w.Status == ContentStatus.Published && w.ExposeDate >= today && w.ExposeDate <= last)
            .Select(w => w.ExposeDate).ToListAsync(ct)).ToHashSet();
        var quizzes = (await ctx.DailyQuizzes.AsNoTracking().Where(q => q.IsCurrent && q.Status == ContentStatus.Published && q.ExposeDate >= today && q.ExposeDate <= last)
            .Select(q => q.ExposeDate).ToListAsync(ct)).ToHashSet();
        var days = Enumerable.Range(0, LookAheadDays).Select(today.AddDays)
            .Select(d => (Date: d, NoWord: !words.Contains(d), NoQuiz: !quizzes.Contains(d))).Where(x => x.NoWord || x.NoQuiz).ToList();
        return days.Count == 0 ? null : new(days.Count, days[0].Date, days[0].NoWord, days[0].NoQuiz);
    }
}
