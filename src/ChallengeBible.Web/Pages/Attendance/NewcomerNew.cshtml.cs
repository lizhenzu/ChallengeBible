using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Attendance;

/// <summary>`/{slug}/attendance/newcomers/new` 새친구 등록(확정 시안 C-새친구-등록, R-236). 새친구반 담당 · 부서 관리자.</summary>
[TenantRoles(Roles.TenantAdmin, Roles.Teacher)]
public class NewcomerNewModel(AttendanceService attendance, ClassService classes, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "새친구 등록";
    public TenantContext Tenant => tenant;

    [BindProperty] public string? Name { get; set; }
    [BindProperty] public DateOnly? FirstVisit { get; set; }
    [BindProperty] public int? Month { get; set; }
    [BindProperty] public int? Day { get; set; }
    [BindProperty] public string? Phone1 { get; set; }
    [BindProperty] public string? Phone2 { get; set; }
    [BindProperty] public int? ClassId { get; set; }
    [BindProperty] public bool Voluntary { get; set; } = true;
    [BindProperty] public string? Evangelist { get; set; }

    public List<ClassRow> Classes { get; private set; } = [];
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await AllowedAsync(ct)) return Forbid();
        FirstVisit = attendance.Today;
        Classes = await RegularAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!await AllowedAsync(ct)) return Forbid();
        var (e, _) = await attendance.RegisterAsync(User.UserName()!, new NewcomerInput(Name, FirstVisit, Month, Day, Phone1, Phone2, ClassId, Voluntary, Evangelist), ct);
        if (e == AttendError.None) return RedirectToPage("Newcomers", new { slug = tenant.Current.Slug, Ok = "add" });
        Error = AttendanceService.Message(e);
        Classes = await RegularAsync(ct);
        return Page();
    }

    private async Task<bool> AllowedAsync(CancellationToken ct) => !tenant.IsLobby && (await attendance.ScopeAsync(User.UserName()!, ct)).Newcomers;
    private async Task<List<ClassRow>> RegularAsync(CancellationToken ct) => (await classes.ClassesAsync(ct)).Where(c => !c.IsNewcomer).ToList();
}
