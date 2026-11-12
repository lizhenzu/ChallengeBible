using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Attendance;

/// <summary>
/// `/{slug}/attendance/report?sunday=` 주일 출결 보고서(PC) · `/{slug}/attendance/sunday?sunday=` 주일 출석 명단(휴대폰)
/// — 유년부 SundayStudentsPC · SundayStudentsMobile 과 같은 모양(3/9). 부서 관리자만.
/// </summary>
[TenantRoles(Roles.TenantAdmin)]
public class ReportModel(AttendanceService attendance, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "주일 출결 보고서";
    public TenantContext Tenant => tenant;
    [BindProperty(SupportsGet = true)] public DateOnly? Sunday { get; set; }
    public AttendanceService.SundayReport Rep { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        Rep = await attendance.SundayReportAsync(Sunday, ct);
        return Page();
    }

    public static int Pct(int p, int t) => t == 0 ? 0 : (int)Math.Round(p * 100.0 / t);
}

[TenantRoles(Roles.TenantAdmin)]
public class SundayListModel(AttendanceService attendance, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "주일 출석 명단";
    public TenantContext Tenant => tenant;
    [BindProperty(SupportsGet = true)] public DateOnly? Sunday { get; set; }
    public AttendanceService.SundayReport Rep { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        Rep = await attendance.SundayReportAsync(Sunday, ct);
        return Page();
    }
}
