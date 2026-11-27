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
public class FamilyTeamModel(FamilyJoinService join, TenantContext tenant, TeamGiftService teamGifts, FamilyTeamService teams, TeamCheerService cheers) : PageModel, ITitled
{
    /// <summary>팀 선물 카드(확정 시안 가족팀-선물고르기 ①, 가족팀 화면 위).</summary>
    public IReadOnlyList<TeamGiftView> TeamGifts { get; private set; } = [];
    public string Title => View == "others" ? "다른 팀 랭킹" : "팀보기";
    /// <summary>주소의 뒤쪽: manage = 팀 관리(예전 화면: 방장 넘기기 · 내보내기 · 가입 승인 · 이름 바꾸기 · 나가기), others = 다른 팀 랭킹. 없으면 팀이 있을 때 팀보기.</summary>
    [FromRoute(Name = "view")] public string? View { get; set; }
    public string Slug => tenant.Current.Slug;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool New { get; set; }
    [BindProperty(SupportsGet = true)] public int? Join { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }
    [BindProperty(SupportsGet = true)] public bool Add { get; set; }
    /// <summary>찾기 · 만들기에서 고른 팀 종류: "friend" 면 친구팀(0093).</summary>
    [BindProperty(SupportsGet = true)] public string? Kind { get; set; }
    public bool Friend => Kind == "friend";

    // ── 팀보기(2026-11-27 민트 시안 TeamB · TeamOthers) ──
    public bool ShowMine => State.Mine is not null && View is null && !Add && !New && Join is null && Board is not null;
    public bool ShowOthers => View == "others" && Board is not null;
    public TeamBoard? Board { get; private set; }
    /// <summary>오늘 내가 응원을 보낸 팀원(계정).</summary>
    public IReadOnlySet<string> CheeredToday { get; private set; } = new HashSet<string>();
    /// <summary>우리 팀 선물함: 받은 선물.</summary>
    public IReadOnlyList<TeamReceived> Received { get; private set; } = [];
    /// <summary>다른 팀 랭킹 정렬: s = score(기본) · date · name, o = asc · desc.</summary>
    [BindProperty(SupportsGet = true, Name = "s")] public string Sort { get; set; } = "score";
    [BindProperty(SupportsGet = true, Name = "o")] public string? Order { get; set; }
    public string SortKey => Sort is "date" or "name" ? Sort : "score";
    /// <summary>점수 · 수행날짜는 기본 내림(높은 · 최근 먼저), 팀명은 기본 오름(가나다).</summary>
    public bool Asc => Order is "asc" or "desc" ? Order == "asc" : SortKey == "name";
    public FamilyRanking? Ranking { get; private set; }
    public IReadOnlyList<FamilyRankTeam> Others { get; private set; } = [];
    public FamilyRankTeam? MyRank { get; private set; }
    public static string SortName(string key) => key switch { "date" => "수행날짜순", "name" => "팀명순", _ => "점수순" };
    public string SortUrl(string key)
    {
        var asc = key == SortKey ? !Asc : key == "name";
        var parts = new List<string>();
        if (key != "score") parts.Add($"s={key}");
        if (asc != (key == "name")) parts.Add($"o={(asc ? "asc" : "desc")}");
        return $"/{Slug}/family-team/others" + (parts.Count == 0 ? "" : "?" + string.Join("&", parts));
    }
    /// <summary>선물 날짜(부서 시간대) 「11월 25일」.</summary>
    public string When(DateTime? utc) => utc is { } u
        ? ChallengeBible.Domain.Time.TenantClock.ToTenantDate(new DateTimeOffset(DateTime.SpecifyKind(u, DateTimeKind.Utc)), tenant.Current.TimeZoneId).ToString("M월 d일") : "";
    public TenantContext Tenant => tenant;
    public static string LastText(DateOnly? last, DateOnly today) => last is not { } d ? "–" : d == today ? "오늘" : d == today.AddDays(-1) ? "어제" : d.ToString("M월 d일");

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
        "cheered" => "응원을 보냈어요",
        "cheerdone" => "그 팀원은 오늘 이미 미션을 했어요",
        "cheersent" => "오늘은 이미 응원을 보냈어요",
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

    /// <summary>응원하기: 오늘 아직 안 한 팀원에게 쪽지 한 통(하루 한 번).</summary>
    public async Task<IActionResult> OnPostCheerAsync(string? target, CancellationToken ct)
    {
        var r = await cheers.SendAsync(target, ct);
        if (r == CheerResult.NoTeam) return NotFound();
        var ok = r switch { CheerResult.Ok => "cheered", CheerResult.AlreadyDone => "cheerdone", CheerResult.AlreadySent => "cheersent", _ => "cheerdone" };
        return RedirectToPage(new { slug = Slug, Ok = ok });
    }

    private async Task<IActionResult> Act(Task<FamilyError> work, string ok, CancellationToken ct)
    {
        var e = await work;
        if (e == FamilyError.Disabled) return NotFound();
        if (e == FamilyError.None)
        {
            var rv = new Microsoft.AspNetCore.Routing.RouteValueDictionary { ["slug"] = Slug, ["Ok"] = ok };
            if (View == "manage") rv["view"] = "manage";   // 팀 관리 화면에서 한 일은 그 화면으로 돌아간다
            if (State is null && Friend) rv["Kind"] = Kind;
            return RedirectToPage(null, rv);
        }
        Error = FamilyTeamService.Message(e);
        return await ShowAsync(ct);
    }

    private async Task<IActionResult> ShowAsync(CancellationToken ct)
    {
        State = await join.StateAsync(ct);
        await teamGifts.EnsureAsync(ct);
        TeamGifts = await teamGifts.MineAsync(ct);
        if (!State.Enabled) return NotFound();
        if (State.Mine is not null && HttpMethods.IsGet(Request.Method)) Notice = await join.TakeNoticeAsync(ct);
        if (State.Mine is null)
        {
            if (Friend && !State.IsStudent) Kind = null;   // 친구팀은 학생만
            Hits = await join.SearchAsync(Q, Friend, ct);
            if (Join is int id) JoinTeam = await join.TeamAsync(id, ct);
        }
        else if (Add) Candidates = await join.CandidatesAsync(Q, ct);
        if (State.Mine is not null && (View is null || View == "others") && !Add && !New && Join is null)
        {
            Board = await teams.BoardAsync(ct);
            if (Board is not null)
            {
                CheeredToday = await cheers.SentTodayAsync(ct);
                Received = await teamGifts.ReceivedAsync(ct);
                if (View == "others")
                {
                    Ranking = await teams.RankingAsync(ct);
                    MyRank = Ranking.Teams.FirstOrDefault(x => x.Mine);
                    var ko = StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false);
                    var list = Ranking.Teams.Where(x => !x.Mine);
                    Others = SortKey switch
                    {
                        "date" => (Asc ? list.OrderBy(x => x.Last ?? DateOnly.MinValue) : list.OrderByDescending(x => x.Last ?? DateOnly.MinValue)).ThenBy(x => x.Name, ko).ToList(),
                        "name" => (Asc ? list.OrderBy(x => x.Name, ko) : list.OrderByDescending(x => x.Name, ko)).ToList(),
                        _ => (Asc ? list.OrderBy(x => x.Points) : list.OrderByDescending(x => x.Points)).ThenBy(x => x.Name, ko).ToList(),
                    };
                }
            }
        }
        if (View == "others" && Board is null) return Redirect($"/{Slug}/family-team");
        return Page();
    }
}
