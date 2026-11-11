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

    /// <summary>내 명부 줄(0093, R-226): 본인은 생일 · 연락처를 직접 넣고 고친다. 일반 부서에 들어가지 않았으면 null.</summary>
    public async Task<Person?> ProfileAsync(string userName, CancellationToken ct = default)
    {
        await using var sys = systemDb.Create(userName);
        var (user, tenantId) = await HomeDeptAsync(sys, userName, ct);
        if (tenantId is null) return null;
        return await sys.People.AsNoTracking().SingleOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == user.Id, ct)
            ?? new Person { TenantId = tenantId.Value, Name = AppUser.FullName(user.LastName, user.FirstName), UserId = user.Id };
    }

    public async Task<PeopleError> SaveProfileAsync(string userName, int? month, int? day, string? phone1, string? phone2, DateTime now, CancellationToken ct = default)
    {
        var (e, _, bm, bd, p1, p2) = PeopleService.Check(new PersonInput(null, null, month, day, phone1, phone2), needName: false);
        if (e != PeopleError.None) return e;
        await using var sys = systemDb.Create(userName);
        var (user, tenantId) = await HomeDeptAsync(sys, userName, ct);
        if (tenantId is null) return PeopleError.NotFound;
        var p = await sys.People.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == user.Id, ct);
        if (p is null)
        {
            p = new Person { TenantId = tenantId.Value, Name = AppUser.FullName(user.LastName, user.FirstName), UserId = user.Id, CreatedUtc = now, CreatedByUserName = userName };
            sys.People.Add(p);
        }
        (p.BirthMonth, p.BirthDay, p.Phone1, p.Phone2) = (bm, bd, p1, p2);
        await sys.SaveChangesAsync(ct);
        return PeopleError.None;
    }

    // 한 사람 한 부서(0090): 임시 부서가 아닌 내 소속 부서
    private static async Task<(AppUser User, int? TenantId)> HomeDeptAsync(AppDbContext sys, string userName, CancellationToken ct)
    {
        var user = await sys.Users.AsNoTracking().SingleAsync(u => u.UserName == userName, ct);
        var tenantId = await (from d in sys.DeptRoles join t in sys.Tenants on d.TenantId equals t.Id
                              where d.UserId == user.Id && !t.IsLobby select (int?)t.Id).FirstOrDefaultAsync(ct);
        return (user, tenantId);
    }
}
