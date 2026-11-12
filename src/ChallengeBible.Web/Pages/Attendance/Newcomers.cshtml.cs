using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Attendance;

/// <summary>`/{slug}/attendance/newcomers?d=` 새친구(확정 시안 B-새친구): 새친구반 담당 · 부서 관리자. [출석]은 출석부와 같은 저장(handler=Set).</summary>
[TenantRoles(Roles.TenantAdmin, Roles.Teacher)]
public class NewcomersModel(AttendanceService attendance, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "새친구";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public DateOnly? D { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }
    public DateOnly Sunday { get; private set; }
    public DateOnly ThisSunday { get; private set; }
    public List<NewcomerRow> Rows { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        var scope = await attendance.ScopeAsync(User.UserName()!, ct);
        if (!scope.Newcomers) return Forbid();
        ThisSunday = AttendanceService.SundayOf(attendance.Today);
        Sunday = D is { } d && AttendanceService.SundayOf(d) < ThisSunday ? AttendanceService.SundayOf(d) : ThisSunday;
        Rows = await attendance.NewcomersAsync(Sunday, ct);
        return Page();
    }
}
