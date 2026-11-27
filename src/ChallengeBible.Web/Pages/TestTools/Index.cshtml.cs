using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.TestTools;

/// <summary>`/test-tools` 옛 주소(ADR 0102): 목적별 두 화면으로 나눴다. 플랫폼 관리자 → 가짜 데이터 넣기, 가짜 계정 → 상황별 로그인.</summary>
public class IndexModel(TestToolsService tools) : PageModel
{
    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!tools.Enabled || !await tools.CanUseAsync(User.UserName(), ct)) return NotFound();
        return Redirect(await tools.IsPlatformAdminAsync(User.UserName()!, ct) ? "/test-tools/data" : "/test-tools/login");
    }
}
