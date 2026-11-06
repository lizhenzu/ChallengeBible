using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Mission;

/// <summary>
/// `/{slug}/family-team` 가족이 스스로 꾸리는 가족팀(확정 시안: family-team-self, ADR 0083, R-204 ~ R-210). 이 부서 승인 소속원.
/// 팀이 있으면 "우리 가족팀", 없으면 찾기 · 새 팀(`?New=true`) · 합류 신청(`?Join=팀`). 기본 규칙 스위치가 꺼졌거나 임시 부서면 404. JS 없이 폼마다 서버가 다시 그린다.
/// </summary>
public class FamilyTeamModel(FamilyJoinService join, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "우리 가족팀";
    public string Slug => tenant.Current.Slug;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool New { get; set; }
    [BindProperty(SupportsGet = true)] public int? Join { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    public FamilyState State { get; private set; } = null!;
    public IReadOnlyList<FamilySearchHit> Hits { get; private set; } = [];
    public FamilySearchHit? JoinTeam { get; private set; }
    public string? Error { get; private set; }
    public string? NameInput { get; private set; }

    /// <summary>처음 고를 역할: 보호자면 엄마, 아니면 학생.</summary>
    public string DefaultRelation => State.IsGuardian ? FamilyRelation.Mother : FamilyRelation.Student;

    public string OkMessage => Ok switch
    {
        "created" => "가족팀을 만들었어요. 가족에게 팀 이름을 알려 주세요",
        "requested" => "합류를 신청했어요",
        "cancelled" => "신청을 취소했어요",
        "accepted" => "가족이 합류했어요",
        "rejected" => "신청을 거절했어요",
        "left" => "가족팀에서 나왔어요. 내 점수는 그대로예요",
        "kicked" => "팀에서 내보냈어요. 그 사람의 점수는 그대로예요",
        "renamed" => "팀 이름을 바꿨어요",
        "relation" => "내 역할을 바꿨어요",
        _ => "",
    };

    public static string Emoji(string relation) => relation switch
    {
        FamilyRelation.Student => "🧒", FamilyRelation.Mother => "👩", FamilyRelation.Father => "👨",
        FamilyRelation.Grandmother => "👵", FamilyRelation.Grandfather => "👴", _ => "🙂",
    };

    /// <summary>"학생으로" · "아빠로"</summary>
    public static string AsRelation(string relation) =>
        FamilyTeamService.RelationLabel(relation) + (relation == FamilyRelation.Student ? "으로" : "로");

    /// <summary>찾은 글자에 표시를 달기 위해 앞 · 찾은 글자 · 뒤로 나눈다. 없으면 전체가 앞.</summary>
    public (string Before, string Hit, string After) Split(string text)
    {
        var q = Q?.Trim();
        var i = string.IsNullOrEmpty(q) ? -1 : text.IndexOf(q, StringComparison.OrdinalIgnoreCase);
        return i < 0 ? (text, "", "") : (text[..i], text.Substring(i, q!.Length), text[(i + q!.Length)..]);
    }

    public Task<IActionResult> OnGetAsync(CancellationToken ct) => ShowAsync(ct);

    public Task<IActionResult> OnPostCreateAsync(string? name, string? relation, CancellationToken ct)
    {
        New = true;
        NameInput = name;
        return Act(join.CreateAsync(name, relation ?? "", ct), "created", ct);
    }

    public Task<IActionResult> OnPostRequestAsync(int team, string? relation, CancellationToken ct)
    {
        Join = team;
        return Act(join.RequestAsync(team, relation ?? "", ct), "requested", ct);
    }

    public Task<IActionResult> OnPostCancelAsync(CancellationToken ct) => Act(join.CancelAsync(ct), "cancelled", ct);
    public Task<IActionResult> OnPostAcceptAsync(int request, CancellationToken ct) => Act(join.AcceptAsync(request, ct), "accepted", ct);
    public Task<IActionResult> OnPostRejectAsync(int request, CancellationToken ct) => Act(join.RejectAsync(request, ct), "rejected", ct);
    public Task<IActionResult> OnPostLeaveAsync(CancellationToken ct) => Act(join.LeaveAsync(ct), "left", ct);
    public Task<IActionResult> OnPostKickAsync(int member, CancellationToken ct) => Act(join.KickAsync(member, ct), "kicked", ct);
    public Task<IActionResult> OnPostRenameAsync(string? name, CancellationToken ct) => Act(join.RenameAsync(name, ct), "renamed", ct);
    public Task<IActionResult> OnPostRelationAsync(string? relation, CancellationToken ct) => Act(join.SetRelationAsync(relation ?? "", ct), "relation", ct);

    private async Task<IActionResult> Act(Task<FamilyError> work, string ok, CancellationToken ct)
    {
        var e = await work;
        if (e == FamilyError.Disabled) return NotFound();
        if (e == FamilyError.None) return RedirectToPage(new { slug = Slug, Ok = ok });
        Error = FamilyTeamService.Message(e);
        return await ShowAsync(ct);
    }

    private async Task<IActionResult> ShowAsync(CancellationToken ct)
    {
        State = await join.StateAsync(ct);
        if (!State.Enabled) return NotFound();
        if (State.Mine is null)
        {
            Hits = await join.SearchAsync(Q, ct);
            if (Join is int id) JoinTeam = await join.TeamAsync(id, ct);
        }
        return Page();
    }
}
