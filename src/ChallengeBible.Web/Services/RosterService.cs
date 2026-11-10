using ChallengeBible.Domain.Seasons;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>명단의 한 사람. Points = 이번 시즌 점수(순위를 끈 부서는 0, 화면에도 안 보임).</summary>
public sealed record RosterPerson(string Name, string Email, string? GroupLabel, RankKind Kind, bool IsParticipant, int Points, bool TodayDone, bool Me);

public sealed record Roster(DateOnly Today, bool ShowPoints, IReadOnlyList<RosterPerson> People)
{
    public int AllCount => People.Count;
    public int ParticipantCount => People.Count(p => p.IsParticipant);
}

/// <summary>
/// 부서 명단(확정 시안: dept-roster, ADR 0085, R-214 · R-215). 이 부서 승인 소속원 모두가 전체 명단을,
/// 그 가운데 활성 참여자만 참여자 명단으로 본다. 홈 카드의 두 숫자(전체 · 참여자)와 같은 기준이다.
/// 구분은 순위와 같다(교사 · 관리자 = 선생님, 보호자 = 부모님, 나머지 = 학생). 점수는 기본 규칙의 "순위" 스위치가 켜졌을 때만 싣는다.
/// </summary>
public sealed class RosterService(IAppDb db, TenantContext tenant, TenantClock clock, RuleService rules)
{
    public async Task<Roster> GetAsync(CancellationToken ct = default)
    {
        var today = clock.Today(tenant.Current.TimeZoneId);
        var showPoints = (await rules.ForDateAsync(today, ct)).Features.Ranking;
        await using var ctx = db.Create();

        var adminIds = await DeptMembership.AdminIdsAsync(ctx, ct);
        var members = await DeptMembership.PeopleAsync(ctx, ct);

        var points = new Dictionary<string, int>();
        if (showPoints && members.Count > 0)
        {
            var start = await ctx.TenantSeasons.Where(s => s.Year == today.Year).Select(s => (DateOnly?)s.StartDate).SingleOrDefaultAsync(ct);
            var season = start is { } d ? Season.Create(today.Year, d) : Season.DefaultFor(today.Year);
            var (from, to) = season.ScoringRange(today);
            if (from <= to)
            {
                var ids = members.Select(m => m.UserName).ToList();
                points = await ctx.MissionActivities.Where(l => ids.Contains(l.UserName) && l.MissionDate >= from && l.MissionDate <= to)
                    .GroupBy(l => l.UserName).Select(g => new { g.Key, P = g.Sum(x => x.Points) }).ToDictionaryAsync(x => x.Key, x => x.P, ct);
            }
        }
        var doneToday = (await ctx.MissionActivities.Where(a => a.MissionDate == today).Select(a => a.UserName).Distinct().ToListAsync(ct)).ToHashSet();

        var people = members.Select(m =>
        {
            var mine = UserRole.ScreenRoles(m.RoleCode, adminIds.Contains(m.UserId)).ToList();
            var kind = mine.Any(x => x is Roles.Teacher or Roles.TenantAdmin) ? RankKind.Teacher
                : mine.Contains(Roles.Guardian) ? RankKind.Parent : RankKind.Student;
            return new RosterPerson(m.Name, m.Email, null, kind, m.RoleCode == UserRole.StudentCode,
                Math.Max(0, points.GetValueOrDefault(m.UserName)), doneToday.Contains(m.UserName), m.DeptRoleId == tenant.MembershipId);
        }).OrderBy(x => x.Name, StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false)).ToList();
        return new Roster(today, showPoints, people);
    }
}
