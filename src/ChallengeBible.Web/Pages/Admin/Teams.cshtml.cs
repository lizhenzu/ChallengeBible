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
public class TeamsModel(FamilyTeamService teams, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "가족팀";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public int? Open { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    public FamilyBoard Board { get; private set; } = null!;
    public string? Error { get; private set; }
    public int? ErrorFor { get; private set; }
    private string Me => User.UserName()!;

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

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        Board = await teams.GetAsync(ct);
        return Board.Enabled;
    }
}
