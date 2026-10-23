using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages;

/// <summary>`/join` 부서 코드로 참여 신청(0045, 0048). 찾기 → 이 부서가 맞나요 → 역할 고르기 → 신청 → 승인 기다림.</summary>
public class JoinModel(JoinService join, AccountService accounts, LookupLimiter limiter) : PageModel, ITitled
{
    public string Title => "부서 참여 신청";

    [BindProperty(SupportsGet = true)] public string Code { get; set; } = "";
    [BindProperty] public List<string> SelectedRoles { get; set; } = [Roles.Participant];
    [BindProperty(SupportsGet = true)] public int Welcome { get; set; }

    public JoinTarget? Found { get; private set; }
    public string? CodeError { get; private set; }
    public string? RoleError { get; private set; }
    public List<MyMembership> Waiting { get; private set; } = [];
    public bool HasApproved { get; private set; }

    private int Me => User.AccountId()!.Value;

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadMineAsync(ct);
        // 부서 주소로 들어와 코드가 미리 채워졌으면 바로 찾아 둔다(0045)
        if (!string.IsNullOrWhiteSpace(Code)) await FindAsync(ct);
    }

    public async Task<IActionResult> OnPostFindAsync(CancellationToken ct)
    {
        await LoadMineAsync(ct);
        await FindAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostApplyAsync(CancellationToken ct)
    {
        var (error, _) = await join.ApplyAsync(Me, Code, SelectedRoles, ct);
        if (error == JoinError.None) return RedirectToPage();
        await LoadMineAsync(ct);
        if (error is JoinError.NoRole or JoinError.BadRole)
        {
            Found = await join.LookupAsync(Code, ct);
            RoleError = JoinService.Message(error);
        }
        else CodeError = JoinService.Message(error);
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(int tenantId, CancellationToken ct)
    {
        await join.CancelAsync(Me, tenantId, ct);
        return RedirectToPage();
    }

    private async Task FindAsync(CancellationToken ct)
    {
        Code = JoinService.NormalizeCode(Code);
        if (Code.Length == 0) { CodeError = "부서 코드를 넣어 주세요."; return; }
        if (!limiter.TryTake(Me)) { CodeError = "너무 여러 번 찾았어요. 1분 뒤에 다시 해 주세요."; return; }
        Found = await join.LookupAsync(Code, ct);
        if (Found is null) CodeError = JoinService.Message(JoinError.NotFound);
    }

    private async Task LoadMineAsync(CancellationToken ct)
    {
        var mine = await accounts.MyMembershipsAsync(Me, ct);
        Waiting = mine.Where(m => m.Status == MembershipStatus.Requested).ToList();
        HasApproved = mine.Any(m => m.Status == MembershipStatus.Approved && m.TenantActive);
    }
}
