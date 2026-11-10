using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>
/// `/{slug}/admin/teams` 가족팀 · 친구팀 관리(ADR 0083 → 0093, R-210 · R-222 · R-223, 확정 시안 관리-팀-만들기-방장). 영역 관리자만.
/// 팀 만들기(`?New=true`) · 구성원 바로 넣기 · 방장 바꾸기 · 이름 고치기 · 구성원 빼기 · 팀 삭제. 선물을 받은 팀은 빼기 · 넣기 · 삭제가 막힌다.
/// 기본 규칙 "가족팀" 스위치가 꺼지면 404(0012 — 화면 숨김만으로 막지 않음). JS 없이 폼마다 서버가 다시 그린다.
/// </summary>
[TenantRoles(Roles.TenantAdmin)]
public class TeamsModel(FamilyTeamService teams, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "가족팀";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public int? Open { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }
    [BindProperty(SupportsGet = true)] public bool New { get; set; }

    public FamilyBoard Board { get; private set; } = null!;
    public string? Error { get; private set; }
    public int? ErrorFor { get; private set; }
    /// <summary>새 팀 만들기 칸(오류가 나면 고른 값 그대로).</summary>
    public (string Type, string? Name, string? Creator, string? Relation) NewForm { get; private set; } = (TeamTypes.Family, null, null, FamilyRelation.Student);

    public IEnumerable<FamilyTeamView> Shown => string.IsNullOrWhiteSpace(Q) ? Board.Teams
        : Board.Teams.Where(t => t.DisplayName.Contains(Q.Trim()) || t.Members.Any(m => m.Name.Contains(Q.Trim())));

    public string OkMessage => Ok switch
    {
        "renamed" => "팀 이름을 고쳤어요",
        "removed" => "팀에서 뺐어요 · 점수는 그대로예요",
        "created" => "팀을 만들었어요. 구성원을 더 넣어 주세요",
        "added" => "구성원을 넣었어요. 다음에 들어오면 알림이 보여요",
        "creator" => "방장을 바꿨어요",
        "deleted" => "팀을 지웠어요 · 구성원의 점수는 그대로예요",
        _ => "",
    };

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) => await LoadAsync(ct) ? Page() : NotFound();

    public Task<IActionResult> OnPostRenameAsync(int id, string? name, CancellationToken ct) => Act(id, "renamed", teams.RenameAsync(id, name, ct), ct);
    public Task<IActionResult> OnPostRemoveAsync(int id, int member, CancellationToken ct) => Act(id, "removed", teams.RemoveMemberAsync(id, member, ct), ct);
    public Task<IActionResult> OnPostAddAsync(int id, string? person, string? relation, CancellationToken ct) => Act(id, "added", teams.AddMemberAsync(id, person, relation, ct), ct);
    public Task<IActionResult> OnPostCreatorAsync(int id, int member, CancellationToken ct) => Act(id, "creator", teams.SetCreatorAsync(id, member, ct), ct);
    public Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct) => Act(id, "deleted", teams.DeleteTeamAsync(id, ct), ct, closeOnOk: true);

    public async Task<IActionResult> OnPostCreateAsync(string? type, string? name, string? creator, string? relation, CancellationToken ct)
    {
        var (e, id) = await teams.CreateTeamAsync(type, name, creator, relation, ct);
        if (e == FamilyError.Disabled) return NotFound();
        if (e == FamilyError.None) return RedirectToPage(new { slug = tenant.Current.Slug, Open = id, Ok = "created" });
        if (!await LoadAsync(ct)) return NotFound();
        New = true;
        NewForm = (type ?? TeamTypes.Family, name, creator, relation);
        Error = FamilyTeamService.Message(e);
        return Page();
    }

    private async Task<IActionResult> Act(int id, string ok, Task<FamilyError> work, CancellationToken ct, bool closeOnOk = false)
    {
        var e = await work;
        if (e == FamilyError.Disabled) return NotFound();
        if (e == FamilyError.None) return RedirectToPage(new { slug = tenant.Current.Slug, Open = closeOnOk ? (int?)null : id, Q, Ok = ok });
        if (!await LoadAsync(ct)) return NotFound();
        Error = FamilyTeamService.Message(e);
        ErrorFor = id;
        Open = id;
        return Page();
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        Board = await teams.GetAsync(ct);
        return Board.Enabled;
    }
}
