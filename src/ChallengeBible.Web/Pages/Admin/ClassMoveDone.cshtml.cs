using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/classes/move/done` 반 이동 완료 화면(2026-11-12 사용자): 결과 · [반 목록으로] · [부서 홈으로]. 결과가 없으면 반 목록으로.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class ClassMoveDoneModel(TenantContext tenant) : PageModel, ITitled
{
    public string Title => "반 이동 완료";
    public TenantContext Tenant => tenant;

    public int Count { get; private set; }
    public string Route { get; private set; } = "";
    public string Names { get; private set; } = "";
    public string Skipped { get; private set; } = "";

    public IActionResult OnGet()
    {
        if (TempData["mvN"] is not int n) return RedirectToPage("Classes", new { slug = tenant.Current.Slug });
        Count = n;
        Route = TempData["mvRoute"] as string ?? "";
        Names = TempData["mvNames"] as string ?? "";
        Skipped = TempData["mvSkipped"] as string ?? "";
        return Page();
    }
}
