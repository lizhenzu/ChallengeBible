using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages;

/// <summary>
/// `/test-tools?t=부서번호` 임시 시험 도구(ADR 0095): 가짜 데이터 넣기 · 지우기(플랫폼 관리자만) + 상황별 로그인 버튼(비밀번호 1234).
/// 설정 `TestTools:Enabled` 가 꺼져 있거나, 플랫폼 관리자 · 가짜 계정이 아니면 없는 화면처럼 404.
/// </summary>
public class TestToolsModel(TestToolsService tools, AccountService accounts, UserSwitchService switcher) : PageModel, ITitled
{
    public string Title => "시험 도구(임시)";

    [BindProperty(SupportsGet = true, Name = "t")] public int? TenantId { get; set; }

    public bool IsAdmin { get; private set; }
    public IReadOnlyList<FakeDept> Depts { get; private set; } = [];
    public FakeDept? Dept { get; private set; }
    public int FakeAccounts { get; private set; }
    public FakeRunResult? Result { get; private set; }
    public string? ResultTitle { get; private set; }
    public string? Error { get; private set; }

    private async Task<bool> AllowedAsync(CancellationToken ct) => await tools.CanUseAsync(User.UserName(), ct);

    private async Task LoadAsync(CancellationToken ct)
    {
        var me = User.UserName()!;
        IsAdmin = await tools.IsPlatformAdminAsync(me, ct);
        (Depts, FakeAccounts) = await tools.LoginsAsync(me, ct);
        Dept = Depts.FirstOrDefault(d => d.TenantId == TenantId) ?? Depts.FirstOrDefault();
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!tools.Enabled) return NotFound();
        if (!await AllowedAsync(ct)) return NotFound();
        await LoadAsync(ct);
        return Page();
    }

    /// <summary>가짜 데이터 넣기(kind=create) · 지우기(kind=delete). 확인 글자 FAKE 가 있어야 한다(시험 실행은 없앴다, 2026-11-16 사용자).</summary>
    public async Task<IActionResult> OnPostRunAsync(string kind, string? confirm, CancellationToken ct)
    {
        if (!tools.Enabled || !await AllowedAsync(ct)) return NotFound();
        var me = User.UserName()!;
        if (!await tools.IsPlatformAdminAsync(me, ct)) return Forbid();
        var create = kind == "create";
        ResultTitle = create ? "가짜 데이터 넣기" : "가짜 데이터 지우기";
        if (!string.Equals(confirm?.Trim(), TestToolsService.ConfirmWord, StringComparison.Ordinal))
            Error = $"실행하려면 확인 칸에 {TestToolsService.ConfirmWord} 를 써야 해요.";
        else
            Result = await tools.RunAsync(me, create, commit: true, ct);
        await LoadAsync(ct);
        return Page();
    }

    /// <summary>관리자 없는 부서에 시험 관리자 만들기(비밀번호 1234). 플랫폼 관리자만.</summary>
    public async Task<IActionResult> OnPostAdminsAsync(CancellationToken ct)
    {
        if (!tools.Enabled || !await AllowedAsync(ct)) return NotFound();
        var me = User.UserName()!;
        if (!await tools.IsPlatformAdminAsync(me, ct)) return Forbid();
        var made = await tools.CreateMissingAdminsAsync(me, ct);
        AdminsMessage = made.Count == 0 ? "관리자가 없는 부서가 없어요." : $"관리자 {made.Count}명을 만들었어요(비밀번호 1234) · " + string.Join(" · ", made.Select(m => $"{m.Dept} {m.UserName}"));
        await LoadAsync(ct);
        return Page();
    }

    public string? AdminsMessage { get; private set; }

    /// <summary>
    /// 상황별 로그인: 가짜 계정으로 바로 들어간다. 들어가면 첫 화면으로.
    /// 플랫폼 관리자(또는 가장 중)는 가장으로 들어가고 비밀번호를 보지 않는다 — 가짜 계정인지만 본다(부서 관리자 비밀번호를 바꿔도 된다, 2026-11-15).
    /// 가짜 계정이 누를 때는 비밀번호 1234 로 일반 로그인과 같은 확인을 한다(비밀번호가 바뀐 계정으로는 못 들어간다).
    /// </summary>
    public async Task<IActionResult> OnPostLoginAsync(string user, CancellationToken ct)
    {
        if (!tools.Enabled || !await AllowedAsync(ct)) return NotFound();
        // 플랫폼 관리자(또는 가장 중)면 가장으로 들어간다(ADR 0096): 띠의 [사람 바꾸기] · [복귀]가 보인다
        var me = User.UserName()!;
        var admin = User.ImpersonatorId() ?? (await tools.IsPlatformAdminAsync(me, ct) ? me : null);
        if (admin is not null)
        {
            if (!await tools.IsFakeUserAsync(user, ct)) return NotFound();
            var (_, target, _) = await switcher.SwitchAsync(admin, me, user, ct);
            if (target is null) return NotFound();
            await CurrentUser.SignInAsync(HttpContext, await accounts.ToSignedInAsync(target, ct), admin);
            return LocalRedirect("/");
        }
        var account = await tools.FakeLoginAsync(accounts, user, ct);
        if (account is null) return NotFound();
        await CurrentUser.SignInAsync(HttpContext, await accounts.ToSignedInAsync(account, ct));
        return LocalRedirect("/");
    }
}
