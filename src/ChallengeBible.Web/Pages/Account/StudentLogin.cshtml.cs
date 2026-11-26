using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Account;

/// <summary>
/// `/account/student-login/{dept}` 부서 대표 학생 로그인(R-367): 비밀번호만. 어느 아이인지는 누를 때 고른다(아이디 · 이름은 화면 · HTML 에 쓰지 않음).
/// 버튼이 없는 부서는 404. 틀린 횟수는 관리자 로그인과 함께 센다.
/// </summary>
public class StudentLoginModel(AccountService accounts, AdminLoginLimiter limiter, DeptStudentLoginService students) : PageModel, ITitled
{
    public string Title => Dept is null ? "학생 로그인" : Dept.Short + " 학생 로그인";

    /// <summary>부서 주소. 경로 이름을 slug 로 하면 TenantPageFilter 가 부서 화면으로 본다.</summary>
    [FromRoute(Name = "dept")] public string? Slug { get; set; }
    [BindProperty] public string Password { get; set; } = "";
    public DeptAdminButton? Dept { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Dept = (await students.FindAsync(Slug, ct))?.Dept;
        return Dept is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var pick = await students.FindAsync(Slug, ct);
        if (pick is null) return NotFound();
        Dept = pick.Dept;
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "-";
        if (limiter.Blocked(ip)) { Error = "여러 번 틀려서 잠시 막았어요. 15분 뒤에 다시 해 주세요."; Password = ""; return Page(); }

        var r = await accounts.LoginAsync(pick.UserName, Password, ct);
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
        TempData[ChallengeBible.Web.Pages.TenantHomeModel.MsgBandKey] = "1";   // 새 쪽지 띠 한 번(ADR 0112)
        return LocalRedirect("/");   // 2026-11-26 사용자: 로그인한 뒤 화면 = 앱 아이콘으로 여는 첫 화면
    }
}
