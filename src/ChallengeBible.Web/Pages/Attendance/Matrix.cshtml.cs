using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Attendance;

/// <summary>
/// `/{slug}/attendance/matrix?date=` 아이별 주별 출석표 — 올해 1월 첫 주일부터 오늘(또는 date)이 든 달의 마지막 주일까지, 한 화면에 다.
/// 데스크탑 전용(휴대폰에서도 PC 폭으로 그려 줄여 보인다), 위에 주일별 출석 막대 그래프. 부서 관리자만. (확정 시안 2026-11-13)
/// </summary>
[TenantRoles(Roles.TenantAdmin)]
public class MatrixModel(AttendanceService attendance, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "아이별 주별 출석표";
    public TenantContext Tenant => tenant;
    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }
    public AttendanceService.Matrix M { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        M = await attendance.MatrixMonthAsync(Date, ct);
        return Page();
    }
}
