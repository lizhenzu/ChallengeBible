using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>첫 화면의 부서 관리자 로그인 버튼 하나: 부서 주소 · 짧은 이름(유년부) · 색 번호(나이순 0 ~ 7).</summary>
public sealed record DeptAdminButton(string Slug, string Short, int Color)
{
    public string Label => $"{Short} 관리자";
}

/// <summary>
/// 부서 관리자 로그인(R-364, 확정 시안 첫화면-부서관리자 B, 2026-11-22 사용자): 첫 화면 [관리자 로그인] 아래에 부서마다 버튼,
/// 누르면 그 부서 관리자 계정(아이디 = 부서주소-admin)으로 비밀번호만 넣는다. 항상 보인다.
/// 버튼은 영아부 ~ 청년부(사용자가 고른 8개, 나이순)에서 그 계정이 있고 Identity Admin 역할이며 그 부서 소속일 때만.
/// </summary>
public sealed class DeptAdminLoginService(ISystemDb systemDb)
{
    public const string Suffix = "-admin";
    /// <summary>버튼을 두는 마지막 부서(나이순). 장년부는 넣지 않는다(사용자 2026-11-22: 영아 ~ 고등부 + 청년부).</summary>
    public static readonly int LastAge = DeptOrder.Key("청년부");

    public static string UserNameOf(string slug) => slug.ToLowerInvariant() + Suffix;
    public static string ShortName(string name) => name.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? name;

    public async Task<IReadOnlyList<DeptAdminButton>> ListAsync(CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create("landing");   // 로그인 전
        var tenants = await ctx.Tenants.AsNoTracking().Where(t => t.Status == TenantStatus.Active && !t.IsLobby)
            .Select(t => new { t.Id, t.Slug, t.Name }).ToListAsync(ct);
        var adminRole = await ctx.IdentityRoleRows.AsNoTracking().Where(r => r.Name == IdentityRoles.Admin).Select(r => r.Id).FirstOrDefaultAsync(ct);
        var names = tenants.Select(t => UserNameOf(t.Slug)).ToList();
        var ok = await (from u in ctx.Users.AsNoTracking()
                        join ur in ctx.UserRoles on u.Id equals ur.UserId
                        join d in ctx.DeptRoles on u.Id equals d.UserId
                        where ur.RoleId == adminRole && names.Contains(u.UserName!)
                        select new { u.UserName, d.TenantId }).ToListAsync(ct);
        return tenants
            .Where(t => DeptOrder.Key(t.Name) <= LastAge && ok.Any(o => o.TenantId == t.Id && string.Equals(o.UserName, UserNameOf(t.Slug), StringComparison.OrdinalIgnoreCase)))
            .OrderBy(t => DeptOrder.Key(t.Name)).ThenBy(t => t.Id)
            .Select(t => new DeptAdminButton(t.Slug, ShortName(t.Name), DeptOrder.Key(t.Name)))
            .ToList();
    }

    public async Task<DeptAdminButton?> FindAsync(string? slug, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(slug) ? null
            : (await ListAsync(ct)).FirstOrDefault(b => string.Equals(b.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
