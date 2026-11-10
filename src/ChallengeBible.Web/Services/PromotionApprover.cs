using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>
/// 진급 승인 처리기(0026, 0030, 물리 설계 3-3). 받는 부서 관리자의 권한을 이 부서 연결로 먼저 확인한 뒤,
/// 시스템 연결의 한 트랜잭션에서 소속을 받는 부서로 옮긴다(DeptMembership: 기록 이동 · 팀 정리, 0066, 0090).
/// 통독 · 읽음 · 책갈피는 사람에 붙어 그대로다(R-158). 실행 사실은 AuditLog 에 남긴다.
/// </summary>
public sealed class PromotionApprover(IAppDb db, ISystemDb systemDb, TenantContext tenant, TimeProvider time)
{
    public async Task<PromotionError> ApproveAsync(string actor, int id, string? roleCode, CancellationToken ct = default)
    {
        // 1) 권한: 이 부서가 받는 쪽인 요청이어야 한다(일반 연결 — 필터 · RLS 그대로)
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
        var user = await sys.Users.SingleAsync(u => u.UserName == req.UserName, ct);
        if (await sys.DeptRoles.AnyAsync(d => d.TenantId == req.TargetTenantId && d.UserId == user.Id, ct)) return PromotionError.AlreadyMember;

        var now = time.GetUtcNow().UtcDateTime;
        var code = string.IsNullOrWhiteSpace(roleCode) ? null : roleCode.Trim();
        var (error, moved) = await DeptMembership.PlaceAsync(sys, user, req.TargetTenantId, code, now, ct);
        if (error == MoveError.TeamLocked) return PromotionError.TeamLocked;

        req.Status = PromotionStatus.Approved;
        req.DecidedUtc = now;
        req.DecidedByUserName = actor;
        sys.AuditLogs.Add(new AuditLog
        {
            OccurredUtc = now, ActorUserName = actor, Action = "PromotionApproved", TenantId = req.TargetTenantId,
            Detail = $"request={req.Id} account={req.UserName} source={req.SourceTenantId} target={req.TargetTenantId} moved={moved.Activities} points={moved.Points} quiz={moved.QuizAttempts} skipped={moved.Skipped}",
        });
        await sys.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return PromotionError.None;
    }
}
