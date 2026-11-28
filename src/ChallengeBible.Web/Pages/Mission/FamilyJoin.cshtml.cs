using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Mission;

/// <summary>
/// `/{slug}/family-join/{sent|result}` 합류 신청 흐름(2026-11-28 시안 JoinA9 ~ A12, 알림 띠 JoinA1 ~ A8).
/// sent = 신청 확인(「신청을 보냈어요」), result = 결과(합류 · 거절은 저장된 알림을 한 번 읽고 지운다, 취소는 `?k=cancel`).
/// 알림 띠(미션홈 · 팀보기)의 수락 · 거절 · 확인 · 다음에 할게요 · 신청 취소는 여기 POST 로 처리하고 `ret`(home · team · manage)로 돌아간다.
/// 수락 · 거절 규칙은 서비스가 지킨다(팀 구성원 누구나, 선물 받은 팀은 잠김, 친구팀 2명).
/// </summary>
public class FamilyJoinModel(FamilyJoinService join, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "합류 신청";
    public string Slug => tenant.Current.Slug;
    /// <summary>sent · accepted · rejected · cancelled</summary>
    public string Step { get; private set; } = "sent";
    public string TeamName { get; private set; } = "";
    public int TeamPoints { get; private set; }

    public async Task<IActionResult> OnGetAsync(string view, string? k, CancellationToken ct)
    {
        var state = await join.StateAsync(ct);
        if (!state.Enabled) return NotFound();
        if (view == "sent")
        {
            if (state.Pending is not { } p) return Redirect(Home);
            Step = "sent"; TeamName = p.TeamName;
            return Page();
        }
        if (k == "cancel") { Step = "cancelled"; return Page(); }
        var r = await join.TakeResultAsync(ct);
        if (r is null) return Redirect(Home);
        Step = r.Kind == "ACCEPTED" ? "accepted" : "rejected";
        TeamName = r.TeamName;
        TeamPoints = state.Mine?.Total ?? 0;
        return Page();
    }

    private string Home => $"/{Slug}/word-mission";
    private string Back(string? ret) => ret switch { "team" => $"/{Slug}/family-team", "manage" => $"/{Slug}/family-team/manage", _ => Home };

    /// <summary>fetch 로 부른 것(수락 줄 「처리 중… → 합류했어요 ✓」)은 JSON 으로 답한다.</summary>
    private bool Ajax => Request.Headers["X-Requested-With"] == "fetch";
    private IActionResult Done(FamilyError e, string? ret)
    {
        if (e == FamilyError.Disabled) return NotFound();
        if (Ajax) return new JsonResult(new { ok = e == FamilyError.None, message = e == FamilyError.None ? "" : FamilyTeamService.Message(e) });
        return Redirect(Back(ret));
    }

    public async Task<IActionResult> OnPostAcceptAsync(int request, string? ret, CancellationToken ct) => Done(await join.AcceptAsync(request, ct), ret);
    public async Task<IActionResult> OnPostRejectAsync(int request, string? ret, CancellationToken ct) => Done(await join.RejectAsync(request, ct), ret);
    /// <summary>이미 다른 팀에 든 사람의 신청을 치운다(「확인」). 거절 알림은 보내지 않는다.</summary>
    public async Task<IActionResult> OnPostDismissAsync(int request, string? ret, CancellationToken ct) => Done(await join.RejectAsync(request, false, ct), ret);
    public async Task<IActionResult> OnPostDismissResultAsync(string? ret, CancellationToken ct) => Done(await join.DismissResultAsync(ct), ret);
    public async Task<IActionResult> OnPostLaterAsync(string? ret, CancellationToken ct) => Done(await join.SnoozeInvitesAsync(ct), ret);

    public async Task<IActionResult> OnPostCancelAsync(CancellationToken ct)
    {
        var e = await join.CancelAsync(ct);
        if (e == FamilyError.Disabled) return NotFound();
        return Redirect($"/{Slug}/family-join/result?k=cancel");
    }
}
