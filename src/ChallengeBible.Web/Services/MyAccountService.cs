using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>내 계정 화면의 부서 한 줄: 승인된 소속 · 내 역할 · 가족과 함께하는 부서인지.</summary>
public sealed record MyDept(int TenantId, string Name, string Slug, string ThemeColor, IReadOnlyList<string> Roles, bool IsFamily);

/// <summary>
/// 내 계정(확정 시안: 내계정). 역할 표는 영역마다 막혀 있어(RLS) 여러 부서의 내 역할을 보려면 시스템 연결로 읽는다 —
/// 읽기만, 이 로그인 계정의 소속만(물리 설계 3-3 "내 계정 부서 목록").
/// </summary>
public sealed class MyAccountService(ISystemDb systemDb)
{
    public async Task<IReadOnlyList<MyDept>> DeptsAsync(int accountId, CancellationToken ct = default)
    {
        await using var sys = systemDb.Create(accountId);
        var family = await sys.Accounts.Where(a => a.Id == accountId).Select(a => a.FamilyTenantId).SingleAsync(ct);
        var rows = await (from m in sys.Memberships
                          join t in sys.Tenants on m.TenantId equals t.Id
                          where m.AccountId == accountId && m.Status == MembershipStatus.Approved && t.Status == TenantStatus.Active
                          orderby t.Name
                          select new { t.Id, t.Name, t.Slug, t.ThemeColor, Roles = m.Roles.Select(r => r.Role).ToList() }).ToListAsync(ct);
        return rows.Select(r => new MyDept(r.Id, r.Name, r.Slug, r.ThemeColor,
            Roles.All.Where(x => r.Roles.Contains(x)).ToList(), family == r.Id)).ToList();
    }
}
