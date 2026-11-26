using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Account;

/// <summary>
/// `/account/dept-login/{slug}` 부서 관리자 로그인(R-364, 확정 시안 첫화면-부서관리자 B): 비밀번호만 넣는다.
/// 아이디는 부서주소-admin(화면에 쓰지 않음). 첫 화면에 버튼이 있는 부서가 아니면 404. 틀린 횟수는 관리자 로그인과 함께 센다(AdminLoginLimiter).
/// </summary>
public class DeptLoginModel(AccountService accounts, AdminLoginLimiter limiter, DeptAdminLoginService depts) : PageModel, ITitled
{
    public string Title => Dept is null ? "관리자 로그인" : Dept.Label + " 로그인";

    /// <summary>부서 주소. 경로 이름을 slug 로 하면 TenantPageFilter 가 부서 화면으로 보고 참여 신청으로 보낸다.</summary>
    [FromRoute(Name = "dept")] public string? Slug { get; set; }
    [BindProperty] public string Password { get; set; } = "";
    public DeptAdminButton? Dept { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Dept = await depts.FindAsync(Slug, ct);
        return Dept is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        Dept = await depts.FindAsync(Slug, ct);
        if (Dept is null) return NotFound();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "-";
        if (limiter.Blocked(ip)) { Error = "여러 번 틀려서 잠시 막았어요. 15분 뒤에 다시 해 주세요."; Password = ""; return Page(); }

        var r = await accounts.LoginAsync(DeptAdminLoginService.UserNameOf(Dept.Slug), Password, ct);
        var signed = r.Account is null ? null : await accounts.ToSignedInAsync(r.Account, ct);
        if (signed is null)
        {
            if (!string.IsNullOrEmpty(Password)) limiter.Failed(ip);
            Error = string.IsNullOrEmpty(Password) ? "비밀번호를 넣어 주세요." : "비밀번호가 맞지 않아요.";
            Password = "";
            return Page();
        }
        await CurrentUser.SignInAsync(HttpContext, signed);
        if (r.MustChangePassword) return RedirectToPage("/Account/ChangePassword");
        TempData[LoginModel.TodoCheckKey] = "1";   // 오늘 할 일(ADR 0100, R-309)
        TempData[ChallengeBible.Web.Pages.TenantHomeModel.MsgBandKey] = "1";   // 새 쪽지 띠 한 번(ADR 0112)
        return LocalRedirect("/" + Dept.Slug);   // 2026-11-27 사용자: 로그인한 뒤 = 부서 홈(바탕화면 아이콘으로 열 때와 같은 화면)
    }
}
