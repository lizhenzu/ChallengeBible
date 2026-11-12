using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/classes/move?c=` 반 이동(확정 시안 A-반이동, ADR 0093 반별 학생 이동). 부서 관리자만, 반별로만 옮긴다.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class ClassMoveModel(ClassService classes, ClassMoveService mover, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "반 이동";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public int? C { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    public List<ClassRow> Rows { get; private set; } = [];
    public ClassDetail? Picked { get; private set; }
    public List<MoveTarget> Targets { get; private set; } = [];
    public string? Error { get; private set; }
    private string Me => User.UserName()!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostUnassignedAsync(CancellationToken ct)
    {
        if (tenant.IsLobby || C is not int c) return NotFound();
        var (e, n) = await mover.ToUnassignedAsync(c, ct);
        if (e == MoveClassError.None) return RedirectToPage(new { slug = tenant.Current.Slug, C = c, Ok = $"un{n}" });
        Error = ClassMoveService.Message(e);
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostDeptAsync(int target, CancellationToken ct)
    {
        if (tenant.IsLobby || C is not int c) return NotFound();
        var (e, n, skipped) = await mover.ToDeptAsync(Me, c, target, ct);
        if (e == MoveClassError.None && skipped.Count == 0) return RedirectToPage(new { slug = tenant.Current.Slug, C = c, Ok = $"dept{n}" });
        Error = e != MoveClassError.None ? ClassMoveService.Message(e)
            : $"{n}명을 옮겼어요. 선물 받은 팀에 있어 옮기지 못한 아이(이 부서 미배정반에 남음): {string.Join(", ", skipped)}";
        await LoadAsync(ct);
        return Page();
    }

    public string OkMessage => Ok switch
    {
        { } s when s.StartsWith("un") => $"{s[2..]}명을 미배정반으로 옮겼어요 · 이전 반 · 담임 정보는 꼬리표로 남았어요",
        { } s when s.StartsWith("dept") => $"{s[4..]}명을 다른 부서 미배정반으로 옮겼어요",
        _ => "",
    };

    private async Task LoadAsync(CancellationToken ct)
    {
        Rows = (await classes.ClassesAsync(ct)).Where(r => !r.IsNewcomer).ToList();
        if (C is int c && Rows.Any(r => r.Id == c)) Picked = await classes.DetailAsync(c, ct);
        Targets = await mover.TargetsAsync(Me, ct);
    }
}
