using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Account;

/// <summary>`POST /account/impersonate/stop` 가장 끝내기 [복귀](ADR 0067). 가장한 관리자가 지금도 플랫폼 관리자면 그 계정으로 돌아가고, 아니면 로그아웃.</summary>
public class ImpersonateStopModel(ImpersonationService imp) : PageModel
{
    public IActionResult OnGet() => Redirect("/");

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (User.ImpersonatorId() is not int admin || User.AccountId() is not int me) return Redirect("/");
        var back = await imp.StopAsync(admin, me, ct);
        if (back is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect("/");
        }
        await CurrentUser.SignInAsync(HttpContext, back);
        return Redirect($"/platform/accounts?id={me}");
    }
}
