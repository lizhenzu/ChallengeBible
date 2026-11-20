using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Todo;

/// <summary>`/{slug}/todo` 오늘 할 일(ADR 0100, 확정 시안 오늘할일 ① · ②): 내가 볼 수 있는 일을 급한 순서로, 설명 · 정보 · 타임라인을 보고 업무 화면으로 간다(카드에서 완료하지 않는다, 확정 시안 할일타임라인). 위에 [홈으로] · 안 보기.</summary>
public class IndexModel(TodoService todo, TenantContext tenant, LeaveService leave) : PageModel, ITitled
{
    public string Title => "오늘 할 일";
    public string Slug => tenant.Current.Slug;
    public TenantContext Tenant => tenant;
    public TodoBoard Board { get; private set; } = null!;
    [TempData] public string? Msg { get; set; }
    /// <summary>기다리는 탈퇴 신청(부서 관리자만, ADR 0119).</summary>
    public IReadOnlyList<LeaveRow> Leaves { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        Board = await todo.BoardAsync(ct);
        if (tenant.IsAdmin) Leaves = await leave.ListAsync(User.Identity!.Name!, tenant.Current.Id, ct);
        return Page();
    }

    /// <summary>부모님께 보낼 안내글(JSON): 링크가 없으면 만든다. 내 할 일 카드에 없는 선물이면 404.</summary>
    public async Task<IActionResult> OnPostShareAsync(long id, CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        if (await todo.ShareAsync(id, ct) is not { } r) return NotFound();
        var url = $"{Request.Scheme}://{Request.Host}/gift/{r.Token}";
        return new JsonResult(new { url, text = BirthdayGiftService.ShareText(r.View, url, r.Linked) });
    }

    /// <summary>탈퇴 승인 · 반려(R-343): 이 부서 관리자만, 이 부서 신청만.</summary>
    public async Task<IActionResult> OnPostLeaveAsync(int leaveId, string? leaveDo, CancellationToken ct)
    {
        if (tenant.IsLobby || !tenant.IsAdmin) return Forbid();
        var me = User.Identity!.Name!;
        if (leaveDo == "ok")
        {
            var e = await leave.ApproveAsync(me, leaveId, tenant.Current.Id, ct);
            Msg = e == LeaveError.None ? "탈퇴를 승인했어요. 계정과 기록을 지웠어요." : LeaveService.Message(e);
        }
        else Msg = await leave.RejectAsync(me, leaveId, tenant.Current.Id, ct) ? "탈퇴 신청을 반려했어요." : LeaveService.Message(LeaveError.NotFound);
        return Redirect($"/{Slug}/todo");
    }

    public async Task<IActionResult> OnPostSnoozeAsync(int days, CancellationToken ct)
    {
        await todo.SnoozeAsync(days, ct);
        return Redirect($"/{Slug}");
    }
}
