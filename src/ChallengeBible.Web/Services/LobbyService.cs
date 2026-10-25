using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>진짜 부서로 옮긴 결과: 옮긴 미션 기록 · 점수 줄 · 퀴즈 시도 · 축하 단계, 같은 날 같은 미션이 있어 남겨 둔 기록.</summary>
public sealed record LobbyMove(int Activities, int Points, int QuizAttempts, int Milestones, int Skipped);

/// <summary>
/// 임시 부서 "처음 오신 분"(ADR 0064, R-171 ~ R-174). 시스템 연결 처리기(물리 설계 3-3): 계정이 아직 영역에 들어가기 전(가입 직후),
/// 또는 다른 영역(진짜 부서)의 승인 때 임시 부서 행을 다룬다. 옮기기는 감사 로그를 남긴다.
/// </summary>
public sealed class LobbyService(ISystemDb systemDb, TimeProvider time)
{
    public const string MovedAction = "LobbyMoved";

    /// <summary>R-171: 가입하면 임시 부서의 승인된 참여자가 된다(승인 절차 없음). 임시 부서가 없거나 이미 있으면 아무것도 하지 않는다.</summary>
    public async Task<bool> EnrollAsync(int accountId, CancellationToken ct = default)
    {
        await using var sys = systemDb.Create(accountId);
        var lobby = await sys.Tenants.Where(t => t.IsLobby && t.Status == TenantStatus.Active).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct);
        if (lobby is not int id) return false;
        if (await sys.Memberships.AnyAsync(m => m.TenantId == id && m.AccountId == accountId, ct)) return true;
        var now = time.GetUtcNow().UtcDateTime;
        var m = new Membership { TenantId = id, AccountId = accountId, Status = MembershipStatus.Approved, RequestedUtc = now, DecidedUtc = now };
        sys.Memberships.Add(m);
        await sys.SaveChangesAsync(ct);
        sys.MembershipRoles.Add(new MembershipRole { TenantId = id, MembershipId = m.Id, Role = Roles.Participant });
        sys.Participants.Add(new Participant { TenantId = id, MembershipId = m.Id });
        await sys.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// R-173 · R-174: 진짜 부서에 처음 승인되면(임시 부서 소속이 아직 승인 상태일 때 한 번) 임시 부서의 미션 기록 · 점수를 날짜별로 그 부서 참여자에게 옮기고
    /// (같은 날 같은 미션 · 같은 주 암송이 이미 있으면 그 부서 것을 남김), 임시 부서 소속은 멈춘다(기록은 남김). 그 부서에서 참여자가 아니면 멈추기만 한다.
    /// 승인한 부서 관리자의 처리 뒤에 부른다. 옮길 일이 없으면 null.
    /// </summary>
    public async Task<LobbyMove?> LeaveAsync(int actor, int accountId, int targetTenantId, CancellationToken ct = default)
    {
        await using var sys = systemDb.Create(actor);
        await using var tx = sys.Database.IsRelational() ? await sys.Database.BeginTransactionAsync(ct) : null;

        var lobby = await sys.Tenants.Where(t => t.IsLobby).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct);
        if (lobby is not int from || from == targetTenantId) return null;
        var lm = await sys.Memberships.SingleOrDefaultAsync(m => m.TenantId == from && m.AccountId == accountId && m.Status == MembershipStatus.Approved, ct);
        if (lm is null) return null;   // 이미 옮겼거나 임시 부서에 없음 — 처음 승인된 부서로 한 번만
        var now = time.GetUtcNow().UtcDateTime;

        var lp = await sys.Participants.SingleOrDefaultAsync(p => p.TenantId == from && p.MembershipId == lm.Id, ct);
        var tp = await sys.Participants
            .Where(p => p.TenantId == targetTenantId && p.IsActive && sys.Memberships.Any(m => m.TenantId == targetTenantId && m.Id == p.MembershipId
                && m.AccountId == accountId && m.Status == MembershipStatus.Approved))
            .SingleOrDefaultAsync(ct);

        var moved = new LobbyMove(0, 0, 0, 0, 0);
        if (lp is not null && tp is not null) moved = await RecordMover.CopyAsync(sys, from, lp.Id, targetTenantId, tp.Id, ct);

        lm.Status = MembershipStatus.Suspended;
        lm.DecidedUtc = now;
        lm.DecidedByAccountId = actor;
        if (lp is not null) lp.IsActive = false;
        var account = await sys.Accounts.SingleAsync(a => a.Id == accountId, ct);
        if (account.LastTenantId == from) account.LastTenantId = targetTenantId;
        sys.AuditLogs.Add(new AuditLog
        {
            OccurredUtc = now, ActorAccountId = actor, Action = MovedAction, TenantId = targetTenantId,
            Detail = $"account={accountId} from={from} activities={moved.Activities} points={moved.Points} quiz={moved.QuizAttempts} milestones={moved.Milestones} skipped={moved.Skipped}",
        });
        await sys.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return moved;
    }
}
