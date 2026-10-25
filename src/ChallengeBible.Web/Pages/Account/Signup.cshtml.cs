using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Account;

public class SignupModel(AccountService accounts, LobbyService lobby) : PageModel, ITitled
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
        // R-171: 임시 부서 "처음 오신 분"에 바로 들어가 미션 · 통독을 한다(0064). 임시 부서가 아직 없으면 예전처럼 참여 신청으로
        if (await lobby.EnrollAsync(account.Id, ct)) return Redirect("/");
        return RedirectToPage("/Join", new { welcome = 1 });
    }
}
