using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>
/// `/{slug}/admin/teachers` 교사(2026-11-21 사용자, 확정 시안 역할-담임-시안2 ①): 반별 담임 · 부담임과 반이 없는 교사. 부서 관리자만.
/// 넣기 · 빼기 · 다른 반으로는 반 화면과 같은 규칙(ClassService, R-232: 한 사람은 한 반만, 교사 · 임원이 담임).
/// </summary>
[TenantRoles(Roles.TenantAdmin)]
public class TeachersModel(ClassService classes, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "교사";
    public TenantContext Tenant => tenant;
    public TeacherBoard Board { get; private set; } = null!;
    [TempData] public string? Msg { get; set; }
    [TempData] public bool Bad { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => Board = await classes.TeacherBoardAsync(ct);

    public Task<IActionResult> OnPostAddAsync(int classId, int personId, CancellationToken ct) => Done(classes.AddAsync(classId, [personId], teacher: true, ct), "넣었어요");
    public Task<IActionResult> OnPostRemoveAsync(int classId, int personId, CancellationToken ct) => Done(classes.RemoveAsync(classId, personId, ct), "뺐어요");
    public Task<IActionResult> OnPostMoveAsync(int fromId, int toId, int personId, CancellationToken ct) => Done(classes.MoveTeacherAsync(fromId, toId, personId, ct), "옮겼어요");

    private async Task<IActionResult> Done(Task<ClassError> work, string ok)
    {
        var e = await work;
        (Msg, Bad) = e == ClassError.None ? (ok, false) : (Message(e), true);
        return Redirect($"/{tenant.Current.Slug}/admin/teachers");
    }

    public static string Message(ClassError e) => e switch
    {
        ClassError.InOtherClass => "이미 다른 반에 있어요. 그 반에서 먼저 빼거나 [다른 반으로]를 써 주세요.",
        ClassError.NotEligible => "교사 · 임원만 담임이 될 수 있어요(새친구반 담당은 관리자도).",
        _ => "반이나 사람을 찾을 수 없어요. 새로고침해 주세요.",
    };
}
