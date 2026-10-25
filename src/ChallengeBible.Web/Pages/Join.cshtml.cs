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
public class JoinModel(JoinService join, AccountService accounts, LookupLimiter limiter) : PageModel, ITitled
{
    public string Title => "부서 참여 신청";

    [BindProperty(SupportsGet = true)] public string Code { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty] public int TenantId { get; set; }
    [BindProperty] public List<string> SelectedRoles { get; set; } = [Roles.Participant];
    [BindProperty(SupportsGet = true)] public int Welcome { get; set; }

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

    private int Me => User.AccountId()!.Value;

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadMineAsync(ct);
        // 부서 주소로 들어와 코드가 미리 채워졌으면 그 부서를 골라 둔다(0045)
        if (!string.IsNullOrWhiteSpace(Code)) await FindAsync(ct);
        await LoadListAsync(ct);
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
        SelectedRoles = [Roles.Participant];   // v3: 역할은 묻지 않는다 — 보낸 값은 쓰지 않음
        if (TenantId != 0 || !string.IsNullOrWhiteSpace(Code))
        {
            // 목록에서 고른 부서, 또는 고르지 않고 코드만 넣은 경우(예전 방식) 그 코드의 부서
            var (error, _) = TenantId != 0
                ? await join.ApplyAsync(Me, TenantId, Code, SelectedRoles, ct)
                : await join.ApplyAsync(Me, Code, SelectedRoles, ct);
            if (error == JoinError.None) return RedirectToPage(new { Code = (string?)null, Q = (string?)null });
            if (error is JoinError.NoRole or JoinError.BadRole) RoleError = JoinService.Message(error);
            else PickError = error == JoinError.AlreadyMember ? JoinService.Message(error) : "고른 부서에 신청할 수 없어요. 다시 골라 주세요.";
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
        Mine = mine.ToDictionary(m => m.TenantId, m => m.Status switch
        {
            MembershipStatus.Approved or MembershipStatus.Suspended => "이미 소속",
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
