using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Account;

/// <summary>
/// `/account/admin-login` 플랫폼 관리자 로그인(비밀번호만, 2026-11-12 사용자 요청).
/// 아이디는 설정 `AdminLogin:UserName`(없으면 superadmin) — 화면에 쓰지 않는다. 플랫폼 관리자가 아니면 들어가지 못한다.
/// 비밀번호만 맞히면 되므로 틀린 횟수를 세게 막는다(AdminLoginLimiter).
/// </summary>
public class AdminLoginModel(AccountService accounts, AdminLoginLimiter limiter, IConfiguration config) : PageModel, ITitled
{
    public string Title => "관리자 로그인";
    public const string DefaultUserName = "superadmin";
    private const string Wrong = "비밀번호가 맞지 않아요.";

    [BindProperty] public string Password { get; set; } = "";
    public string? Error { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "-";
        if (limiter.Blocked(ip)) { Error = "여러 번 틀려서 잠시 막았어요. 15분 뒤에 다시 해 주세요."; Password = ""; return Page(); }

        var userName = config["AdminLogin:UserName"] is { Length: > 0 } u ? u : DefaultUserName;
        var r = await accounts.LoginAsync(userName, Password, ct);
        var signed = r.Account is null ? null : await accounts.ToSignedInAsync(r.Account, ct);
        if (signed is null || !signed.IsPlatformAdmin)
        {
            if (!string.IsNullOrEmpty(Password)) limiter.Failed(ip);
            Error = string.IsNullOrEmpty(Password) ? "비밀번호를 넣어 주세요." : Wrong;
            Password = "";
            return Page();
        }
        await CurrentUser.SignInAsync(HttpContext, signed);
        if (r.MustChangePassword) return RedirectToPage("/Account/ChangePassword");
        return LocalRedirect("/platform");
    }
}
