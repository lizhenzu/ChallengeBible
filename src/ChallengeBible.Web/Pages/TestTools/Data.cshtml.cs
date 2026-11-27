using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.TestTools;

/// <summary>
/// `/test-tools/data` 가짜 데이터 넣기(ADR 0095 · 0102): 플랫폼 관리자만, 확인 글자 FAKE. 지우기는 완전 초기화(/platform/reset).
/// 설정 `TestTools:Enabled` 가 꺼져 있거나 플랫폼 관리자가 아니면 없는 화면처럼 404.
/// </summary>
public class DataModel(TestToolsService tools) : PageModel, ITitled
{
    public string Title => "가짜 데이터 넣기(임시)";
    public int FakeAccounts { get; private set; }
    public FakeRunResult? Result { get; private set; }
    public string? Error { get; private set; }

    private async Task<bool> AllowedAsync(CancellationToken ct) =>
        tools.Enabled && User.UserName() is { } me && await tools.IsPlatformAdminAsync(me, ct);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await AllowedAsync(ct)) return NotFound();
        FakeAccounts = (await tools.LoginsAsync(User.UserName()!, ct)).FakeAccounts;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? confirm, CancellationToken ct)
    {
        if (!await AllowedAsync(ct)) return NotFound();
        var me = User.UserName()!;
        if (!string.Equals(confirm?.Trim(), TestToolsService.ConfirmWord, StringComparison.Ordinal))
            Error = $"실행하려면 확인 칸에 {TestToolsService.ConfirmWord} 를 써야 해요.";
        else
            Result = await tools.RunAsync(me, commit: true, ct);
        FakeAccounts = (await tools.LoginsAsync(me, ct)).FakeAccounts;
        return Page();
    }
}
