using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/depttodostatus` 부서 현황(R-312, 2026-11-21 사용자: 오늘 할 일에서 쪼갬): 모든 부서의 밀린 일을 보기만 한다.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class DeptTodoStatusModel(PlatformTodoService todo) : PageModel, ITitled
{
    public string Title => "오늘 할 일(부서)";
    public IReadOnlyList<PlatformTodoDept> Depts { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct) => (_, Depts) = await todo.GetAsync(User.UserName()!, ct);
}
