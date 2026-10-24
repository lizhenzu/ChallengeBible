using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record AuditLine(DateTime OccurredUtc, string Actor, string Detail);

/// <summary>
/// 관리 화면의 "바꾼 기록"(이름 · 색, 시즌 시작일 …). app.AuditLog 는 RLS 없는 플랫폼 표라
/// 여기서 반드시 그 부서 TenantId 로 거른다.
/// </summary>
public static class AuditLines
{
    public static Task<List<AuditLine>> ForTenantAsync(AppDbContext ctx, int tenantId, string action, CancellationToken ct, int take = 30) =>
        (from l in ctx.AuditLogs
         join a in ctx.Accounts on l.ActorAccountId equals a.Id
         where l.TenantId == tenantId && l.Action == action
         orderby l.OccurredUtc descending, l.Id descending
         select new AuditLine(l.OccurredUtc, a.DisplayName, l.Detail ?? "")).Take(take).ToListAsync(ct);
}
