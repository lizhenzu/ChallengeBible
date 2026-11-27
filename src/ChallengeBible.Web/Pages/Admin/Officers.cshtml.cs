using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>
/// `/{slug}/admin/officers` 임원 및 기타(2026-11-21 사용자 "교사 외 → 임원 및 기타", 확정 시안 역할-담임-시안2 ②): 임원 직책 칸 · 봉사 · 기타 · 새친구.
/// 한 명 자리에 두 명(예: 부장 2명) · 빈 자리를 경고로 보여 주고, 바닥 창에서 역할을 바꾼다. 부서 관리자만.
/// </summary>
[TenantRoles(Roles.TenantAdmin)]
public class OfficersModel(StaffService staff, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "임원 및 기타";
    public TenantContext Tenant => tenant;
    public StaffBoard Board { get; private set; } = null!;
    [TempData] public string? Msg { get; set; }
    [TempData] public bool Bad { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => Board = await staff.BoardAsync(ct);

    public async Task<IActionResult> OnPostRoleAsync(int personId, string? code, CancellationToken ct)
    {
        var e = await staff.ChangeAsync(personId, code, ct);
        (Msg, Bad) = e == StaffError.None ? ("역할을 바꿨어요", false) : (StaffService.Message(e), true);
        return Redirect($"/{tenant.Current.Slug}/admin/officers");
    }
}
