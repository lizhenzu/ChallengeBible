using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Attendance;

/// <summary>
/// `/{slug}/attendance/newcomers/detail?id=` 새친구 상세(유년부 NewComerDetail, 9/9, R-249): 첫 방문 · 데려온 사람 · 주일별 출석.
/// 보기는 새친구반 담당 · 부서 관리자, 지우기는 부서 관리자만(사진 · 학교 · 부모 이름은 쓰지 않기로 함).
/// </summary>
[TenantRoles(Roles.TenantAdmin, Roles.Teacher)]
public class NewcomerDetailModel(AttendanceService attendance, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "새친구 상세";
    public TenantContext Tenant => tenant;
    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }
    public AttendanceService.NcDetail D { get; private set; } = null!;
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        if (!(await attendance.ScopeAsync(User.UserName()!, ct)).Newcomers) return Forbid();
        var d = await attendance.NewcomerDetailAsync(Id, ct);
        if (d is null) return NotFound();
        D = d;
        return Page();
    }

}
