using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/members` 소속 관리(확정 시안: 관리-소속관리). 영역 관리자만.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class MembersModel(MemberAdminService members, TenantContext tenant) : PageModel, ITitled
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
        _ => "",
    };

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
