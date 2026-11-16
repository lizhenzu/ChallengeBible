using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/admin-rule` 부서 관리자 규칙(ADR 0101, 확정 시안 할일이동-역할업무 ③): 체크한 역할 · 업무는 모든 부서에서 바로 부서 관리자. 어드민은 늘.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class AdminRuleModel(DeptAdminRuleService rules) : PageModel, ITitled
{
    public string Title => "부서 관리자 규칙";
    public AdminRuleView View { get; private set; } = new([], [], 0, 0);
    [TempData] public string? Msg { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => View = await rules.ViewAsync(User.UserName()!, ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var (granted, revoked, moved) = await rules.SaveAsync(User.UserName()!, Request.Form["code"].OfType<string>(), Request.Form["duty"].OfType<string>(), ct);
        Msg = $"저장했어요 · 관리자가 된 사람 {granted}명 · 빠진 사람 {revoked}명" + (moved > 0 ? $" · 어드민 역할로 옮긴 관리자 {moved}명" : "");
        return Redirect("/platform/admin-rule");
    }

    public async Task<IActionResult> OnPostDutyAsync(string? name, CancellationToken ct)
    {
        Msg = await rules.AddDutyAsync(User.UserName()!, name, ct) ? $"업무 \"{name!.Trim()}\"을(를) 만들었어요. 관리자 업무로 하려면 체크하고 저장해요." : "업무 이름이 비었거나 이미 있어요.";
        return Redirect("/platform/admin-rule#duties");
    }
}
