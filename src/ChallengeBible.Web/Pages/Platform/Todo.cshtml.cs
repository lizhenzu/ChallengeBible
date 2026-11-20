using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/todo` 플랫폼 관리자의 오늘 할 일(ADR 0100, 확정 시안 오늘할일 ③): 내 할 일(처리) + 부서 현황(보기만) + 공통 규칙(누가 보나).</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class TodoModel(PlatformTodoService todo, TodoRuleService rules, LeaveService leave) : PageModel, ITitled
{
    public string Title => "오늘 할 일";
    public IReadOnlyList<PlatformTodo> Mine { get; private set; } = [];
    public IReadOnlyList<PlatformTodoDept> Depts { get; private set; } = [];
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Common { get; private set; } = new Dictionary<string, IReadOnlyList<string>>();
    [TempData] public string? Msg { get; set; }
    /// <summary>모든 부서의 기다리는 탈퇴 신청(ADR 0119).</summary>
    public IReadOnlyList<LeaveRow> Leaves { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        (Mine, Depts) = await todo.GetAsync(User.UserName()!, ct);
        Common = await rules.CommonAsync(User.UserName()!, ct);
        Leaves = await leave.ListAsync(User.UserName()!, null, ct);
    }

    /// <summary>탈퇴 승인 · 반려(R-343): 플랫폼 관리자는 모든 부서.</summary>
    public async Task<IActionResult> OnPostLeaveAsync(int leaveId, string? leaveDo, CancellationToken ct)
    {
        var me = User.UserName()!;
        if (leaveDo == "ok")
        {
            var e = await leave.ApproveAsync(me, leaveId, null, ct);
            Msg = e == LeaveError.None ? "탈퇴를 승인했어요. 계정과 기록을 지웠어요." : LeaveService.Message(e);
        }
        else Msg = await leave.RejectAsync(me, leaveId, null, ct) ? "탈퇴 신청을 반려했어요." : LeaveService.Message(LeaveError.NotFound);
        return Redirect("/platform/todo");
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
