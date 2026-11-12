using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/classes` 반 목록(확정 시안 A-반목록-단순, ADR 0093 반 관리 및 자동 생성 규칙). 부서 관리자만.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class ClassesModel(ClassService classes, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "반";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    public List<ClassRow> Rows { get; private set; } = [];
    public int Unassigned { get; private set; }
    public string? Error { get; private set; }

    public string OkMessage => Ok switch { "add" => "반을 만들었어요", "del" => "반을 지웠어요 · 그 반 아이들은 미배정반으로 갔어요", _ => "" };

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync(string? name, CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        var (e, _) = await classes.CreateAsync(name, ct);
        if (e == ClassError.None) return RedirectToPage(new { slug = tenant.Current.Slug, Ok = "add" });
        Error = ClassService.Message(e);
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Rows = await classes.ClassesAsync(ct);
        Unassigned = (await classes.UnassignedAsync(ct)).Count;
    }
}
