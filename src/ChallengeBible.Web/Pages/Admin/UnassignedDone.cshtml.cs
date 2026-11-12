using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/classes/unassigned/done` 미배정반 배정 완료(확정 시안 G-배정완료, 2026-11-12): 반마다 넣은 아이 · [그 반으로 →]. 결과가 없으면 미배정반으로.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class UnassignedDoneModel(TenantContext tenant) : PageModel, ITitled
{
    public string Title => "배정 완료";
    public TenantContext Tenant => tenant;

    public sealed record Group(int ClassId, string ClassName, string Names)
    {
        public int Count => Names.Split(" · ").Length;
    }

    public int Count { get; private set; }
    public List<Group> Groups { get; private set; } = [];

    public IActionResult OnGet()
    {
        if (TempData["asgN"] is not int n) return RedirectToPage("Unassigned", new { slug = tenant.Current.Slug });
        Count = n;
        Groups = ((TempData["asgGroups"] as string) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('\t')).Where(p => p.Length == 3 && int.TryParse(p[0], out _))
            .Select(p => new Group(int.Parse(p[0]), p[1], p[2])).ToList();
        return Page();
    }
}
