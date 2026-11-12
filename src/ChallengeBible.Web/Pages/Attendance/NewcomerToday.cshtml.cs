using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Attendance;

/// <summary>`/{slug}/attendance/newcomers/today` 오늘 출석 체크(확정 시안 K2): 이번 주일 · 관리 중 새친구만 · 한 명씩(누르면 저장, 다시 누르면 취소).</summary>
[TenantRoles(Roles.TenantAdmin, Roles.Teacher)]
public class NewcomerTodayModel(AttendanceService attendance, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "오늘 출석 체크";
    public TenantContext Tenant => tenant;
    public AttendanceService.NcHome Home { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        if (!(await attendance.ScopeAsync(User.UserName()!, ct)).Newcomers) return Forbid();
        Home = await attendance.NewcomerHomeAsync(ct);
        return Page();
    }
}
