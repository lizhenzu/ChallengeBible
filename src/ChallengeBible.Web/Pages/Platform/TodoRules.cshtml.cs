using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/todorules` 할 일 공통 규칙(ADR 0100, R-312; 2026-11-21 사용자: 오늘 할 일에서 쪼갬): 부서가 "공통 규칙 따름"이면 이 값.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class TodoRulesModel(TodoRuleService rules) : PageModel, ITitled
{
    public string Title => "할 일 공통 규칙";
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Common { get; private set; } = new Dictionary<string, IReadOnlyList<string>>();
    [TempData] public string? Msg { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => Common = await rules.CommonAsync(User.UserName()!, ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var map = TodoKinds.All.ToDictionary(k => k, k => (IReadOnlyList<string>)Request.Form[$"r_{k}"].OfType<string>().ToList());
        await rules.SaveCommonAsync(User.UserName()!, map, ct);
        Msg = "공통 규칙을 저장했어요.";
        return Redirect("/platform/todorules");
    }
}
