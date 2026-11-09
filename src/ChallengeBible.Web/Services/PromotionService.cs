using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>진급 요청 한 줄. 받은 쪽은 Other = 보낸 부서, 보낸 쪽은 Other = 받는 부서.</summary>
public sealed record PromotionRow(int Id, string Name, string Email, string OtherName, string OtherColor, string Status,
    DateTime RequestedUtc, DateTime? DecidedUtc, string? DecidedBy);

public sealed record PromotionTarget(int TenantId, string Name, string ThemeColor);

public enum PromotionError
{
    None, NotFound, TargetNotFound, SameTenant, NotParticipant, Pending, BadState, NoRole, BadRole, AlreadyMember, TooMany, TeamLocked,
}

/// <summary>
/// 진급(0026, 0030, 0058) 중 일반 요청 경로에서 하는 일: 보내기 · 목록 · 취소 · 거절.
/// 모두 이 부서의 연결로만 한다(전역 필터 + RLS sec.fn_Promotion). 승인은 <see cref="PromotionApprover"/> 만 한다.
/// </summary>
public sealed class PromotionService(IAppDb db, TenantContext tenant, TimeProvider time)
{
    /// <summary>받을 부서 확인(이름 · 색만, 0026). 정지된 부서는 "없음"과 같다(0048).</summary>
    public async Task<PromotionTarget?> LookupAsync(string code, CancellationToken ct = default)
    {
        var c = JoinService.NormalizeCode(code);
        if (c.Length != 6) return null;
        await using var ctx = db.Create();
        return await ctx.Tenants.Where(t => t.JoinCode == c && t.Status == TenantStatus.Active)
            .Select(t => new PromotionTarget(t.Id, t.Name, t.ThemeColor)).SingleOrDefaultAsync(ct);
    }

    /// <summary>
    /// 고른 참여자들을 받을 부서로 보낸다. 승인된 참여자만, 이미 기다리는 요청이 있으면 건너뛴다(0030 고유).
    /// 받는 부서의 소속은 여기서 만들지 않는다 — 승인 때 만든다(0058).
    /// </summary>
    public async Task<(PromotionError Error, int Sent, List<string> Skipped)> SendAsync(string actor, IReadOnlyCollection<int> deptRoleIds, string code, CancellationToken ct = default)
    {
        if (deptRoleIds.Count == 0) return (PromotionError.NotFound, 0, []);
        var target = await LookupAsync(code, ct);
        if (target is null) return (PromotionError.TargetNotFound, 0, []);
        if (target.TenantId == tenant.Current.Id) return (PromotionError.SameTenant, 0, []);

        await using var ctx = db.Create();
        var picked = (await (from d in ctx.DeptRoles
                            where deptRoleIds.Contains(d.Id)
                            join a in ctx.Users on d.UserId equals a.Id
                            select new { UserName = a.UserName!, a.LastName, a.FirstName }).ToListAsync(ct))
            .Select(x => new { x.UserName, DisplayName = AppUser.FullName(x.LastName, x.FirstName) }).ToList();
        if (picked.Count != deptRoleIds.Distinct().Count()) return (PromotionError.NotFound, 0, []);

        var ids = picked.Select(p => p.UserName).ToList();
        var waiting = (await ctx.PromotionRequests
            .Where(r => r.SourceTenantId == tenant.Current.Id && r.Status == PromotionStatus.Pending && ids.Contains(r.UserName))
            .Select(r => r.UserName).ToListAsync(ct)).ToHashSet();
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var p in picked.Where(p => !waiting.Contains(p.UserName)))
            ctx.PromotionRequests.Add(new PromotionRequest
            {
                UserName = p.UserName, SourceTenantId = tenant.Current.Id, TargetTenantId = target.TenantId,
                RequestedUtc = now, RequestedByUserName = actor,
            });
        await ctx.SaveChangesAsync(ct);
        var skipped = picked.Where(p => waiting.Contains(p.UserName)).Select(p => p.DisplayName).ToList();
        var sent = picked.Count - skipped.Count;
        return (sent == 0 ? PromotionError.Pending : PromotionError.None, sent, skipped);
    }

    public Task<List<PromotionRow>> ReceivedAsync(CancellationToken ct = default) => ListAsync(received: true, ct);
    public Task<List<PromotionRow>> SentAsync(CancellationToken ct = default) => ListAsync(received: false, ct);

    private async Task<List<PromotionRow>> ListAsync(bool received, CancellationToken ct)
    {
        var me = tenant.Current.Id;
        await using var ctx = db.Create();
        var rows = await (from r in ctx.PromotionRequests
                          where received ? r.TargetTenantId == me : r.SourceTenantId == me
                          join a in ctx.Users on r.UserName equals a.UserName
                          join t in ctx.Tenants on (received ? r.SourceTenantId : r.TargetTenantId) equals t.Id
                          join d in ctx.Users on r.DecidedByUserName equals d.UserName into dd
                          from d in dd.DefaultIfEmpty()
                          orderby r.RequestedUtc descending, r.Id descending
                          select new PromotionRow(r.Id, AppUser.FullName(a.LastName, a.FirstName), a.Email ?? "", t.Name, t.ThemeColor, r.Status, r.RequestedUtc, r.DecidedUtc,
                              d == null ? null : AppUser.FullName(d.LastName, d.FirstName)))
            .Take(100).ToListAsync(ct);
        // 보낸 쪽에는 상대 부서의 이름 · 상태만(0030): 승인 · 거절한 그 부서 선생님 이름은 빼고, 내가 취소한 것만 이름을 둔다
        return received ? rows : rows.Select(r => r.Status == PromotionStatus.Cancelled ? r : r with { DecidedBy = null }).ToList();
    }

    public async Task<int> PendingReceivedCountAsync(CancellationToken ct = default)
    {
        var me = tenant.Current.Id;
        await using var ctx = db.Create();
        return await ctx.PromotionRequests.CountAsync(r => r.TargetTenantId == me && r.Status == PromotionStatus.Pending, ct);
    }

    /// <summary>보낸 부서가 기다리는 요청을 거둔다. 아무것도 지우지 않는다.</summary>
    public Task<PromotionError> CancelAsync(string actor, int id, CancellationToken ct = default) =>
        CloseAsync(actor, id, received: false, PromotionStatus.Cancelled, ct);

    /// <summary>받는 부서가 거절한다. 아이는 보낸 부서에 그대로 남는다(0026 결정 5).</summary>
    public Task<PromotionError> RejectAsync(string actor, int id, CancellationToken ct = default) =>
        CloseAsync(actor, id, received: true, PromotionStatus.Rejected, ct);

    private async Task<PromotionError> CloseAsync(string actor, int id, bool received, string to, CancellationToken ct)
    {
        var me = tenant.Current.Id;
        await using var ctx = db.Create();
        var r = await ctx.PromotionRequests.SingleOrDefaultAsync(x => x.Id == id && (received ? x.TargetTenantId == me : x.SourceTenantId == me), ct);
        if (r is null) return PromotionError.NotFound;
        if (r.Status != PromotionStatus.Pending) return PromotionError.BadState;
        r.Status = to;
        r.DecidedUtc = time.GetUtcNow().UtcDateTime;
        r.DecidedByUserName = actor;
        await ctx.SaveChangesAsync(ct);
        return PromotionError.None;
    }

    public static string Message(PromotionError e) => e switch
    {
        PromotionError.NotFound => "이 부서에서 찾을 수 없어요.",
        PromotionError.TargetNotFound => "이 코드로 찾은 부서가 없어요.",
        PromotionError.SameTenant => "지금 이 부서예요. 받을 부서의 참여 코드를 넣어 주세요.",
        PromotionError.NotParticipant => "승인된 참여자만 진급을 보낼 수 있어요.",
        PromotionError.Pending => "이미 진행 중인 진급 요청이 있어요.",
        PromotionError.BadState => "이미 처리됐어요. 화면을 새로 고쳐 주세요.",
        PromotionError.NoRole => "역할을 하나는 골라 주세요.",
        PromotionError.BadRole => "역할을 다시 골라 주세요.",
        PromotionError.TeamLocked => "시상 기록이 있는 팀에 있어 옮길 수 없어요.",
        PromotionError.AlreadyMember => "이미 이 부서 소속이에요. 이 요청은 거절해 주세요.",
        PromotionError.TooMany => "잠시 뒤에 다시 해 주세요.",
        _ => "",
    };
}
