using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/members` 소속 관리(확정 시안: 관리-소속관리). 영역 관리자만.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class MembersModel(MemberAdminService members, PromotionService promotions, LookupLimiter limiter, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "소속 관리";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = MembershipStatus.Requested;
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Role { get; set; }
    [BindProperty(SupportsGet = true)] public string? Group { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    public List<MemberRow> Rows { get; private set; } = [];
    public Dictionary<string, int> Counts { get; private set; } = [];
    public List<string> Groups { get; private set; } = [];
    public List<(int MembershipId, string Name)> Guardians { get; private set; } = [];
    public Dictionary<int, List<int>> Links { get; private set; } = [];
    public string? Error { get; private set; }
    public int? ErrorFor { get; private set; }
    public int Me => User.AccountId()!.Value;

    // 진급 보내기(시안: 여러 명 골라 → 받을 부서 코드 → 이름 확인 → 보내기)
    public HashSet<int> Picks { get; private set; } = [];
    public string? PromoCode { get; private set; }
    public PromotionTarget? PromoTarget { get; private set; }
    public string? PromoError { get; private set; }
    public List<string> PromoSkipped { get; private set; } = [];

    public static readonly (string Key, string Label)[] Tabs =
        [(MembershipStatus.Requested, "참여 신청"), (MembershipStatus.Approved, "소속"), (MembershipStatus.Suspended, "정지")];

    public static string RoleLabel(string r) => r switch
    {
        Roles.Participant => "참여자", Roles.Guardian => "보호자", Roles.Teacher => "교사", Roles.TenantAdmin => "관리자", _ => r,
    };

    public string OkMessage => Ok switch
    {
        "approve" => "승인했어요 · 바로 미션을 할 수 있어요",
        "save" => "저장했어요",
        "reject" => "신청을 거절했어요",
        "suspend" => "정지했어요",
        "resume" => "정지를 풀었어요",
        "link" => "보호자를 연결했어요 · 이제 자녀 기록을 볼 수 있어요",
        "unlink" => "보호자 연결을 풀었어요",
        "regroup" => $"{Moved}명을 {(string.IsNullOrWhiteSpace(To) ? "반 없음" : To)}(으)로 옮겼어요",
        _ => "",
    };

    [BindProperty(SupportsGet = true)] public int Moved { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }
    public string? RegroupError { get; private set; }

    /// <summary>위 목록에서 고른 참여자들의 반을 한 번에 바꾼다(확정 시안: 관리-소속관리 "반 바꾸기").</summary>
    public async Task<IActionResult> OnPostRegroupAsync(List<int> pick, string? newGroup, CancellationToken ct)
    {
        Tab = MembershipStatus.Approved;
        if (pick.Count == 0)
        {
            RegroupError = "반을 바꿀 아이를 위에서 골라 주세요.";
            NewGroup = newGroup;
            await LoadAsync(ct);
            return Page();
        }
        var moved = await members.RegroupAsync(pick, newGroup, ct);
        return RedirectToPage(new { slug = tenant.Current.Slug, Tab, Q, Role, Group, Ok = "regroup", Moved = moved, To = newGroup?.Trim() });
    }

    public string? NewGroup { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public Task<IActionResult> OnPostApproveAsync(int id, List<string> roles, string? group, CancellationToken ct) =>
        Act(id, "approve", members.ApproveAsync(Me, id, roles, group, ct), ct);

    public Task<IActionResult> OnPostSaveAsync(int id, List<string> roles, string? group, CancellationToken ct) =>
        Act(id, "save", members.UpdateAsync(Me, id, roles, group, ct), ct);

    public Task<IActionResult> OnPostLinkAsync(int id, int guardian, CancellationToken ct) =>
        Act(id, "link", members.LinkGuardianAsync(Me, guardian, id, ct), ct);

    public Task<IActionResult> OnPostUnlinkAsync(int id, int guardian, CancellationToken ct) =>
        Act(id, "unlink", members.UnlinkGuardianAsync(guardian, id, ct), ct);

    public Task<IActionResult> OnPostRejectAsync(int id, CancellationToken ct) => Act(id, "reject", members.RejectAsync(Me, id, ct), ct);
    public Task<IActionResult> OnPostSuspendAsync(int id, CancellationToken ct) => Act(id, "suspend", members.SuspendAsync(Me, id, ct), ct);
    public Task<IActionResult> OnPostResumeAsync(int id, CancellationToken ct) => Act(id, "resume", members.ResumeAsync(Me, id, ct), ct);

    /// <summary>받을 부서 이름 확인(이름만, 0026). 참여 코드 찾기와 같은 횟수 제한(0048).</summary>
    public async Task<IActionResult> OnPostPromoteCheckAsync(List<int> pick, string? code, CancellationToken ct)
    {
        Tab = MembershipStatus.Approved;
        Picks = [.. pick];
        PromoCode = JoinService.NormalizeCode(code ?? "");
        if (Picks.Count == 0) PromoError = "보낼 아이를 위에서 골라 주세요.";
        else if (!limiter.TryTake(Me)) PromoError = PromotionService.Message(PromotionError.TooMany);
        else
        {
            PromoTarget = await promotions.LookupAsync(PromoCode, ct);
            if (PromoTarget is null) PromoError = PromotionService.Message(PromotionError.TargetNotFound);
            else if (PromoTarget.TenantId == tenant.Current.Id) { PromoError = PromotionService.Message(PromotionError.SameTenant); PromoTarget = null; }
        }
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostPromoteAsync(List<int> pick, string? code, CancellationToken ct)
    {
        Tab = MembershipStatus.Approved;
        var (e, sent, skipped) = await promotions.SendAsync(Me, pick, code ?? "", ct);
        if (e == PromotionError.None && skipped.Count == 0)
            return RedirectToPage("Promotions", new { slug = tenant.Current.Slug, Tab = "out", Ok = "sent" });
        Picks = [.. pick];
        PromoCode = JoinService.NormalizeCode(code ?? "");
        if (e == PromotionError.None) { Ok = null; PromoSkipped = skipped; PromoError = $"{sent}명은 보냈어요. 이미 진행 중인 요청이 있어 건너뛴 아이: {string.Join(", ", skipped)}"; Picks = []; }
        else PromoError = PromotionService.Message(e);
        await LoadAsync(ct);
        return Page();
    }

    private async Task<IActionResult> Act(int id, string ok, Task<MemberError> work, CancellationToken ct)
    {
        var e = await work;
        if (e == MemberError.None)
            return RedirectToPage(new { slug = tenant.Current.Slug, Tab, Q, Role, Group, Ok = ok });
        Error = MemberAdminService.Message(e);
        ErrorFor = id;
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (!Tabs.Any(t => t.Key == Tab)) Tab = MembershipStatus.Requested;
        Rows = await members.ListAsync(new MemberFilter(Tab, Q, Role, Group), ct);
        Counts = await members.CountsAsync(ct);
        Groups = await members.GroupsAsync(ct);
        if (Tab == MembershipStatus.Approved)
        {
            Guardians = await members.GuardiansAsync(ct);
            Links = await members.LinksAsync(ct);
        }
    }
}
