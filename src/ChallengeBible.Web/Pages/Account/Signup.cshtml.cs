using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Account;

public class SignupModel(AccountService accounts, LobbyService lobby, MessageService msgs, RosterLinkService roster) : PageModel, ITitled
{
    public string Title => "회원가입";

    [BindProperty] public string DisplayName { get; set; } = "";
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string PasswordConfirm { get; set; } = "";
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public int BMonth { get; set; }
    [BindProperty] public int BDay { get; set; }

    public FieldErrors Errors { get; private set; } = new();

    public string? ErrorOf(string field) => Errors.TryGetValue(field, out var m) ? m : null;

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var (account, errors) = await accounts.SignUpAsync(new SignUpInput(DisplayName, Email, Password, PasswordConfirm, Phone, BMonth, BDay), ct);
        if (account is null)
        {
            Errors = errors;
            Password = PasswordConfirm = "";
            return Page();
        }
        await CurrentUser.SignInAsync(HttpContext, await accounts.ToSignedInAsync(account, ct));
        // 자동 알림(R-329, 2026-11-17 사용자): 회원 가입을 superadmin 에게 쪽지로
        await msgs.NotifyAsync(account.UserName!, "회원 가입", $"새 회원 가입 · {account.DisplayName} (아이디 {account.UserName})\n가입하면 임시 부서 \"처음 오신 분\"에서 시작하고, 내 부서를 고르면 승인 없이 바로 들어가요.", ct);
        // R-171: 임시 부서 "처음 오신 분"에 바로 들어가 미션 · 통독을 한다(0064). 임시 부서가 아직 없으면 예전처럼 참여 신청으로
        var lobbied = await lobby.EnrollAsync(account.UserName!, account.Id, ct);
        // R-341: 명부에 같은 이름이 있으면 본인 확인으로 잇는 화면부터
        if ((await roster.CandidatesAsync(account.UserName!, ct)).Count > 0) return Redirect("/account/link");
        if (lobbied) return Redirect("/");
        return RedirectToPage("/Join", new { welcome = 1 });
    }
}
