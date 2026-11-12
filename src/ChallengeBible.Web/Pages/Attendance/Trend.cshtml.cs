using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Attendance;

/// <summary>
/// `/{slug}/attendance/trend?date=` 최근 전체 출석현황(3주) · `/{slug}/attendance/byclass?sunday=` 주일 반별 출석
/// — 유년부 AdminTrend3Weeks · AdminTrendSundayDetail 과 같은 모양(4/9). 부서 관리자만. 총원은 지금 반 편성 기준(R-243 명단).
/// </summary>
[TenantRoles(Roles.TenantAdmin)]
public class TrendModel(AttendanceService attendance, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "전체 출석현황";
    public TenantContext Tenant => tenant;
    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }
    /// <summary>오래된 주 → 기준 주일(3개).</summary>
    public List<AttendanceService.SundayReport> Weeks { get; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        var now = await attendance.SundayReportAsync(Date, ct);
        foreach (var back in new[] { 14, 7 }) Weeks.Add(await attendance.SundayReportAsync(now.Sunday.AddDays(-back), ct));
        Weeks.Add(now);
        return Page();
    }

    public static (int Total, int Present) Count(AttendanceService.SundayReport r) =>
        (r.Classes.Sum(c => c.Kids.Count), r.Classes.Sum(c => c.Kids.Count(k => k.Present)));
    public static double Rate(int p, int t) => t == 0 ? 0 : Math.Round(p * 1000.0 / t) / 10;
    public static string Pct(double r) => r.ToString(r == Math.Floor(r) ? "0" : "0.#", System.Globalization.CultureInfo.InvariantCulture);
}

[TenantRoles(Roles.TenantAdmin)]
public class ByClassModel(AttendanceService attendance, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "반별 출석";
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
