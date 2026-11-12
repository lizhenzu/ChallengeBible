using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/classes/unassigned` 미배정반(확정 시안 B-미배정반): 정규 반이 없는 아이를 골라 한꺼번에 배정. 부서 관리자만.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class UnassignedModel(ClassService classes, TenantContext tenant) : PageModel, ITitled
{
    public string Title => ClassService.UnassignedName;
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public int? Done { get; set; }
    public List<ClassPerson> Rows { get; private set; } = [];
    public List<ClassRow> Targets { get; private set; } = [];
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        await LoadAsync(ct);
        return Page();
    }

    /// <summary>to[사람 번호] = 반 번호(0 이면 그대로).</summary>
    public async Task<IActionResult> OnPostAsync(Dictionary<int, int> to, CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        var (e, n) = await classes.AssignAsync(to, ct);
        if (e == ClassError.None) return RedirectToPage(new { slug = tenant.Current.Slug, Done = n });
        Error = ClassService.Message(e);
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Rows = await classes.UnassignedAsync(ct);
        Targets = (await classes.ClassesAsync(ct)).Where(c => !c.IsNewcomer).ToList();
    }
}
