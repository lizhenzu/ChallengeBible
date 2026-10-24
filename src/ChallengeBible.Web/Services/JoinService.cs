using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record JoinTarget(int TenantId, string Name, bool MissionEnabled, bool ReadingEnabled)
{
    public string ThemeColor { get; init; } = "blue";
}

public enum JoinError { None, NotFound, AlreadyMember, NoRole, BadRole }

/// <summary>
/// 목록에서 부서를 고르거나(0059) 참여 코드로 부서를 찾아 참여 신청을 한다(0045, 0048). 신청은 "신청" 상태 소속을 만들고,
/// 부서 관리자가 승인하면 그때 쓸 수 있다(R-130). 신청할 수 있는 역할은 참여자 · 보호자뿐이다.
/// </summary>
public sealed class JoinService(IAppDb db, RequestScope scope, TimeProvider time)
{
    public static readonly string[] AllowedRoles = [Roles.Participant, Roles.Guardian];

    public static string NormalizeCode(string code) =>
        new string((code ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>정지된 부서는 "없음"과 똑같이 답한다(0048 결정 5).</summary>
    public async Task<JoinTarget?> LookupAsync(string code, CancellationToken ct = default)
    {
        var c = NormalizeCode(code);
        if (c.Length != 6) return null;
        await using var ctx = db.Create();
        return await ctx.Tenants
            .Where(t => t.JoinCode == c && t.Status == TenantStatus.Active)
            .Select(t => new JoinTarget(t.Id, t.Name, t.MissionEnabled, t.ReadingEnabled) { ThemeColor = t.ThemeColor })
            .SingleOrDefaultAsync(ct);
    }

    /// <summary>참여 신청 화면의 부서 목록(0059): 플랫폼이 "목록에 보이기"를 켠, 운영 중인 부서만.</summary>
    public async Task<List<JoinTarget>> ListedAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return await ctx.Tenants
            .Where(t => t.JoinListed && t.Status == TenantStatus.Active)
            .OrderBy(t => t.Name)
            .Select(t => new JoinTarget(t.Id, t.Name, t.MissionEnabled, t.ReadingEnabled) { ThemeColor = t.ThemeColor })
            .ToListAsync(ct);
    }

    /// <summary>
    /// 목록에서 고른 부서로 신청(0059). 목록에 없는 부서는 그 부서의 코드를 함께 받아야 한다
    /// (부서 주소 · 코드로 찾은 경우) — Id 만 바꿔 숨긴 부서에 신청하지 못하게.
    /// </summary>
    public async Task<(JoinError Error, int? MembershipId)> ApplyAsync(int accountId, int tenantId, string? code, IReadOnlyCollection<string> roles, CancellationToken ct = default)
    {
        var c = NormalizeCode(code ?? "");
        JoinTarget? target;
        await using (var ctx = db.Create())
            target = await ctx.Tenants
                .Where(t => t.Id == tenantId && t.Status == TenantStatus.Active && (t.JoinListed || t.JoinCode == c))
                .Select(t => new JoinTarget(t.Id, t.Name, t.MissionEnabled, t.ReadingEnabled))
                .SingleOrDefaultAsync(ct);
        return await ApplyToAsync(accountId, target, roles, ct);
    }

    public async Task<(JoinError Error, int? MembershipId)> ApplyAsync(int accountId, string code, IReadOnlyCollection<string> roles, CancellationToken ct = default) =>
        await ApplyToAsync(accountId, await LookupAsync(code, ct), roles, ct);

    private async Task<(JoinError Error, int? MembershipId)> ApplyToAsync(int accountId, JoinTarget? target, IReadOnlyCollection<string> roles, CancellationToken ct)
    {
        if (roles.Count == 0) return (JoinError.NoRole, null);
        if (roles.Any(r => !AllowedRoles.Contains(r))) return (JoinError.BadRole, null);
        if (target is null) return (JoinError.NotFound, null);

        // 신청은 그 부서의 행을 만드는 일이라, 이 요청의 영역을 그 부서로 정한 뒤 새 연결로 쓴다(RLS BLOCK 조건).
        scope.SetTenant(target.TenantId);
        await using var ctx = db.Create();
        if (await ctx.Memberships.AnyAsync(m => m.AccountId == accountId, ct))
            return (JoinError.AlreadyMember, null);

        var m = new Membership
        {
            TenantId = target.TenantId,
            AccountId = accountId,
            Status = MembershipStatus.Requested,
            RequestedUtc = time.GetUtcNow().UtcDateTime,
            Roles = roles.Distinct().Select(r => new MembershipRole { TenantId = target.TenantId, Role = r }).ToList(),
        };
        ctx.Memberships.Add(m);
        await ctx.SaveChangesAsync(ct);
        return (JoinError.None, m.Id);
    }

    /// <summary>본인 신청 취소 — 아직 "신청" 상태일 때만.</summary>
    public async Task<bool> CancelAsync(int accountId, int tenantId, CancellationToken ct = default)
    {
        scope.SetTenant(tenantId);
        await using var ctx = db.Create();
        var m = await ctx.Memberships.Include(x => x.Roles)
            .SingleOrDefaultAsync(x => x.AccountId == accountId && x.Status == MembershipStatus.Requested, ct);
        if (m is null) return false;
        ctx.MembershipRoles.RemoveRange(m.Roles);
        ctx.Memberships.Remove(m);
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    public static string Message(JoinError e) => e switch
    {
        JoinError.NotFound => "이 코드로 찾은 부서가 없어요. 선생님께 다시 물어봐 주세요.",
        JoinError.AlreadyMember => "이미 이 부서에 신청했거나 들어가 있어요.",
        JoinError.NoRole => "하나는 골라 주세요.",
        JoinError.BadRole => "교사 · 관리자 역할은 신청할 수 없어요. 선생님이 정해 줘요.",
        _ => "",
    };
}
