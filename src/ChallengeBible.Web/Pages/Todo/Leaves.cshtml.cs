using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Todo;

/// <summary>`/{slug}/leaves` 탈퇴 신청(ADR 0119, 확정 시안 탈퇴메뉴 C): 이 부서의 기다리는 탈퇴 — 부서 관리자만.</summary>
public class LeavesModel(LeaveService leave, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "탈퇴 신청";
    public string Slug => tenant.Current.Slug;
    public TenantContext Tenant => tenant;
    public IReadOnlyList<LeaveRow> Leaves { get; private set; } = [];
    [TempData] public string? Msg { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby || !tenant.IsAdmin) return NotFound();
        Leaves = await leave.ListAsync(User.UserName()!, tenant.Current.Id, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostLeaveAsync(int leaveId, string? leaveDo, CancellationToken ct)
    {
        if (tenant.IsLobby || !tenant.IsAdmin) return Forbid();
        Msg = await LeaveActions.DoAsync(leave, User.UserName()!, leaveId, leaveDo, tenant.Current.Id, ct);
        return Redirect($"/{Slug}/leaves");
    }
}
