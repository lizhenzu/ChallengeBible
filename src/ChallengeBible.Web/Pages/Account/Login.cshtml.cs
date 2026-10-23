using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Account;

public class LoginModel(AccountService accounts) : PageModel, ITitled
{
    public string Title => "로그인";

    [BindProperty] public string LoginId { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }
    public string? ErrorField { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var r = await accounts.LoginAsync(LoginId, Password, ct);
        if (r.Account is null)
        {
            Error = r.Error;
            ErrorField = string.IsNullOrWhiteSpace(LoginId) ? "LoginId" : "Password";
            Password = "";
            return Page();
        }
        await CurrentUser.SignInAsync(HttpContext, r.Account);
        if (r.Account.MustChangePassword) return RedirectToPage("/Account/ChangePassword");   // 0035
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
    }
}
