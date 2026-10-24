using ChallengeBible.Domain.Time;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages;

/// <summary>
/// `/` 공통 첫 화면(0045). 로그인 전에는 부서 이름 없는 소개, 로그인 뒤에는 승인된 소속 수로 나눈다:
/// 1곳 → 그 부서 홈, 2곳 이상 → 부서 고르기, 0곳 → 참여 신청.
/// </summary>
public class IndexModel(AccountService accounts, TenantClock clock) : PageModel, ITitled
{
    public string Title => "";
    /// <summary>맨 아래 © 연도: 부서가 없는 화면이라 한국 시간 기준 올해(하드코딩 안 함).</summary>
    public int Year => clock.Today("Asia/Seoul").Year;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (User.AccountId() is not int id) return Page();
        var go = await accounts.WhereToGoAsync(id, ct);
        if (go.ApprovedCount == 0 && User.IsPlatformAdmin()) return Redirect("/platform");
        return go.ApprovedCount switch
        {
            0 => RedirectToPage("/Join"),
            1 => Redirect($"/{go.OnlyTenantSlug}"),
            _ => RedirectToPage("/Account/Tenants"),
        };
    }
}
