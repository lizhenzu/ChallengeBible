using ChallengeBible.Domain.Seasons;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>참여자 화면의 한 사람. Detail = 반(학생) · 가족 관계(부모님) · 담당 반(선생님). LastDay = 마지막으로 점수를 받은 날.</summary>
public sealed record ParticipantRow(string UserName, string Name, RankKind Kind, string? Detail, DateOnly? LastDay, bool TodayDone, int Points, bool Me, bool HasPhoto);

public sealed record ParticipantBoard(DateOnly Today, IReadOnlyList<ParticipantRow> People)
{
    public int DoneToday => People.Count(p => p.TodayDone);
}

/// <summary>
/// 참여자 화면(2026-11-27 사용자 민트 시안 「참여자」, R-214): 이 부서 승인 소속원 가운데 <b>이번 시즌 점수가 1점 이상</b>인 사람.
/// 부모님은 가족팀에 들어 있을 때만(팀이 없는 부모님은 나오지 않는다), 선생님은 팀 없이 혼자 한다. 순위를 꺼도 점수는 보인다.
/// 구분은 순위와 같다(교사 · 관리자 = 선생님, 보호자 = 부모님, 나머지 = 학생). 아이디 = 가입 이메일 앞부분(UserName).
/// </summary>
public sealed class RosterService(IAppDb db, TenantContext tenant, TenantClock clock, ClassService classes)
{
    public async Task<ParticipantBoard> ParticipantsAsync(CancellationToken ct = default)
    {
        var today = clock.Today(tenant.Current.TimeZoneId);
        await using var ctx = db.Create();
        var adminIds = await DeptMembership.AdminIdsAsync(ctx, ct);
        var members = await DeptMembership.PeopleAsync(ctx, ct);
        if (members.Count == 0) return new ParticipantBoard(today, []);
        var ids = members.Select(m => m.UserName).ToList();

        var start = await ctx.TenantSeasons.Where(s => s.Year == today.Year).Select(s => (DateOnly?)s.StartDate).SingleOrDefaultAsync(ct);
        var season = start is { } d ? Season.Create(today.Year, d) : Season.DefaultFor(today.Year);
        var (from, to) = season.ScoringRange(today);
        var points = new Dictionary<string, int>();
        if (from <= to)
            points = await ctx.MissionActivities.Where(l => ids.Contains(l.UserName) && l.MissionDate >= from && l.MissionDate <= to)
                .GroupBy(l => l.UserName).Select(g => new { g.Key, P = g.Sum(x => x.Points) }).ToDictionaryAsync(x => x.Key, x => x.P, ct);
        var lastDays = await ctx.MissionActivities.Where(a => ids.Contains(a.UserName) && a.MissionDate <= today && a.Points > 0)
            .GroupBy(a => a.UserName).Select(g => new { g.Key, D = g.Max(x => x.MissionDate) }).ToDictionaryAsync(x => x.Key, x => x.D, ct);
        var doneToday = (await ctx.MissionActivities.Where(a => a.MissionDate == today).Select(a => a.UserName).Distinct().ToListAsync(ct)).ToHashSet();

        // 부모님: 가족팀 관계(활성 팀 · 활성 구성원만)
        var family = await (from m in ctx.FamilyMembers join f in ctx.FamilyTeams on m.FamilyTeamId equals f.Id
                            where m.IsActive && f.IsActive && ids.Contains(m.UserName) select new { m.UserName, m.RoleCode }).ToListAsync(ct);
        var relation = family.GroupBy(x => x.UserName).ToDictionary(g => g.Key, g => FamilyRelation.FromRole(g.First().RoleCode));
        // 반: 계정 → 사람 번호 → 이번 학기 반
        var userIds = members.Select(m => m.UserId).ToList();
        var personOf = (await ctx.People.AsNoTracking().Where(p => p.UserId != null && userIds.Contains(p.UserId)).Select(p => new { p.Id, p.UserId }).ToListAsync(ct))
            .GroupBy(p => p.UserId!).ToDictionary(g => g.Key, g => g.First().Id);
        var classOf = await classes.PersonClassNamesAsync(ct);
        var photos = (await ctx.UserSettings.AsNoTracking().Where(s => ids.Contains(s.UserName) && s.PhotoPath != null).Select(s => s.UserName).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = new List<ParticipantRow>();
        foreach (var m in members)
        {
            var pts = points.GetValueOrDefault(m.UserName);
            if (pts < 1) continue;   // 점수가 1점 이상인 사람만
            var roles = UserRole.ScreenRoles(m.RoleCode, adminIds.Contains(m.UserId)).ToList();
            var kind = roles.Any(x => x is Roles.Teacher or Roles.TenantAdmin) ? RankKind.Teacher : roles.Contains(Roles.Guardian) ? RankKind.Parent : RankKind.Student;
            string? detail;
            if (kind == RankKind.Parent)
            {
                if (!relation.TryGetValue(m.UserName, out var rel)) continue;   // 팀이 없는 부모님은 나오지 않는다
                detail = FamilyTeamService.RelationLabel(rel);
            }
            else
            {
                var cls = personOf.TryGetValue(m.UserId, out var pid) && classOf.TryGetValue(pid, out var c) ? c.Class : null;
                detail = cls is null ? null : kind == RankKind.Teacher ? cls + " 담당" : cls;
            }
            rows.Add(new ParticipantRow(m.UserName, m.Name, kind, detail, lastDays.TryGetValue(m.UserName, out var ld) ? ld : null, doneToday.Contains(m.UserName), pts, m.DeptRoleId == tenant.MembershipId, photos.Contains(m.UserName)));
        }
        return new ParticipantBoard(today, rows);
    }
}
