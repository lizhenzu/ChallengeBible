using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/leaves` 탈퇴 신청(ADR 0119, 확정 시안 탈퇴메뉴 C): 모든 부서의 기다리는 탈퇴를 모아 승인 · 반려. 오늘 할 일의 카드와 같다.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class LeavesModel(LeaveService leave) : PageModel, ITitled
{
    public string Title => "탈퇴 신청";
    public IReadOnlyList<LeaveRow> Leaves { get; private set; } = [];
    [TempData] public string? Msg { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => Leaves = await leave.ListAsync(User.UserName()!, null, ct);

    public async Task<IActionResult> OnPostLeaveAsync(int leaveId, string? leaveDo, CancellationToken ct)
    {
        Msg = await LeaveActions.DoAsync(leave, User.UserName()!, leaveId, leaveDo, null, ct);
        return Redirect("/platform/leaves");
    }
}
