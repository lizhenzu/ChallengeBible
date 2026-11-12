using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>
/// `/{slug}/admin/classes/move` 반 이동 마법사(확정 시안 1-반고르기 · 2-아이고르기 · 3-보낼곳 · 4-확인, 2026-11-12). 부서 관리자만.
/// 고르는 단계는 주소(F 보낼 반 · P 아이 · D 보낼 곳 · T 반 · X 부서)로 넘기고, 마지막 [옮기기]만 저장한다.
/// </summary>
[TenantRoles(Roles.TenantAdmin)]
public class ClassMoveModel(ClassService classes, ClassMoveService mover, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "반 이동";
    public TenantContext Tenant => tenant;
    public const string FromUnassigned = "u";

    [BindProperty(SupportsGet = true)] public string? F { get; set; }
    [BindProperty(SupportsGet = true)] public List<int> P { get; set; } = [];
    [BindProperty(SupportsGet = true)] public MoveDest? D { get; set; }
    [BindProperty(SupportsGet = true)] public int? T { get; set; }
    [BindProperty(SupportsGet = true)] public int? X { get; set; }
    [BindProperty(SupportsGet = true)] public int S { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    public List<ClassRow> Rows { get; private set; } = [];
    public int UnassignedCount { get; private set; }
    public List<ClassPerson> Kids { get; private set; } = [];
    public List<MoveTarget> Depts { get; private set; } = [];
    public string FromName { get; private set; } = "";
    public string? FromTeacher { get; private set; }
    public string? Error { get; private set; }
    private string Me => User.UserName()!;

    public int? FromId => F == FromUnassigned ? null : int.TryParse(F, out var id) ? id : null;
    public List<ClassPerson> Picked => Kids.Where(k => P.Contains(k.PersonId)).ToList();
    public string DestText => D switch
    {
        MoveDest.Unassigned => ClassService.UnassignedName,
        MoveDest.Class => Rows.FirstOrDefault(r => r.Id == T)?.Name ?? "?",
        MoveDest.Dept => $"{Depts.FirstOrDefault(t => t.TenantId == X)?.Name ?? "?"} 미배정반",
        _ => "",
    };

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        await LoadAsync(ct);
        // 단계는 고른 값이 있어야 넘어간다(뒤로 가기 · 주소 고치기에도 안전하게)
        if (S >= 2 && F is null) S = 1;
        if (S >= 3 && Picked.Count == 0) { Error = S > 2 ? ClassMoveService.Message(MoveClassError.Empty) : null; S = 2; }
        if (S >= 4 && !DestOk) { Error = ClassMoveService.Message(MoveClassError.BadTarget); S = 3; }
        return Page();
    }

    private bool DestOk => D switch
    {
        MoveDest.Unassigned => FromId is not null,
        MoveDest.Class => T is int t && t != FromId && Rows.Any(r => r.Id == t),
        MoveDest.Dept => X is int x && Depts.Any(d => d.TenantId == x),
        _ => false,
    };

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (tenant.IsLobby || D is not MoveDest dest) return NotFound();
        var (e, n, skipped) = await mover.MoveAsync(Me, FromId, P, dest, T, X, ct);
        if (e == MoveClassError.None && skipped.Count == 0)
            return RedirectToPage("Classes", new { slug = tenant.Current.Slug, Ok = $"mv{n}" });   // 끝나면 반 목록으로(2026-11-12 사용자)
        Error = e != MoveClassError.None ? ClassMoveService.Message(e)
            : $"{n}명을 옮겼어요. 선물 받은 팀에 있어 옮기지 못한 아이(이 부서 미배정반에 남음): {string.Join(", ", skipped)}";
        S = 1;
        F = null;
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Rows = (await classes.ClassesAsync(ct)).Where(r => !r.IsNewcomer).ToList();
        UnassignedCount = (await classes.UnassignedAsync(ct)).Count;
        Depts = await mover.TargetsAsync(Me, ct);
        if (F is null) return;
        if (FromId is int id && Rows.FirstOrDefault(r => r.Id == id) is { } row) { FromName = row.Name; FromTeacher = row.Teachers.Count > 0 ? string.Join(" · ", row.Teachers) : null; }
        else if (F == FromUnassigned) FromName = ClassService.UnassignedName;
        else { F = null; return; }
        Kids = await mover.KidsAsync(FromId, ct);
    }
}
