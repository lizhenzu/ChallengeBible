using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Attendance;

/// <summary>
/// `/{slug}/attendance/stats?year=&amp;month=` 월별 출석 통계 — 유년부 Statistics 와 같은 짜임(8/9, R-248): 월 카드 · 이번 달 TOP 3 반 · 요약. 부서 관리자만.
/// </summary>
[TenantRoles(Roles.TenantAdmin)]
public class StatsModel(AttendanceService attendance, TenantContext tenant, TimeProvider time) : PageModel, ITitled
{
    public string Title => "월별 출석 통계";
    public TenantContext Tenant => tenant;
    [BindProperty(SupportsGet = true)] public int? Year { get; set; }
    [BindProperty(SupportsGet = true)] public int? Month { get; set; }
    public AttendanceService.MonthStats S { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        var today = Domain.Time.TenantClock.ToTenantDate(time.GetUtcNow(), tenant.Current.TimeZoneId);
        S = await attendance.MonthStatsAsync(Year ?? today.Year, Month ?? today.Month, ct);
        return Page();
    }
}

/// <summary>
/// `/{slug}/attendance/stats/detail?year=&amp;month=` 월별 출석 상세 — 유년부 StatisticsDetail 과 같은 짜임(8/9, R-248):
/// 반별 출석 합계 가로 막대 · 주별 전체 출석 선 · 반별 표(반을 누르면 그 반 출석 체크). 그래프는 외부 스크립트 없이 CSS · SVG 로 그린다.
/// </summary>
[TenantRoles(Roles.TenantAdmin)]
public class StatsDetailModel(AttendanceService attendance, TenantContext tenant, TimeProvider time) : PageModel, ITitled
{
    public string Title => "월별 출석 상세";
    public TenantContext Tenant => tenant;
    [BindProperty(SupportsGet = true)] public int? Year { get; set; }
    [BindProperty(SupportsGet = true)] public int? Month { get; set; }
    public AttendanceService.MonthStats S { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        var today = Domain.Time.TenantClock.ToTenantDate(time.GetUtcNow(), tenant.Current.TimeZoneId);
        S = await attendance.MonthStatsAsync(Year ?? today.Year, Month ?? today.Month, ct);
        return Page();
    }

    public static string Pct(double r) => r.ToString(r == Math.Floor(r) ? "0" : "0.#", System.Globalization.CultureInfo.InvariantCulture);
}
