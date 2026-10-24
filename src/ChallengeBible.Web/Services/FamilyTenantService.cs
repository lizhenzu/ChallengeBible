using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>고를 수 있는 부서 하나(시안: 가족부서고르기). Team 은 나 또는 자녀가 든 활성 가족팀.</summary>
public sealed record FamilyOption(int TenantId, string Name, string ThemeColor, IReadOnlyList<string> Children,
    string? TeamName, int TeamPoints, IReadOnlyList<string> TeamMembers, bool IAmParticipant, int MyPoints);
/// <summary>다른 부서를 고르면 멈추는 곳: 내가 참여자인 부서(이름, 내 점수, 내가 든 가족팀).</summary>
public sealed record FamilyStop(int TenantId, string Name, int MyPoints, string? TeamName);
public sealed record FamilyChoice(int? Current, IReadOnlyList<FamilyOption> Options, IReadOnlyList<FamilyStop> Participating)
{
    public FamilyOption? CurrentOption => Options.FirstOrDefault(o => o.TenantId == Current);
    /// <summary>tenantId 를 고르면 멈추는 곳(그 부서 밖에서 내가 참여자인 부서).</summary>
    public IReadOnlyList<FamilyStop> StopsIf(int tenantId) => Participating.Where(p => p.TenantId != tenantId).ToList();
}

/// <summary>
/// 가족과 함께할 부서(0046, R-168, 확정 시안: 가족부서고르기). 보호자 계정이 직접 고른다 — 자동으로 정하지 않는다.
/// 내 소속이 여러 부서에 걸쳐 있어 시스템 연결로 읽고(물리 설계 3-3 "가족 부서 바꾸기"), 바꾸는 것도 이 처리기 한 곳에서 한 번에 한다:
/// 고른 부서 밖에서는 내 참여자 역할을 빼고 가족팀 자리를 멈춘다(점수 · 기록은 남음). 실행 사실은 AuditLog 에 남긴다.
/// </summary>
public sealed class FamilyTenantService(ISystemDb systemDb, TenantClock clock, TimeProvider time)
{
    /// <summary>보호자가 아니면 null(어느 부서에서도 보호자 역할이 승인되지 않음).</summary>
    public async Task<FamilyChoice?> GetAsync(int accountId, CancellationToken ct = default)
    {
        await using var sys = systemDb.Create(accountId);
        var mine = await (from m in sys.Memberships
                          join t in sys.Tenants on m.TenantId equals t.Id
                          where m.AccountId == accountId && m.Status == MembershipStatus.Approved && t.Status == TenantStatus.Active
                          select new { m.Id, m.TenantId, t.Name, t.ThemeColor, t.TimeZoneId, Roles = m.Roles.Select(r => r.Role).ToList() }).ToListAsync(ct);
        if (!mine.Any(m => m.Roles.Contains(Roles.Guardian))) return null;
        var current = await sys.Accounts.Where(a => a.Id == accountId).Select(a => a.FamilyTenantId).SingleAsync(ct);

        var options = new List<FamilyOption>();
        var participating = new List<FamilyStop>();
        var ko = StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false);
        foreach (var m in mine.OrderBy(x => x.Name, ko))
        {
            var t = m.TenantId;
            var myPid = await sys.Participants.Where(p => p.TenantId == t && p.MembershipId == m.Id && p.IsActive).Select(p => (int?)p.Id).SingleOrDefaultAsync(ct);
            var children = await (from l in sys.GuardianLinks
                                  join p in sys.Participants on new { l.TenantId, Id = l.ParticipantId } equals new { p.TenantId, p.Id }
                                  join cm in sys.Memberships on new { p.TenantId, Id = p.MembershipId } equals new { cm.TenantId, cm.Id }
                                  join a in sys.Accounts on cm.AccountId equals a.Id
                                  where l.TenantId == t && l.GuardianMembershipId == m.Id
                                  select new { p.Id, a.DisplayName }).ToListAsync(ct);
            var mineIds = children.Select(c => c.Id).Concat(myPid is { } x ? [x] : []).ToList();
            var team = await sys.FamilyTeams.Include(f => f.Members)
                .Where(f => f.TenantId == t && f.IsActive && f.Members.Any(fm => fm.IsActive && mineIds.Contains(fm.ParticipantId)))
                .OrderBy(f => f.Id).FirstOrDefaultAsync(ct);

            var teamPids = team?.Members.Where(fm => fm.IsActive).OrderBy(fm => fm.SortOrder).Select(fm => fm.ParticipantId).ToList() ?? [];
            var today = clock.Today(m.TimeZoneId);
            var points = await FamilyTeamService.SeasonPointsAsync(sys, teamPids.Concat(mineIds).Distinct().ToList(), today, ct);
            string? teamName = null;
            List<string> teamMembers = [];
            if (team is not null)
            {
                var names = await (from p in sys.Participants
                                   join pm in sys.Memberships on new { p.TenantId, Id = p.MembershipId } equals new { pm.TenantId, pm.Id }
                                   join a in sys.Accounts on pm.AccountId equals a.Id
                                   where p.TenantId == t && teamPids.Contains(p.Id)
                                   select new { p.Id, a.DisplayName }).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
                var members = team.Members.Where(fm => fm.IsActive && names.ContainsKey(fm.ParticipantId)).OrderBy(fm => fm.SortOrder)
                    .Select(fm => (names[fm.ParticipantId], fm.Relation)).ToList();
                teamName = FamilyTeamService.DisplayName(team.Name, members);
                teamMembers = members.Select(x => x.Item1).ToList();
            }
            var myPoints = myPid is { } me ? points.GetValueOrDefault(me) : 0;
            if (m.Roles.Contains(Roles.Guardian))
                options.Add(new FamilyOption(t, m.Name, m.ThemeColor, children.Select(c => c.DisplayName).ToList(),
                    teamName, teamPids.Sum(p => points.GetValueOrDefault(p)), teamMembers, myPid is not null, myPoints));
            if (m.Roles.Contains(Roles.Participant))
                participating.Add(new FamilyStop(t, m.Name, myPoints, team is not null && myPid is { } mp && team.Members.Any(fm => fm.IsActive && fm.ParticipantId == mp) ? teamName : null));
        }
        return new FamilyChoice(current, options, participating);
    }

    /// <summary>
    /// 고르기 · 바꾸기(R-168). 고른 부서는 내가 보호자로 승인된 부서여야 한다.
    /// 고른 부서 밖에서 내가 참여자인 부서: 참여자 역할을 빼고, 참여자를 멈추고(점수 · 기록은 남음), 가족팀 자리를 멈춘다.
    /// 고른 부서에서 참여자 역할은 선생님이 준다(자동으로 주지 않음). 같은 부서를 다시 고르면 아무것도 하지 않는다.
    /// </summary>
    public async Task<bool> ChooseAsync(int accountId, int tenantId, CancellationToken ct = default)
    {
        await using var sys = systemDb.Create(accountId);
        var ok = await sys.Memberships.AnyAsync(m => m.AccountId == accountId && m.TenantId == tenantId && m.Status == MembershipStatus.Approved
            && m.Roles.Any(r => r.Role == Roles.Guardian) && sys.Tenants.Any(t => t.Id == tenantId && t.Status == TenantStatus.Active), ct);
        if (!ok) return false;
        var account = await sys.Accounts.SingleAsync(a => a.Id == accountId, ct);
        if (account.FamilyTenantId == tenantId) return true;

        await using var tx = sys.Database.IsRelational() ? await sys.Database.BeginTransactionAsync(ct) : null;
        var others = await sys.Memberships.Include(m => m.Roles)
            .Where(m => m.AccountId == accountId && m.TenantId != tenantId && m.Roles.Any(r => r.Role == Roles.Participant)).ToListAsync(ct);
        var stopped = new List<int>();
        foreach (var m in others)
        {
            sys.MembershipRoles.RemoveRange(m.Roles.Where(r => r.Role == Roles.Participant));
            var p = await sys.Participants.SingleOrDefaultAsync(x => x.TenantId == m.TenantId && x.MembershipId == m.Id, ct);
            if (p is null) continue;
            p.IsActive = false;
            foreach (var fm in await sys.FamilyMembers.Where(f => f.TenantId == m.TenantId && f.ParticipantId == p.Id && f.IsActive).ToListAsync(ct)) fm.IsActive = false;
            stopped.Add(m.TenantId);
        }
        var was = account.FamilyTenantId;
        account.FamilyTenantId = tenantId;
        sys.AuditLogs.Add(new AuditLog
        {
            OccurredUtc = time.GetUtcNow().UtcDateTime, ActorAccountId = accountId, Action = "FamilyTenant", TenantId = tenantId,
            Detail = $"account={accountId} from={(was?.ToString() ?? "none")} to={tenantId} stopped={string.Join(",", stopped)}",
        });
        await sys.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>홈 "우리 가족팀" 칸의 "가족과 함께하는 부서"(부서 이름, 안 골랐으면 null).</summary>
    public async Task<string?> CurrentNameAsync(int accountId, CancellationToken ct = default)
    {
        await using var sys = systemDb.Create(accountId);
        return await (from a in sys.Accounts join t in sys.Tenants on a.FamilyTenantId equals t.Id where a.Id == accountId select t.Name).SingleOrDefaultAsync(ct);
    }

    /// <summary>부서 홈 안내용: 이 계정이 보호자인데 아직 가족 부서를 고르지 않았는지(0046 결과).</summary>
    public async Task<bool> NeedsChoiceAsync(int accountId, CancellationToken ct = default)
    {
        await using var sys = systemDb.Create(accountId);
        return await sys.Accounts.AnyAsync(a => a.Id == accountId && a.FamilyTenantId == null, ct)
            && await sys.Memberships.AnyAsync(m => m.AccountId == accountId && m.Status == MembershipStatus.Approved && m.Roles.Any(r => r.Role == Roles.Guardian), ct);
    }
}
