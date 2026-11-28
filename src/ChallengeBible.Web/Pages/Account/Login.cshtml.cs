using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Account;

public class LoginModel(AccountService accounts, DemoLoginService demo) : PageModel, ITitled
{
    public string Title => "로그인";

    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    /// <summary>영상 촬영용 임시 버튼(ADR 0143): 데모 로그인이 켜져 있고 기한 안일 때만 주소가 있다.</summary>
    public string? DemoUserLink => demo.LinkFor("user");
    public string? DemoAdminLink => demo.LinkFor("admin");

    public string? Error { get; private set; }
    public string? ErrorField { get; private set; }

    /// <summary>로그인 직후 한 번만 "할 일이 있으면 할 일 화면으로"를 확인하게 하는 표시(TempData, R-309).</summary>
    public const string TodoCheckKey = "todo-check";

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var r = await accounts.LoginAsync(Email, Password, ct);
        if (r.Account is null)
        {
            Error = r.Error;
            ErrorField = string.IsNullOrWhiteSpace(Email) ? "Email" : "Password";
            Password = "";
            return Page();
        }
        await CurrentUser.SignInAsync(HttpContext, await accounts.ToSignedInAsync(r.Account, ct));
        if (r.MustChangePassword) return RedirectToPage("/Account/ChangePassword");   // 0035
        if (!Url.IsLocalUrl(ReturnUrl)) TempData[TodoCheckKey] = "1";
        TempData[ChallengeBible.Web.Pages.TenantHomeModel.MsgBandKey] = "1";   // 새 쪽지 띠 한 번(ADR 0112)   // 오늘 할 일(ADR 0100): 처음 가는 화면에서 할 일이 있으면 할 일 화면으로
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : await accounts.HomePathAsync(r.Account.UserName!, ct));   // 새 시안 = 부서 홈
    }
}
