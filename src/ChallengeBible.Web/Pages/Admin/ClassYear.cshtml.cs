using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/classes/new-year` 새해 반 만들기(확정 시안 C-새해반-복사). 부서 관리자만.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class ClassYearModel(ClassService classes, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "새해 반 만들기";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public int From { get; set; }
    public TermRow? Source { get; private set; }
    public List<ClassRow> Rows { get; private set; } = [];
    public DateOnly Suggest { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) => await LoadAsync(ct) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(DateOnly start, CancellationToken ct)
    {
        var (e, term) = await classes.CopyAsync(From, start, ct);
        if (e == ClassError.None) return RedirectToPage("Classes", new { slug = tenant.Current.Slug, Term = term, Ok = "copy" });
        Error = ClassService.Message(e);
        return await LoadAsync(ct) ? Page() : NotFound();
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        Source = (await classes.TermsAsync(ct)).FirstOrDefault(t => t.Id == From);
        if (Source is null) return false;
        Rows = await classes.ClassesAsync(Source.Id, ct);
        Suggest = FirstSunday(Source.Year + 1);
        return true;
    }

    /// <summary>다음 해 1월 첫 주일(처음 값).</summary>
    public static DateOnly FirstSunday(int year)
    {
        var d = new DateOnly(year, 1, 1);
        return d.AddDays(((int)DayOfWeek.Sunday - (int)d.DayOfWeek + 7) % 7);
    }
}
