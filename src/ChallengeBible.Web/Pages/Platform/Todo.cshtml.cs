using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/todo` 플랫폼 관리자의 오늘 할 일(ADR 0100, 확정 시안 오늘할일 ③): 내 할 일(처리) + 부서 현황(보기만) + 공통 규칙(누가 보나).</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class TodoModel(PlatformTodoService todo, TodoRuleService rules) : PageModel, ITitled
{
    public string Title => "오늘 할 일";
    public IReadOnlyList<PlatformTodo> Mine { get; private set; } = [];
    public IReadOnlyList<PlatformTodoDept> Depts { get; private set; } = [];
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Common { get; private set; } = new Dictionary<string, IReadOnlyList<string>>();
    [TempData] public string? Msg { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        (Mine, Depts) = await todo.GetAsync(User.UserName()!, ct);
        Common = await rules.CommonAsync(User.UserName()!, ct);
    }

    public async Task<IActionResult> OnPostSnoozeAsync(int days, CancellationToken ct)
    {
        await todo.SnoozeAsync(User.UserName()!, days, ct);
        return Redirect("/platform");
    }

    /// <summary>공통 규칙 저장(플랫폼 관리자): 부서가 "공통 규칙 따름"이면 이 값.</summary>
    public async Task<IActionResult> OnPostRulesAsync(CancellationToken ct)
    {
        var map = TodoKinds.All.ToDictionary(k => k, k => (IReadOnlyList<string>)Request.Form[$"r_{k}"].OfType<string>().ToList());
        await rules.SaveCommonAsync(User.UserName()!, map, ct);
        Msg = "공통 규칙을 저장했어요.";
        return Redirect("/platform/todo#rules");
    }
}
