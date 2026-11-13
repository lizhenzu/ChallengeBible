using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Attendance;

/// <summary>`/{slug}/attendance/matrix?date=&amp;weeks=` 아이별 주별 출석표(52주) — 유년부 AdminStudentMatrixWeeks 와 같은 모양(6/9). 부서 관리자만.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class MatrixModel(AttendanceService attendance, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "아이별 주별 출석표";
    public TenantContext Tenant => tenant;
    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }
    [BindProperty(SupportsGet = true)] public int? Weeks { get; set; }
    public AttendanceService.Matrix M { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        M = await attendance.MatrixAsync(Date, Weeks, ct);
        return Page();
    }
}
