using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/admin-rule` 부서 관리자 규칙(확정 시안 관리자규칙, 2026-11-16): 체크한 직분은 모든 부서에서 바로 부서 관리자.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class AdminRuleModel(DeptAdminRuleService rules) : PageModel, ITitled
{
    public string Title => "부서 관리자 규칙";
    public AdminRuleView View { get; private set; } = new([], 0, 0);
    [TempData] public string? Msg { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => View = await rules.ViewAsync(User.UserName()!, ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var codes = Request.Form["code"].OfType<string>().ToList();
        var (granted, revoked) = await rules.SaveAsync(User.UserName()!, codes, ct);
        Msg = $"저장했어요 · 관리자가 된 사람 {granted}명 · 빠진 사람 {revoked}명";
        return Redirect("/platform/admin-rule");
    }
}
