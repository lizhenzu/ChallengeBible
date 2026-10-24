using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Account;

/// <summary>
/// 새 비밀번호 정하기. 임시 비밀번호로 로그인한 뒤에는 이 화면을 거쳐야 한다(0035) — 그때만 지금 비밀번호를 묻지 않는다.
/// 스스로 바꿀 때는 지금 비밀번호를 확인한다(내 계정 확정).
/// </summary>
public class ChangePasswordModel(AccountService accounts) : PageModel, ITitled
{
    public string Title => "새 비밀번호";

    [BindProperty] public string Current { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string PasswordConfirm { get; set; } = "";
    public bool Forced { get; private set; }
    public FieldErrors Errors { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct) =>
        Forced = await accounts.MustChangePasswordAsync(User.AccountId()!.Value, ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var me = User.AccountId()!.Value;
        Forced = await accounts.MustChangePasswordAsync(me, ct);
        Errors = Forced
            ? await accounts.ChangePasswordAsync(me, Password, PasswordConfirm, ct)
            : await accounts.ChangeOwnPasswordAsync(me, Current, Password, PasswordConfirm, ct);
        if (!Errors.Ok) return Page();
        await CurrentUser.PasswordChangedAsync(HttpContext);   // 쿠키의 "바꿔야 함" 표시를 뺀다(0035)
        return Redirect("/");
    }
}
