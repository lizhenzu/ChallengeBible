using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>
/// `/{slug}/admin/teams` 가족팀 보기와 바로잡기(ADR 0083, R-210). 영역 관리자만. 만들기 · 합류는 가족이 직접 한다.
/// 기본 규칙 "가족팀" 스위치가 꺼지면 404(0012 — 화면 숨김만으로 막지 않음). JS 없이 폼마다 서버가 다시 그린다.
/// </summary>
[TenantRoles(Roles.TenantAdmin)]
public class TeamsModel(FamilyTeamService teams, TenantContext tenant, OnFamilyService onFamily) : PageModel, ITitled
{
    public string Title => "가족팀";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public int? Open { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    public FamilyBoard Board { get; private set; } = null!;
    public string? Error { get; private set; }
    public int? ErrorFor { get; private set; }
    private int Me => User.UserName()!.Value;

    public IEnumerable<FamilyTeamView> Shown => string.IsNullOrWhiteSpace(Q) ? Board.Teams
        : Board.Teams.Where(t => t.DisplayName.Contains(Q.Trim()) || t.Members.Any(m => m.Name.Contains(Q.Trim())));

    public string OkMessage => Ok switch
    {
        "renamed" => "팀 이름을 고쳤어요",
        "removed" => "팀에서 뺐어요 · 점수는 그대로예요",
        _ => "",
    };

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) => await LoadAsync(ct) ? Page() : NotFound();

    public Task<IActionResult> OnPostRenameAsync(int id, string? name, CancellationToken ct) => Act(id, "renamed", teams.RenameAsync(id, name, ct), ct);
    public Task<IActionResult> OnPostRemoveAsync(int id, int member, CancellationToken ct) => Act(id, "removed", teams.RemoveMemberAsync(id, member, ct), ct);

    private async Task<IActionResult> Act(int id, string ok, Task<FamilyError> work, CancellationToken ct)
    {
        var e = await work;
        if (e == FamilyError.None) return RedirectToPage(new { slug = tenant.Current.Slug, Open = id, Q, Ok = ok });
        if (!await LoadAsync(ct)) return NotFound();
        Error = FamilyTeamService.Message(e);
        ErrorFor = id;
        Open = id;
        return Page();
    }

    /// <summary>이 부서 사람의 계정 Id → 그 사람이 든 온가족(R-178, 다른 부서 구성원은 이름을 가림). 팀 카드 안에 총점 한 줄.</summary>
    public Dictionary<int, FamilyBoardView> OnFamilies { get; private set; } = [];

    /// <summary>이 팀 구성원이 든 온가족들(겹치면 하나로).</summary>
    public IEnumerable<FamilyBoardView> LinkedFamiliesOf(FamilyTeamView t) =>
        t.Members.Select(m => OnFamilies.GetValueOrDefault(m.UserName)).OfType<FamilyBoardView>().DistinctBy(f => f.Id);

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        Board = await teams.GetAsync(ct);
        if (Board.Enabled) OnFamilies = await onFamily.ForTenantAsync(Me, tenant.Current.Id, ct);
        return Board.Enabled;
    }
}
