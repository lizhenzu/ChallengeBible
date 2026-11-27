using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.TestTools;

/// <summary>
/// `/test-tools/login?t=부서번호` 상황별 로그인(ADR 0095 · 0102): 가짜 계정(이메일 @fake-seed.test)으로 바로 들어간다.
/// 플랫폼 관리자(또는 가장 중)는 가장으로 들어가고 비밀번호를 보지 않는다. 가짜 계정이 누를 때는 비밀번호 1234 로 일반 로그인과 같은 확인.
/// 들어가면 로그인 직후처럼 할 일이 있으면 할 일 화면으로(R-309). 설정이 꺼져 있거나 플랫폼 관리자 · 가짜 계정이 아니면 404.
/// </summary>
public class LoginModel(TestToolsService tools, AccountService accounts, UserSwitchService switcher) : PageModel, ITitled
{
    public string Title => "상황별 로그인(임시)";

    [BindProperty(SupportsGet = true, Name = "t")] public int? TenantId { get; set; }

    public bool IsAdmin { get; private set; }
    public IReadOnlyList<FakeDept> Depts { get; private set; } = [];
    public FakeDept? Dept { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!tools.Enabled || !await tools.CanUseAsync(User.UserName(), ct)) return NotFound();
        var me = User.UserName()!;
        IsAdmin = await tools.IsPlatformAdminAsync(me, ct);
        Depts = (await tools.LoginsAsync(me, ct)).Depts;
        Dept = Depts.FirstOrDefault(d => d.TenantId == TenantId) ?? Depts.FirstOrDefault();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string user, CancellationToken ct)
    {
        if (!tools.Enabled || !await tools.CanUseAsync(User.UserName(), ct)) return NotFound();
        var me = User.UserName()!;
        var admin = User.ImpersonatorId() ?? (await tools.IsPlatformAdminAsync(me, ct) ? me : null);
        if (admin is not null)
        {
            if (!await tools.IsFakeUserAsync(user, ct)) return NotFound();
            var (_, target, _) = await switcher.SwitchAsync(admin, me, user, ct);
            if (target is null) return NotFound();
            await CurrentUser.SignInAsync(HttpContext, await accounts.ToSignedInAsync(target, ct), admin);
        }
        else
        {
            var account = await tools.FakeLoginAsync(accounts, user, ct);
            if (account is null) return NotFound();
            await CurrentUser.SignInAsync(HttpContext, await accounts.ToSignedInAsync(account, ct));
        }
        TempData[Account.LoginModel.TodoCheckKey] = "1";   // 로그인 직후처럼: 할 일이 있으면 할 일 화면으로(R-309, 2026-11-16 사용자)
        return LocalRedirect("/");
    }
}
