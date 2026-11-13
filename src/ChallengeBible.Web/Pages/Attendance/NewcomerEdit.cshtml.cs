using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Attendance;

/// <summary>
/// `/{slug}/attendance/newcomers/edit?id=` 새친구 정보 고치기(유년부 UpdateNewComerInfo, R-251): 이름 · 생일 · 연락처 · 데려온 사람.
/// 새친구반 담당 · 부서 관리자. 출석 기록 · 첫 방문일 · 반은 건드리지 않는다.
/// </summary>
[TenantRoles(Roles.TenantAdmin, Roles.Teacher)]
public class NewcomerEditModel(AttendanceService attendance, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "새친구 정보 고치기";
    public TenantContext Tenant => tenant;
    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty] public string? Name { get; set; }
    [BindProperty] public int? Month { get; set; }
    [BindProperty] public int? Day { get; set; }
    [BindProperty] public string? Phone1 { get; set; }
    [BindProperty] public string? Phone2 { get; set; }
    [BindProperty] public bool Voluntary { get; set; } = true;
    [BindProperty] public string? Evangelist { get; set; }
    public bool Linked { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await AllowedAsync(ct)) return Forbid();
        var e = await attendance.NewcomerEditAsync(Id, ct);
        if (e is null) return NotFound();
        (Name, Month, Day, Phone1, Phone2, Voluntary, Evangelist, Linked) = (e.Name, e.BirthMonth, e.BirthDay, e.Phone1, e.Phone2, e.Voluntary, e.Evangelist, e.Linked);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!await AllowedAsync(ct)) return Forbid();
        var err = await attendance.UpdateNewcomerAsync(Id, new NewcomerEdit(Name, Month, Day, Phone1, Phone2, Voluntary, Evangelist), ct);
        if (err == AttendError.None) return RedirectToPage("NewcomerDetail", new { slug = tenant.Current.Slug, id = Id, Ok = "edit" });
        if (err == AttendError.NotFound) return NotFound();
        Error = AttendanceService.Message(err);
        Linked = (await attendance.NewcomerEditAsync(Id, ct))?.Linked ?? false;
        return Page();
    }

    private async Task<bool> AllowedAsync(CancellationToken ct) => !tenant.IsLobby && (await attendance.ScopeAsync(User.UserName()!, ct)).Newcomers;
}
