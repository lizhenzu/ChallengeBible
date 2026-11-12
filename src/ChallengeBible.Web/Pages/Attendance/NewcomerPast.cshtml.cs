using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Attendance;

/// <summary>
/// `/{slug}/attendance/newcomers/past` 기존 출석 변경(확정 시안 K5 · K6 · K7, 2026-11-12). 올해만(연도 이동 없음).
/// 주소 없이 → 달 고르기(K5), ?M=11 → 그 달 주일 고르기(K6), ?D=2026-11-01 → 그 주일 새친구 출석 ↔ 결석(K7, 한 명씩, 바로 저장).
/// </summary>
[TenantRoles(Roles.TenantAdmin, Roles.Teacher)]
public class NewcomerPastModel(AttendanceService attendance, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "기존 출석 변경";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public int? M { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? D { get; set; }

    public int Year { get; private set; }
    /// <summary>부서 날짜로 이번 달(그 뒤 달은 흐리게).</summary>
    public int NowMonth { get; private set; }
    public DateOnly ThisSunday { get; private set; }
    public List<AttendanceService.NcSunday> Sundays { get; private set; } = [];
    public List<AttendanceService.NcOn> People { get; private set; } = [];
    /// <summary>달마다 주일 수(K5 칸 아래).</summary>
    public int SundaysIn(int month)
    {
        var d = new DateOnly(Year, month, 1);
        var first = d.AddDays(((int)DayOfWeek.Sunday - (int)d.DayOfWeek + 7) % 7);
        return (DateTime.DaysInMonth(Year, month) - first.Day) / 7 + 1;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        if (!(await attendance.ScopeAsync(User.UserName()!, ct)).Newcomers) return Forbid();
        var today = attendance.Today;
        (Year, NowMonth) = (today.Year, today.Month);
        ThisSunday = AttendanceService.SundayOf(today);
        if (D is { } d)
        {
            // 주일 · 올해 · 오지 않은 주일 아님만
            if (d.DayOfWeek != DayOfWeek.Sunday || d.Year != Year || d > ThisSunday) return RedirectToPage(new { slug = tenant.Current.Slug });
            M = d.Month;
            People = await attendance.NewcomersOnAsync(d, ct);
        }
        else if (M is int m)
        {
            if (m < 1 || m > 12 || m > today.Month) return RedirectToPage(new { slug = tenant.Current.Slug });
            Sundays = await attendance.NewcomerSundaysAsync(Year, m, ct);
        }
        return Page();
    }
}
