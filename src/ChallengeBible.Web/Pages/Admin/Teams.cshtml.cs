using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>
/// `/{slug}/admin/teams` 가족팀 관리(확정 시안: 관리-가족팀, R-90 ~ R-97, 0046). 영역 관리자만.
/// 기본 규칙 "가족팀" 스위치가 꺼지면 404(0012 — 화면 숨김만으로 막지 않음). JS 없이 폼마다 서버가 다시 그린다.
/// </summary>
[TenantRoles(Roles.TenantAdmin)]
public class TeamsModel(FamilyTeamService teams, TenantContext tenant, OnFamilyService onFamily) : PageModel, ITitled
{
    public string Title => "가족팀";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public int? Open { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    /// <summary>"이 사람으로 팀 만들기" — 새 가족팀 칸을 펼치고 이 참여자를 골라 둔다.</summary>
    [BindProperty(SupportsGet = true)] public int? New { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    public FamilyBoard Board { get; private set; } = null!;
    public string? Error { get; private set; }
    public int? ErrorFor { get; private set; }
    public bool CreateOpen { get; private set; }
    private int Me => User.AccountId()!.Value;

    public IEnumerable<FamilyTeamView> Shown => string.IsNullOrWhiteSpace(Q) ? Board.Teams
        : Board.Teams.Where(t => t.DisplayName.Contains(Q.Trim()) || t.Members.Any(m => m.Name.Contains(Q.Trim())));
    public IEnumerable<FamilySolo> Joinable => Board.Solo.Where(s => s.Blocked == FamilyError.None);

    public string OkMessage => Ok switch
    {
        "created" => "가족팀을 만들었어요 · 관계를 확인해 주세요",
        "saved" => "저장했어요",
        "added" => "구성원을 더했어요 · 관계를 확인해 주세요",
        "removed" => "팀에서 뺐어요 · 점수는 그대로예요",
        "stopped" => "팀을 멈췄어요 · 구성원은 다른 팀에 들어갈 수 있어요",
        "resumed" => "팀을 다시 켰어요",
        "hidden" => "이 단계 축하 카드를 홈에서 숨겨요",
        "shown" => "축하 카드를 다시 보여요",
        _ => "",
    };

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await LoadAsync(ct)) return NotFound();
        CreateOpen = New is not null;
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(string? name, List<int> pick, CancellationToken ct)
    {
        var (e, id) = await teams.CreateAsync(name, pick, ct);
        if (e == FamilyError.None) return Done(id, "created");
        return await Fail(null, e, ct, create: true);
    }

    /// <summary>이름 · 메모 · 관계 저장. 관계는 rel_{구성원 Id} 칸으로 온다.</summary>
    public async Task<IActionResult> OnPostSaveAsync(int id, string? name, string? memo, CancellationToken ct)
    {
        var relations = Request.Form.Keys.Where(k => k.StartsWith("rel_", StringComparison.Ordinal))
            .Select(k => (Ok: int.TryParse(k[4..], out var m), M: m, R: Request.Form[k].ToString()))
            .Where(x => x.Ok).ToDictionary(x => x.M, x => x.R);
        return await Act(id, "saved", teams.SaveAsync(id, name, memo, relations, ct), ct);
    }

    public Task<IActionResult> OnPostAddAsync(int id, int participant, CancellationToken ct) => Act(id, "added", teams.AddMemberAsync(id, participant, ct), ct);
    public Task<IActionResult> OnPostRemoveAsync(int id, int member, CancellationToken ct) => Act(id, "removed", teams.RemoveMemberAsync(id, member, ct), ct);
    public Task<IActionResult> OnPostMoveAsync(int id, int member, bool up, CancellationToken ct) => Act(id, null, teams.MoveAsync(id, member, up, ct), ct);
    public Task<IActionResult> OnPostStopAsync(int id, CancellationToken ct) => Act(id, "stopped", teams.SetActiveAsync(id, false, ct), ct);
    public Task<IActionResult> OnPostResumeAsync(int id, CancellationToken ct) => Act(id, "resumed", teams.SetActiveAsync(id, true, ct), ct);
    public Task<IActionResult> OnPostStageAsync(int id, int threshold, bool hide, CancellationToken ct) =>
        Act(id, hide ? "hidden" : "shown", teams.SetStageHiddenAsync(Me, id, threshold, hide, ct), ct);

    private async Task<IActionResult> Act(int id, string? ok, Task<FamilyError> work, CancellationToken ct)
    {
        var e = await work;
        return e == FamilyError.None ? Done(id, ok) : await Fail(id, e, ct);
    }

    private IActionResult Done(int? id, string? ok) => RedirectToPage(new { slug = tenant.Current.Slug, Open = id, Q, Ok = ok });

    private async Task<IActionResult> Fail(int? id, FamilyError e, CancellationToken ct, bool create = false)
    {
        if (!await LoadAsync(ct)) return NotFound();
        Error = FamilyTeamService.Message(e);
        ErrorFor = id;
        Open = id;
        CreateOpen = create;
        return Page();
    }

    /// <summary>이 부서 사람의 계정 Id → 그 사람이 든 온가족(R-178, 다른 부서 구성원은 이름을 가림). 팀 카드 안에 총점 한 줄.</summary>
    public Dictionary<int, FamilyBoardView> OnFamilies { get; private set; } = [];

    /// <summary>이 팀 구성원이 든 온가족들(겹치면 하나로).</summary>
    public IEnumerable<FamilyBoardView> LinkedFamiliesOf(FamilyTeamView t) =>
        t.Members.Select(m => OnFamilies.GetValueOrDefault(m.AccountId)).OfType<FamilyBoardView>().DistinctBy(f => f.Id);

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        Board = await teams.GetAsync(ct);
        if (Board.Enabled) OnFamilies = await onFamily.ForTenantAsync(Me, tenant.Current.Id, ct);
        return Board.Enabled;
    }
}
