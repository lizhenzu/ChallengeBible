using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Attendance;

/// <summary>
/// `/{slug}/attendance/dashboard?date=&amp;classId=` 반 출석 대시보드 — 유년부 Dashboard 와 같은 짜임(7/9). 부서 관리자만.
/// 유년부의 "주간 통계(월 ~ 일 7일)"는 주일만 출석을 받는 우리에게 맞지 않아 최근 7주일로 바꿨다.
/// </summary>
[TenantRoles(Roles.TenantAdmin)]
public class DashboardModel(AttendanceService attendance, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "출석 대시보드";
    public TenantContext Tenant => tenant;
    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }
    [BindProperty(SupportsGet = true)] public int? ClassId { get; set; }
    public AttendanceService.Matrix M { get; private set; } = null!;
    public AttendanceService.RepClass? Class { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        M = await attendance.MatrixAsync(Date, 7, ct);
        Class = M.Rep.Classes.FirstOrDefault(c => c.Id > 0 && c.Id == ClassId);
        return Page();
    }

    public static int Rate(int p, int t) => t == 0 ? 0 : (int)Math.Round(p * 100.0 / t);
}

/// <summary>`/{slug}/attendance/dashboard/all?date=` 전체 반 출석현황 — 유년부 DashboardAll 과 같은 짜임(7/9). 미체크 = 그 주일 출석 0명인 반.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class DashboardAllModel(AttendanceService attendance, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "전체반 출석현황";
    public TenantContext Tenant => tenant;
    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }
    public AttendanceService.SundayReport Rep { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        Rep = await attendance.SundayReportAsync(Date, ct);
        return Page();
    }
}
