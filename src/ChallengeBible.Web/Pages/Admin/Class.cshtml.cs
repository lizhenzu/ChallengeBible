using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/classes/{id}` 반 하나(확정 시안 B-반안-사람넣기): 이름 · 지우기 · 선생님 · 학생 넣고 빼기. 부서 관리자만.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class ClassModel(ClassService classes, TenantContext tenant) : PageModel, ITitled
{
    public string Title => Detail?.Class.Name ?? "반";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    public ClassDetail? Detail { get; private set; }
    public string? Error { get; private set; }

    public string OkMessage => Ok switch
    {
        "name" => "반 이름을 바꿨어요", "add" => "넣었어요", "remove" => "뺐어요", _ => "",
    };

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Detail = await classes.DetailAsync(Id, ct);
        return Detail is null ? NotFound() : Page();
    }

    public Task<IActionResult> OnPostNameAsync(string? name, CancellationToken ct) => Act("name", classes.RenameAsync(Id, name, ct), ct);
    public Task<IActionResult> OnPostTeacherAsync(int person, CancellationToken ct) => Act("add", classes.AddAsync(Id, [person], teacher: true, ct), ct);
    public Task<IActionResult> OnPostStudentsAsync(List<int> pick, CancellationToken ct) => Act("add", classes.AddAsync(Id, pick, teacher: false, ct), ct);
    public Task<IActionResult> OnPostRemoveAsync(int person, CancellationToken ct) => Act("remove", classes.RemoveAsync(Id, person, ct), ct);

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken ct)
    {
        var term = (await classes.DetailAsync(Id, ct))?.Term.Id;
        var e = await classes.DeleteAsync(Id, ct);
        if (e == ClassError.None) return RedirectToPage("Classes", new { slug = tenant.Current.Slug, Term = term, Ok = "del" });
        return await Fail(e, ct);
    }

    private async Task<IActionResult> Act(string ok, Task<ClassError> work, CancellationToken ct)
    {
        var e = await work;
        if (e == ClassError.None) return RedirectToPage(new { slug = tenant.Current.Slug, Id, Ok = ok });
        return await Fail(e, ct);
    }

    private async Task<IActionResult> Fail(ClassError e, CancellationToken ct)
    {
        Error = ClassService.Message(e);
        Detail = await classes.DetailAsync(Id, ct);
        return Detail is null ? NotFound() : Page();
    }
}
