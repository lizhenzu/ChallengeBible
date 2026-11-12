using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Attendance;

/// <summary>
/// `/{slug}/attendance?d=` 출석부 "오늘"(확정 시안 A-출석부-오늘, ADR 0093 출석). 담임은 내 반, 부서 관리자는 모든 반.
/// 이름을 누르면 바로 저장(handler=Set, JSON). 새친구반 담당만이면 새친구 화면으로 보낸다.
/// </summary>
[TenantRoles(Roles.TenantAdmin, Roles.Teacher)]
public class IndexModel(AttendanceService attendance, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "출석";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public DateOnly? D { get; set; }
    public AttendBoard Board { get; private set; } = null!;
    public AttendScope Scope { get; private set; } = null!;
    private string Me => User.UserName()!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        Scope = await attendance.ScopeAsync(Me, ct);
        if (!Scope.Any) return Page();   // 맡은 반이 없다는 안내
        if (!Scope.Admin && Scope.Classes.Count == 0) return RedirectToPage("Newcomers", new { slug = tenant.Current.Slug, D });
        Board = await attendance.BoardAsync(Scope, D, ct);
        return Page();
    }

    public sealed record SetBody(int PersonId, DateOnly Sunday, bool Present);

    /// <summary>출석 · 취소 저장. 새친구 화면도 이 주소를 쓴다.</summary>
    public async Task<IActionResult> OnPostSetAsync([FromBody] SetBody body, CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        var scope = await attendance.ScopeAsync(Me, ct);
        var e = await attendance.SetAsync(scope, Me, body.PersonId, body.Sunday, body.Present, ct);
        return e == AttendError.None ? new JsonResult(new { ok = true }) : new JsonResult(new { ok = false, message = AttendanceService.Message(e) }) { StatusCode = 400 };
    }
}
