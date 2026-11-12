using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Attendance;

/// <summary>
/// `/{slug}/attendance/newcomers` 새친구 홈(확정 시안 K1, 2026-11-12): 메뉴 3개(오늘 출석 체크 · 기존 출석 변경 · 새친구 등록)와
/// 4주 관리 중(주황) · 4주 관리 완료(초록) 목록. 주일 이동은 없다. 새친구반 담당 · 부서 관리자.
/// </summary>
[TenantRoles(Roles.TenantAdmin, Roles.Teacher)]
public class NewcomersModel(AttendanceService attendance, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "새친구";
    public TenantContext Tenant => tenant;
    public const int DoneShown = 2;

    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }
    public AttendanceService.NcHome Home { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        if (!(await attendance.ScopeAsync(User.UserName()!, ct)).Newcomers) return Forbid();
        Home = await attendance.NewcomerHomeAsync(ct);
        return Page();
    }
}
