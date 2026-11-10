using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Mission;

/// <summary>
/// `/{slug}/family-team` 가족이 스스로 꾸리는 가족팀(확정 시안: family-team-self, ADR 0083, R-204 ~ R-210). 이 부서 승인 소속원.
/// 팀이 있으면 "우리 가족팀" · 가족 초대(`?Add=true`, 시안 family-invite, ADR 0084), 없으면 받은 초대 · 찾기 · 새 팀(`?New=true`) · 합류 신청(`?Join=팀`). 기본 규칙 스위치가 꺼졌거나 임시 부서면 404. JS 없이 폼마다 서버가 다시 그린다.
/// </summary>
public class FamilyTeamModel(FamilyJoinService join, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "우리 가족팀";
    public string Slug => tenant.Current.Slug;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool New { get; set; }
    [BindProperty(SupportsGet = true)] public int? Join { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }
    [BindProperty(SupportsGet = true)] public bool Add { get; set; }
    /// <summary>찾기 · 만들기에서 고른 팀 종류: "friend" 면 친구팀(0093).</summary>
    [BindProperty(SupportsGet = true)] public string? Kind { get; set; }
    public bool Friend => Kind == "friend";

    public FamilyState State { get; private set; } = null!;
    /// <summary>들어감 알림(R-224): 이번에 한 번 보이고 지워진다.</summary>
    public string? Notice { get; private set; }
    public IReadOnlyList<FamilySearchHit> Hits { get; private set; } = [];
    public IReadOnlyList<FamilyCandidate> Candidates { get; private set; } = [];
    public FamilySearchHit? JoinTeam { get; private set; }
    public string? Error { get; private set; }
    public string? NameInput { get; private set; }

    /// <summary>처음 고를 역할: 보호자면 엄마, 아니면 학생.</summary>
    public string DefaultRelation => State.IsGuardian ? FamilyRelation.Mother : FamilyRelation.Student;

    public string OkMessage => OkText(Ok);

    public static string OkText(string? ok) => ok switch
    {
        "leftgone" => "팀에서 나왔어요. 마지막 구성원이라 팀이 삭제됐어요",
        "created" => "가족팀을 만들었어요. 가족에게 팀 이름을 알려 주세요",
        "createdfriend" => "친구팀을 만들었어요. 친구에게 팀 이름을 알려 주세요",
        "transferred" => "방장을 넘겼어요",
        "requested" => "합류를 신청했어요",
        "cancelled" => "신청을 취소했어요",
        "accepted" => "가족이 합류했어요",
        "rejected" => "신청을 거절했어요",
        "left" => "팀에서 나왔어요. 내 점수는 그대로예요",
        "kicked" => "팀에서 내보냈어요. 그 사람의 점수는 그대로예요",
        "renamed" => "팀 이름을 바꿨어요",
        "relation" => "관계를 설정했어요",
        "invited" => "초대를 보냈어요. 상대가 수락하면 합류돼요",
        "invitecancelled" => "초대를 취소했어요",
        "inviteaccepted" => "팀에 합류했어요",
        "inviterejected" => "초대를 거절했어요",
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
        return Act(join.CreateAsync(name, relation ?? "", Friend, ct), Friend ? "createdfriend" : "created", ct);
    }

    public Task<IActionResult> OnPostTransferAsync(int member, CancellationToken ct) => Act(join.TransferAsync(member, ct), "transferred", ct);

    public Task<IActionResult> OnPostRequestAsync(int team, string? relation, CancellationToken ct)
    {
        Join = team;
        return Act(join.RequestAsync(team, relation ?? "", ct), "requested", ct);
    }

    public Task<IActionResult> OnPostCancelAsync(CancellationToken ct) => Act(join.CancelAsync(ct), "cancelled", ct);
    public Task<IActionResult> OnPostAcceptAsync(int request, CancellationToken ct) => Act(join.AcceptAsync(request, ct), "accepted", ct);
    public Task<IActionResult> OnPostRejectAsync(int request, CancellationToken ct) => Act(join.RejectAsync(request, ct), "rejected", ct);
    public async Task<IActionResult> OnPostLeaveAsync(CancellationToken ct)
    {
        var (error, teamGone) = await join.LeaveReportAsync(ct);
        return await Act(Task.FromResult(error), teamGone ? "leftgone" : "left", ct);
    }

    public Task<IActionResult> OnPostKickAsync(int member, CancellationToken ct) => Act(join.KickAsync(member, ct), "kicked", ct);
    public Task<IActionResult> OnPostRenameAsync(string? name, CancellationToken ct) => Act(join.RenameAsync(name, ct), "renamed", ct);
    public Task<IActionResult> OnPostRelationAsync(string? relation, CancellationToken ct) => Act(join.SetRelationAsync(relation ?? "", ct), "relation", ct);

    public Task<IActionResult> OnPostInviteAsync(string participant, string? relation, CancellationToken ct)
    {
        Add = true;
        return Act(join.InviteAsync(participant, relation ?? "", ct), "invited", ct);
    }

    public Task<IActionResult> OnPostInviteCancelAsync(int invite, CancellationToken ct) => Act(join.CancelInviteAsync(invite, ct), "invitecancelled", ct);
    public Task<IActionResult> OnPostInviteAcceptAsync(int invite, string? relation, CancellationToken ct) => Act(join.AcceptInviteAsync(invite, relation ?? "", ct), "inviteaccepted", ct);
    public Task<IActionResult> OnPostInviteRejectAsync(int invite, CancellationToken ct) => Act(join.RejectInviteAsync(invite, ct), "inviterejected", ct);

    private async Task<IActionResult> Act(Task<FamilyError> work, string ok, CancellationToken ct)
    {
        var e = await work;
        if (e == FamilyError.Disabled) return NotFound();
        if (e == FamilyError.None) return RedirectToPage(new { slug = Slug, Ok = ok, Kind = State is null && Friend ? Kind : null });
        Error = FamilyTeamService.Message(e);
        return await ShowAsync(ct);
    }

    private async Task<IActionResult> ShowAsync(CancellationToken ct)
    {
        State = await join.StateAsync(ct);
        if (!State.Enabled) return NotFound();
        if (State.Mine is not null && HttpMethods.IsGet(Request.Method)) Notice = await join.TakeNoticeAsync(ct);
        if (State.Mine is null)
        {
            if (Friend && !State.IsStudent) Kind = null;   // 친구팀은 학생만
            Hits = await join.SearchAsync(Q, Friend, ct);
            if (Join is int id) JoinTeam = await join.TeamAsync(id, ct);
        }
        else if (Add) Candidates = await join.CandidatesAsync(Q, ct);
        return Page();
    }
}
