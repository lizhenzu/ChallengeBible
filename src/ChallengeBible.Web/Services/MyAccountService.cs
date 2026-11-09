using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>내 계정 화면의 부서 한 줄: 승인된 소속 · 내 역할 · 가족과 함께하는 부서인지.</summary>
public sealed record MyDept(int TenantId, string Name, string Slug, string ThemeColor, IReadOnlyList<string> Roles);

/// <summary>
/// 내 계정(확정 시안: 내계정). 역할 표는 영역마다 막혀 있어(RLS) 여러 부서의 내 역할을 보려면 시스템 연결로 읽는다 —
/// 읽기만, 이 로그인 계정의 소속만(물리 설계 3-3 "내 계정 부서 목록").
/// </summary>
public sealed class MyAccountService(ISystemDb systemDb)
{
    public async Task<IReadOnlyList<MyDept>> DeptsAsync(string userName, CancellationToken ct = default)
    {
        await using var sys = systemDb.Create(userName);
        var user = await sys.Users.AsNoTracking().SingleAsync(u => u.UserName == userName, ct);
        var admin = await AccountService.IsPlatformAdminAsync(sys, user.Id, ct);
        var rows = await (from d in sys.DeptRoles
                          join t in sys.Tenants on d.TenantId equals t.Id
                          where d.UserId == user.Id && t.Status == TenantStatus.Active
                          orderby t.Name
                          select new { t.Id, t.Name, t.Slug, t.ThemeColor, d.RoleCode }).ToListAsync(ct);
        var deptAdmin = await (from ur in sys.UserRoles join r in sys.IdentityRoleRows on ur.RoleId equals r.Id where ur.UserId == user.Id && r.Name == IdentityRoles.Admin select ur).AnyAsync(ct);
        return rows.Select(r => new MyDept(r.Id, r.Name, r.Slug, r.ThemeColor, UserRole.ScreenRoles(r.RoleCode, deptAdmin).ToList())).ToList();
    }
}
