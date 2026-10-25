using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>
/// 부모의 시상 부서(0046 "가족과 함께할 부서" → 0065 · R-179: 플랫폼 관리자가 정한다, 부모는 보기만).
/// 소속이 여러 부서에 걸쳐 있어 시스템 연결로 읽고(물리 설계 3-3 "시상 부서 정하기"), 바꾸는 것도 이 처리기 한 곳에서 한 번에 한다:
/// 정한 부서 밖에서는 그 계정의 참여자 역할을 빼고 가족팀 자리를 멈춘다(점수 · 기록은 남음). 실행 사실은 AuditLog 에 남긴다.
/// 플랫폼 관리자 확인은 부르는 쪽(FamilyAdminService)이 먼저 한다.
/// </summary>
public sealed class FamilyTenantService(ISystemDb systemDb, TimeProvider time)
{
    public const string SetAction = "FamilyTenantSet";

    /// <summary>
    /// 정하기 · 바꾸기(R-179). 정할 부서는 그 계정이 승인된 운영 중 부서(임시 부서 제외)여야 한다.
    /// 그 밖에서 참여자인 부서: 참여자 역할을 빼고, 참여자를 멈추고(점수 · 기록은 남음), 가족팀 자리를 멈춘다.
    /// 정한 부서의 참여자 역할은 부서 관리자가 준다(자동으로 주지 않음). 같은 부서를 다시 정하면 아무것도 하지 않는다.
    /// </summary>
    public async Task<bool> ChooseAsync(int actor, int accountId, int tenantId, CancellationToken ct = default)
    {
        await using var sys = systemDb.Create(actor);
        var ok = await sys.Memberships.AnyAsync(m => m.AccountId == accountId && m.TenantId == tenantId && m.Status == MembershipStatus.Approved
            && sys.Tenants.Any(t => t.Id == tenantId && t.Status == TenantStatus.Active && !t.IsLobby), ct);
        if (!ok) return false;
        var account = await sys.Accounts.SingleAsync(a => a.Id == accountId, ct);
        if (account.FamilyTenantId == tenantId) return true;

        await using var tx = sys.Database.IsRelational() ? await sys.Database.BeginTransactionAsync(ct) : null;
        var others = await sys.Memberships.Include(m => m.Roles)
            .Where(m => m.AccountId == accountId && m.TenantId != tenantId && m.Roles.Any(r => r.Role == Roles.Participant)
                && !sys.Tenants.Any(t => t.Id == m.TenantId && t.IsLobby)).ToListAsync(ct);
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
            OccurredUtc = time.GetUtcNow().UtcDateTime, ActorAccountId = actor, Action = SetAction, TenantId = tenantId,
            Detail = $"account={accountId} from={(was?.ToString() ?? "none")} to={tenantId} stopped={string.Join(",", stopped)}",
        });
        await sys.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>홈 "우리 가족팀" 칸 · 부서 고르기의 "시상 부서"(부서 이름, 정하지 않았으면 null).</summary>
    public async Task<string?> CurrentNameAsync(int accountId, CancellationToken ct = default)
    {
        await using var sys = systemDb.Create(accountId);
        return await (from a in sys.Accounts join t in sys.Tenants on a.FamilyTenantId equals t.Id where a.Id == accountId select t.Name).SingleOrDefaultAsync(ct);
    }

    /// <summary>부서 홈 안내용: 이 계정이 보호자인데 아직 시상 부서가 정해지지 않았는지.</summary>
    public async Task<bool> NeedsChoiceAsync(int accountId, CancellationToken ct = default)
    {
        await using var sys = systemDb.Create(accountId);
        return await sys.Accounts.AnyAsync(a => a.Id == accountId && a.FamilyTenantId == null, ct)
            && await sys.Memberships.AnyAsync(m => m.AccountId == accountId && m.Status == MembershipStatus.Approved && m.Roles.Any(r => r.Role == Roles.Guardian), ct);
    }
}
