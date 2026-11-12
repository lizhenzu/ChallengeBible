using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/classes` 반 목록(확정 시안 A-반목록, ADR 0093 조직 · 반). 부서 관리자만.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class ClassesModel(ClassService classes, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "반";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public int? Term { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    public List<TermRow> Terms { get; private set; } = [];
    public TermRow? Shown { get; private set; }
    public List<ClassRow> Rows { get; private set; } = [];
    public List<ClassPerson> Unassigned { get; private set; } = [];
    public string? Error { get; private set; }
    public bool AddOpen { get; private set; }

    public string OkMessage => Ok switch
    {
        "add" => "반을 만들었어요", "del" => "반을 지웠어요", "copy" => "새해 반을 만들었어요 · 이름 · 사람을 고쳐 주세요", "start" => "시작 날짜를 바꿨어요", _ => "",
    };

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync(string? name, bool newcomer, CancellationToken ct)
    {
        var (e, _) = await classes.CreateAsync(Term, name, newcomer, ct);
        if (e == ClassError.None) return RedirectToPage(new { slug = tenant.Current.Slug, Term, Ok = "add" });
        (Error, AddOpen) = (ClassService.Message(e), true);
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostStartAsync(DateOnly start, CancellationToken ct)
    {
        var e = Term is int t ? await classes.SetStartAsync(t, start, ct) : ClassError.NotFound;
        if (e == ClassError.None) return RedirectToPage(new { slug = tenant.Current.Slug, Term, Ok = "start" });
        Error = ClassService.Message(e);
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Terms = await classes.TermsAsync(ct);
        Shown = Terms.FirstOrDefault(t => t.Id == Term) ?? Terms.FirstOrDefault(t => t.IsCurrent);
        Term = Shown?.Id;
        if (Shown is not null) Rows = await classes.ClassesAsync(Shown.Id, ct);
        Unassigned = await classes.UnassignedAsync(Shown?.Id, ct);
    }
}
