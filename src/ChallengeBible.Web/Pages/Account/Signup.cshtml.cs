using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Account;

public class SignupModel(AccountService accounts) : PageModel, ITitled
{
    public string Title => "회원가입";

    [BindProperty] public string DisplayName { get; set; } = "";
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string PasswordConfirm { get; set; } = "";

    public FieldErrors Errors { get; private set; } = new();

    public string? ErrorOf(string field) => Errors.TryGetValue(field, out var m) ? m : null;

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var (account, errors) = await accounts.SignUpAsync(new SignUpInput(DisplayName, Email, Password, PasswordConfirm), ct);
        if (account is null)
        {
            Errors = errors;
            Password = PasswordConfirm = "";
            return Page();
        }
        await CurrentUser.SignInAsync(HttpContext, account);
        return RedirectToPage("/Join", new { welcome = 1 });
    }
}
