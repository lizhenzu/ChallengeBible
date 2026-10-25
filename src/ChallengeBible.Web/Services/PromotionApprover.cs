using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>
/// 진급 승인 처리기(0026, 0030 결정 3 · 4, 물리 설계 3-3). 진급에서 시스템 연결을 쓰는 곳은 여기 한 곳뿐이다.
/// 받는 부서 관리자의 권한을 이 부서 연결로 먼저 확인한 뒤, 시스템 연결의 한 트랜잭션에서
/// 받는 부서 소속을 만들고(0058), 참여자로 받으면 보낸 부서의 미션 기록 · 점수를 날짜 그대로 옮긴 뒤(0066, RecordMover)
/// 보낸 부서의 그 소속과 딸린 기록을 지운다(0022, R-139). 실행 사실은 AuditLog 에 남긴다.
/// 통독 · 읽음 · 책갈피는 계정 범위라 그대로 둔다(R-158).
/// </summary>
public sealed class PromotionApprover(IAppDb db, ISystemDb systemDb, TenantContext tenant, TimeProvider time)
{
    public static readonly string[] AllowedRoles = [Roles.Participant, Roles.Guardian];

    public async Task<PromotionError> ApproveAsync(int actor, int id, IReadOnlyCollection<string> roles, string? group, CancellationToken ct = default)
    {
        var want = roles.Distinct().ToList();
        if (want.Count == 0) return PromotionError.NoRole;
        if (want.Any(r => !AllowedRoles.Contains(r))) return PromotionError.BadRole;

        // 1) 권한: 이 부서가 받는 쪽인 기다리는 요청이어야 한다(일반 연결 — 필터 · RLS 그대로)
        var me = tenant.Current.Id;
        await using (var ctx = db.Create())
        {
            var mine = await ctx.PromotionRequests.AnyAsync(r => r.Id == id && r.TargetTenantId == me, ct);
            if (!mine) return PromotionError.NotFound;
        }

        // 2) 시스템 연결, 한 트랜잭션
        await using var sys = systemDb.Create(actor);
        await using var tx = sys.Database.IsRelational() ? await sys.Database.BeginTransactionAsync(ct) : null;

        var req = await sys.PromotionRequests.SingleAsync(r => r.Id == id, ct);
        if (req.Status != PromotionStatus.Pending) return PromotionError.BadState;
        var now = time.GetUtcNow().UtcDateTime;

        // 받는 부서 소속: 이미 승인 · 정지 소속이면 멈춘다. 신청 · 거절 등으로 남은 행은 이어서 쓴다(계정 · 영역당 1건, 0027)
        var target = await sys.Memberships.Include(m => m.Roles)
            .SingleOrDefaultAsync(m => m.TenantId == req.TargetTenantId && m.AccountId == req.AccountId, ct);
        if (target is { Status: MembershipStatus.Approved or MembershipStatus.Suspended }) return PromotionError.AlreadyMember;

        // 받는 부서 소속 만들기 · 승인(먼저 — 옮길 참여자가 있어야 한다, 0066)
        if (target is null)
        {
            target = new Membership { TenantId = req.TargetTenantId, AccountId = req.AccountId, RequestedUtc = now };
            sys.Memberships.Add(target);
        }
        else sys.MembershipRoles.RemoveRange(target.Roles);
        target.Status = MembershipStatus.Approved;
        target.GroupLabel = string.IsNullOrWhiteSpace(group) ? null : group.Trim()[..Math.Min(group.Trim().Length, 50)];
        target.DecidedUtc = now;
        target.DecidedByAccountId = actor;
        await sys.SaveChangesAsync(ct);
        foreach (var r in want) sys.MembershipRoles.Add(new MembershipRole { TenantId = target.TenantId, MembershipId = target.Id, Role = r });
        Participant? newP = null;
        if (want.Contains(Roles.Participant))
        {
            newP = await sys.Participants.SingleOrDefaultAsync(p => p.TenantId == target.TenantId && p.MembershipId == target.Id, ct);
            if (newP is null) sys.Participants.Add(newP = new Participant { TenantId = target.TenantId, MembershipId = target.Id });
            else newP.IsActive = true;
        }
        await sys.SaveChangesAsync(ct);

        // 보낸 부서의 기록을 새 부서로 옮긴 뒤(0066, R-139) 보낸 부서의 소속과 딸린 기록을 지운다(0022)
        var source = await sys.Memberships.Include(m => m.Roles)
            .SingleOrDefaultAsync(m => m.TenantId == req.SourceTenantId && m.AccountId == req.AccountId, ct);
        var removed = 0;
        var moved = new LobbyMove(0, 0, 0, 0, 0);
        if (source is not null)
        {
            var t = source.TenantId;
            var pids = await sys.Participants.Where(p => p.TenantId == t && p.MembershipId == source.Id).Select(p => p.Id).ToListAsync(ct);
            if (newP is not null && pids.Count == 1) moved = await RecordMover.CopyAsync(sys, t, pids[0], target.TenantId, newP.Id, ct);
            sys.PointLedgers.RemoveRange(await sys.PointLedgers.Where(x => x.TenantId == t && pids.Contains(x.ParticipantId)).ToListAsync(ct));
            sys.QuizAttempts.RemoveRange(await sys.QuizAttempts.Where(x => x.TenantId == t && pids.Contains(x.ParticipantId)).ToListAsync(ct));
            sys.MilestonesReached.RemoveRange(await sys.MilestonesReached.Where(x => x.TenantId == t && pids.Contains(x.ParticipantId)).ToListAsync(ct));
            sys.GuardianLinks.RemoveRange(await sys.GuardianLinks.Where(x => x.TenantId == t && (pids.Contains(x.ParticipantId) || x.GuardianMembershipId == source.Id)).ToListAsync(ct));
            await sys.SaveChangesAsync(ct);
            var acts = await sys.Activities.Where(x => x.TenantId == t && pids.Contains(x.ParticipantId)).ToListAsync(ct);
            removed = acts.Count;
            sys.Activities.RemoveRange(acts);
            await sys.SaveChangesAsync(ct);
            sys.Participants.RemoveRange(await sys.Participants.Where(p => p.TenantId == t && pids.Contains(p.Id)).ToListAsync(ct));
            await sys.SaveChangesAsync(ct);
            sys.MembershipRoles.RemoveRange(source.Roles);
            sys.Memberships.Remove(source);
            await sys.SaveChangesAsync(ct);
        }

        req.Status = PromotionStatus.Approved;
        req.DecidedUtc = now;
        req.DecidedByAccountId = actor;
        var account = await sys.Accounts.SingleAsync(a => a.Id == req.AccountId, ct);
        if (account.LastTenantId == req.SourceTenantId) account.LastTenantId = req.TargetTenantId;
        sys.AuditLogs.Add(new AuditLog
        {
            OccurredUtc = now, ActorAccountId = actor, Action = "PromotionApproved", TenantId = req.TargetTenantId,
            Detail = $"request={req.Id} account={req.AccountId} source={req.SourceTenantId} target={req.TargetTenantId} activities={removed} moved={moved.Activities} points={moved.Points} quiz={moved.QuizAttempts} milestones={moved.Milestones} skipped={moved.Skipped}",
        });
        await sys.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return PromotionError.None;
    }
}
