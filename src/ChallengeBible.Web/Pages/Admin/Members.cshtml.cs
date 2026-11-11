using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/members` 소속 관리(확정 시안: 관리-소속관리). 영역 관리자만.</summary>
[TenantRoles(Roles.TenantAdmin, Roles.Teacher)]
public class MembersModel(MemberAdminService members, PeopleService people, PromotionService promotions, LookupLimiter limiter, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "소속 관리";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = MembershipStatus.Requested;
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Role { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    public List<MemberRow> Rows { get; private set; } = [];
    public Dictionary<string, int> Counts { get; private set; } = [];
    public List<(string Code, string Name)> RoleCodes { get; private set; } = [];
    public List<(string UserName, string Name)> Guardians { get; private set; } = [];
    public Dictionary<string, List<string>> Links { get; private set; } = [];
    public string? Error { get; private set; }
    public int? ErrorFor { get; private set; }
    public string Me => User.UserName()!;

    // 진급 보내기(시안: 여러 명 골라 → 받을 부서 코드 → 이름 확인 → 보내기)
    public HashSet<int> Picks { get; private set; } = [];
    public string? PromoCode { get; private set; }
    public PromotionTarget? PromoTarget { get; private set; }
    public string? PromoError { get; private set; }
    public List<string> PromoSkipped { get; private set; } = [];

    // 명부(0093): 부서 관리자 · 교사. 교사는 명부 탭만, 사람 올리기 · 생일 · 연락처 고치기만 한다(R-226 · R-227)
    public List<RosterRow> Roster { get; private set; } = [];
    public Dictionary<string, List<PersonCandidate>> Candidates { get; private set; } = [];
    /// <summary>명부를 열 수 있는지: 부서 관리자, 또는 역할이 교사(임원은 아니다).</summary>
    public bool CanRoster => tenant.IsAdmin || (tenant.IsApproved && UserRole.KindOf(tenant.RoleCode) == UserRoleKind.Teacher);
    public bool IsAdmin => tenant.IsAdmin;
    public string? AddOpen { get; private set; }

    public static readonly (string Key, string Label)[] Tabs =
        [(MembershipStatus.Requested, "참여 신청"), (MembershipStatus.Approved, "명부")];
    public IEnumerable<(string Key, string Label)> MyTabs => IsAdmin ? Tabs : Tabs.Where(t => t.Key == MembershipStatus.Approved);

    public static string RoleLabel(string r) => r switch
    {
        Roles.Participant => "참여자", Roles.Guardian => "보호자", Roles.Teacher => "교사", Roles.TenantAdmin => "관리자", _ => r,
    };

    public string OkMessage => Ok switch
    {
        "approve" => "승인했어요 · 바로 미션을 할 수 있어요",
        "save" => "저장했어요",
        "reject" => "신청을 거절했어요",
        "admin" => "관리자 지정을 바꿨어요",
        "link" => "보호자를 연결했어요 · 이제 자녀 기록을 볼 수 있어요",
        "unlink" => "보호자 연결을 풀었어요",
        "padd" => "명부에 올렸어요",
        "psave" => "저장했어요",
        "pdel" => "명부에서 지웠어요",
        "punlink" => "연결을 풀었어요 · 이 계정은 새 명부 줄을 받았어요",
        _ => "",
    };

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!CanRoster) return Forbid();
        if (!IsAdmin) Tab = MembershipStatus.Approved;
        await LoadAsync(ct);
        return Page();
    }

    // 권한은 일을 시작하기 전에 본다: 관리자만 / 관리자 · 교사(명부)
    private Task<IActionResult> Admin(Func<Task<IActionResult>> work) => IsAdmin ? work() : Task.FromResult<IActionResult>(Forbid());
    private Task<IActionResult> Staff(Func<Task<IActionResult>> work) => CanRoster ? work() : Task.FromResult<IActionResult>(Forbid());

    /// <summary>승인 + 명부 연결(R-228). person: 고른 명부 줄 번호, 0 이면 새 사람.</summary>
    public Task<IActionResult> OnPostApproveAsync(int id, string? roleCode, int? person, CancellationToken ct) =>
        Admin(() => Act(id, "approve", members.ApproveAsync(Me, id, roleCode, person is > 0 ? person : null, ct), ct));

    public async Task<IActionResult> OnPostAddPersonAsync(string? name, string? roleCode, int? month, int? day, string? phone1, string? phone2, CancellationToken ct)
    {
        if (!CanRoster) return Forbid();
        Tab = MembershipStatus.Approved;
        var (e, _) = await people.AddAsync(Me, new PersonInput(name, roleCode, month, day, phone1, phone2), ct);
        if (e == PeopleError.None) return RedirectToPage(new { slug = tenant.Current.Slug, Tab, Ok = "padd" });
        Error = PeopleService.Message(e);
        AddOpen = "1";
        await LoadAsync(ct);
        return Page();
    }

    public Task<IActionResult> OnPostPersonAsync(int pid, string? name, string? roleCode, int? month, int? day, string? phone1, string? phone2, CancellationToken ct) =>
        Staff(() => PersonAct(pid, "psave", people.UpdateAsync(pid, new PersonInput(name, roleCode, month, day, phone1, phone2), ct), ct));

    public Task<IActionResult> OnPostDeletePersonAsync(int pid, CancellationToken ct) => Admin(() => PersonAct(pid, "pdel", people.DeleteAsync(pid, ct), ct));

    public Task<IActionResult> OnPostUnlinkPersonAsync(int pid, CancellationToken ct) => Admin(() => PersonAct(pid, "punlink", people.UnlinkAsync(Me, pid, ct), ct));

    private async Task<IActionResult> PersonAct(int pid, string ok, Task<PeopleError> work, CancellationToken ct)
    {
        Tab = MembershipStatus.Approved;
        var e = await work;
        if (e == PeopleError.None) return RedirectToPage(new { slug = tenant.Current.Slug, Tab, Q, Role, Ok = ok });
        Error = PeopleService.Message(e);
        PersonErrorFor = pid;
        await LoadAsync(ct);
        return Page();
    }

    public int? PersonErrorFor { get; private set; }

    public Task<IActionResult> OnPostSaveAsync(int id, string? roleCode, CancellationToken ct) =>
        Admin(() => Act(id, "save", members.ChangeRoleAsync(id, roleCode, ct), ct));

    public Task<IActionResult> OnPostAdminAsync(int id, bool admin, CancellationToken ct) =>
        Admin(() => Act(id, "admin", members.SetAdminAsync(Me, id, admin, ct), ct));

    public Task<IActionResult> OnPostLinkAsync(int id, string guardian, string student, CancellationToken ct) =>
        Admin(() => Act(id, "link", members.LinkGuardianAsync(Me, guardian, student, ct), ct));

    public Task<IActionResult> OnPostUnlinkAsync(int id, string guardian, string student, CancellationToken ct) =>
        Admin(() => Act(id, "unlink", members.UnlinkGuardianAsync(guardian, student, ct), ct));

    public Task<IActionResult> OnPostRejectAsync(int id, CancellationToken ct) =>
        Admin(() => Act(id, "reject", members.RejectAsync(Me, id, ct), ct));

    /// <summary>받을 부서 이름 확인(이름만, 0026). 참여 코드 찾기와 같은 횟수 제한(0048).</summary>
    public async Task<IActionResult> OnPostPromoteCheckAsync(List<int> pick, string? code, CancellationToken ct)
    {
        if (!IsAdmin) return Forbid();
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
        if (!IsAdmin) return Forbid();
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
            return RedirectToPage(new { slug = tenant.Current.Slug, Tab, Q, Role, Ok = ok });
        Error = MemberAdminService.Message(e);
        ErrorFor = id;
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (!Tabs.Any(t => t.Key == Tab)) Tab = MembershipStatus.Requested;
        if (!IsAdmin) Tab = MembershipStatus.Approved;
        Rows = Tab == MembershipStatus.Approved ? [] : await members.ListAsync(new MemberFilter(Tab, Q, Role), ct);
        if (Tab == MembershipStatus.Requested) Candidates = await people.CandidatesAsync(Rows.Select(r => r.DisplayName), ct);
        Counts = await members.CountsAsync(ct);
        RoleCodes = await members.RoleCodesAsync(ct);
        Counts[MembershipStatus.Approved] = await people.CountAsync(ct);
        if (Tab == MembershipStatus.Approved)
        {
            Roster = await people.RosterAsync(Q, Role, ct);
            Guardians = await members.GuardiansAsync(ct);
            Links = await members.LinksAsync(ct);
        }
    }
}
