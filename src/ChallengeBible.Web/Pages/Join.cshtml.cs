using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages;

/// <summary>
/// `/join` 참여 신청(0045, 0048, 0059 — 확정 시안: 부서참여신청-v3). 교회별로 묶은 부서 칸을 골라 신청 → 승인 기다림.
/// 찾기 칸 · 코드 칸 · 역할 고르기는 없다(2026-10-25 사용자): 신청은 참여자로 받고, 역할은 관리자가 승인 때 정한다.
/// 목록에 안 보이는 부서는 관리자가 준 초대 주소(`/join?code=…`)로 들어오면 그 부서 칸이 보인다.
/// </summary>
public class JoinModel(JoinService join, AccountService accounts, LookupLimiter limiter, MessageService msgs) : PageModel, ITitled
{
    public string Title => "부서 참여 신청";

    [BindProperty(SupportsGet = true)] public string Code { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty] public int TenantId { get; set; }
    /// <summary>신청할 역할: S 학생 · T 교사 · P 학부모 · E 기타(유년부와 같다). 관리자가 승인할 때 바꿀 수 있다.</summary>
    [BindProperty] public string RoleKind { get; set; } = SignupRoleKind.Student;
    [BindProperty(SupportsGet = true)] public int Welcome { get; set; }
    /// <summary>생일(선택, 확정 시안 명부계정연결 ③): 승인 때 명부 후보에서 같은 생일을 맨 위로. 0 = 안 씀.</summary>

    /// <summary>목록(이름 거르기 적용) + 코드로 찾은 부서가 목록에 없으면 맨 앞에 더한다.</summary>
    public List<JoinTarget> Depts { get; private set; } = [];
    public bool AnyListed { get; private set; }
    public JoinTarget? Found { get; private set; }
    public string? CodeError { get; private set; }
    public string? PickError { get; private set; }
    public string? RoleError { get; private set; }
    public bool CodeOpen { get; private set; }
    public List<MyMembership> Waiting { get; private set; } = [];
    /// <summary>임시 부서 "처음 오신 분"에서 왔는지(0064) — 노란 안내.</summary>
    public bool InLobby { get; private set; }
    /// <summary>이미 소속 · 신청한 부서 → 표시 문구(신청할 수 없음, 0027).</summary>
    public Dictionary<int, string> Mine { get; private set; } = [];
    /// <summary>임시 부서가 아닌 소속(한 사람 한 부서, ADR 0090) — 있으면 신청 목록 대신 "이미 함께하고 있어요"(2026-11-18 사용자).</summary>
    public MyMembership? Member { get; private set; }

    public static readonly (string Kind, string Label)[] RoleChoices =
        [(SignupRoleKind.Student, "학생"), (SignupRoleKind.Teacher, "교사"), (SignupRoleKind.Parent, "학부모"), (SignupRoleKind.Etc, "기타")];

    private string Me => User.UserName()!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadMineAsync(ct);
        // 이미 정식 부서(운영 중)에 소속이면 신청할 곳이 없다 — 어떤 경로로 열어도(앱을 다시 열 때 마지막 주소 등) 그 부서 홈으로(2026-11-18 사용자)
        if (Member is { TenantActive: true } m && Waiting.Count == 0) return Redirect($"/{m.Slug}");
        // 부서 주소로 들어와 코드가 미리 채워졌으면 그 부서를 골라 둔다(0045)
        if (!string.IsNullOrWhiteSpace(Code)) await FindAsync(ct);
        await LoadListAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostFindAsync(CancellationToken ct)
    {
        await LoadMineAsync(ct);
        CodeOpen = true;
        await FindAsync(ct);
        await LoadListAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostApplyAsync(CancellationToken ct)
    {
                if (TenantId != 0 || !string.IsNullOrWhiteSpace(Code))
        {
            // 목록에서 고른 부서, 또는 고르지 않고 코드만 넣은 경우(예전 방식) 그 코드의 부서
            var (error, _) = TenantId != 0
                ? await join.ApplyAsync(Me, TenantId, Code, RoleKind, ct)
                : await join.ApplyAsync(Me, Code, RoleKind, ct);
            if (error == JoinError.None)
            {
                // 승인 없이 바로 소속(2026-11-20, R-340): 부서 관리자 · 플랫폼 관리자에게 알림 쪽지, 그 부서 홈으로
                var target = TenantId != 0 ? (await join.ListedAsync(ct)).FirstOrDefault(d => d.TenantId == TenantId) ?? await join.LookupAsync(Code ?? "", ct) : await join.LookupAsync(Code ?? "", ct);
                var role = RoleChoices.FirstOrDefault(r => r.Kind == RoleKind).Label ?? RoleKind;
                var d = MessageService.ShortDept(target?.Name ?? "", false);
                if (target is not null)
                    await msgs.NotifyDeptAsync(Me, target.TenantId, "부서 참여", $"{User.DisplayName()}님이 {(d.Length > 0 ? d : "부서")}에 들어왔어요 · {role}\n승인 없이 바로 들어와요. 명부와 이어지지 않았으면 관리 › 소속 관리에서 이어 주세요.", ct);
                return target is { Slug.Length: > 0 } ? Redirect("/" + target.Slug) : RedirectToPage(new { Code = (string?)null, Q = (string?)null });
            }
            if (error is JoinError.NoRole or JoinError.BadRole) RoleError = JoinService.Message(error);
            else if (error == JoinError.AlreadyMember)
            {
                // 고른 부서가 아니라 다른 부서 소속 · 신청 때문일 수 있다 — 실제 부서 이름으로 알린다(2026-11-18)
                var mine = await accounts.MyMembershipsAsync(Me, ct);
                var other = mine.FirstOrDefault(m => !m.IsLobby && m.Status == MembershipStatus.Approved) ?? mine.FirstOrDefault(m => m.Status == MembershipStatus.Requested);
                PickError = other is null ? JoinService.Message(error)
                    : $"이미 {Split(other.TenantName).Dept}{(other.Status == MembershipStatus.Requested ? "에 신청해 승인을 기다리고" : "에 소속돼")} 있어요. 한 사람은 한 부서에만 참여할 수 있어요.";
            }
            else PickError = "고른 부서에 신청할 수 없어요. 다시 골라 주세요.";
        }
        else PickError = "부서를 먼저 골라 주세요.";
        await LoadMineAsync(ct);
        if (!string.IsNullOrWhiteSpace(Code)) Found = await join.LookupAsync(Code, ct);
        await LoadListAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(int tenantId, CancellationToken ct)
    {
        await join.CancelAsync(Me, tenantId, ct);
        return RedirectToPage(new { Code = (string?)null, Q = (string?)null });
    }

    private async Task FindAsync(CancellationToken ct)
    {
        Code = JoinService.NormalizeCode(Code);
        if (Code.Length == 0) { CodeError = "부서 코드를 넣어 주세요."; return; }
        if (!limiter.TryTake(Me)) { CodeError = "너무 여러 번 찾았어요. 1분 뒤에 다시 해 주세요."; return; }
        Found = await join.LookupAsync(Code, ct);
        if (Found is null) CodeError = JoinService.Message(JoinError.NotFound);
        else if (!Mine.ContainsKey(Found.TenantId)) TenantId = Found.TenantId;
    }

    private async Task LoadListAsync(CancellationToken ct)
    {
        var listed = await join.ListedAsync(ct);
        AnyListed = listed.Count > 0;
        var q = Q?.Trim();
        Depts = listed.Where(d => string.IsNullOrEmpty(q) || d.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        if (Found is not null && Depts.All(d => d.TenantId != Found.TenantId)) Depts.Insert(0, Found);
    }

    private async Task LoadMineAsync(CancellationToken ct)
    {
        var mine = await accounts.MyMembershipsAsync(Me, ct);
        Waiting = mine.Where(m => m.Status == MembershipStatus.Requested).ToList();
        InLobby = mine.Any(m => m.IsLobby && m.Status == MembershipStatus.Approved);
        Member = mine.Where(m => !m.IsLobby && m.Status == MembershipStatus.Approved).OrderByDescending(m => m.TenantActive).FirstOrDefault();
        Mine = mine.ToDictionary(m => m.TenantId, m => m.Status switch
        {
            MembershipStatus.Approved => "이미 소속",
            MembershipStatus.Requested => "승인 기다리는 중",
            _ => "신청할 수 없음",
        });
    }

    /// <summary>"잠실새내교회 유년부" → ("잠실새내교회", "유년부"). 빈칸이 없으면 앞부분은 비운다.</summary>
    public static (string Church, string Dept) Split(string name)
    {
        var i = name.Trim().LastIndexOf(' ');
        return i <= 0 ? ("", name.Trim()) : (name[..i].Trim(), name[(i + 1)..].Trim());
    }
}
