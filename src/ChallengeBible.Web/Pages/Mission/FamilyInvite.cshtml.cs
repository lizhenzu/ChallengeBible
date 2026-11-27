using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Mission;

/// <summary>
/// `/{slug}/family-invite/{id}` 팀 초대 · 참여하기(2026-11-27 사용자 민트 시안 「팀 초대 · 참여하기」).
/// 받은 초대 하나를 보여 주고 [참여하기](초대한 사람이 정해 준 관계로 합류) · [다음에 할게요](오늘은 숨김, R-212).
/// 끝나면 같은 주소에서 「참여했어요」 · 「나중에 볼게요」 화면을 한 번 보여 준다. 팀 규칙(한 곳만 · 친구팀은 학생만)은 서비스가 지킨다.
/// </summary>
public class FamilyInviteModel(FamilyJoinService join, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "팀 초대";
    public string Slug => tenant.Current.Slug;
    public FamilyReceivedInvite? Invite { get; private set; }
    /// <summary>pending(초대 보기) · joined(참여 완료) · later(나중에 보기).</summary>
    public string Step { get; private set; } = "pending";
    public string? JoinedTeam { get; private set; }
    public string? Error { get; private set; }
    [TempData] public string? Done { get; set; }

    public string Label(bool friend) => friend ? "친구팀" : "가족팀";

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        var state = await join.StateAsync(ct);
        if (!state.Enabled) return NotFound();
        if (Done is { } done)
        {
            Step = done.StartsWith("joined:") ? "joined" : "later";
            JoinedTeam = done.StartsWith("joined:") ? done["joined:".Length..] : null;
            return Page();
        }
        Invite = state.Invites.FirstOrDefault(i => i.InviteId == id);
        if (Invite is null) return Redirect($"/{Slug}/word-mission");   // 이미 처리했거나 지워진 초대
        return Page();
    }

    public async Task<IActionResult> OnPostAcceptAsync(int id, string? relation, CancellationToken ct)
    {
        var state = await join.StateAsync(ct);
        if (!state.Enabled) return NotFound();
        var inv = state.Invites.FirstOrDefault(i => i.InviteId == id);
        if (inv is null) return Redirect($"/{Slug}/word-mission");
        var e = await join.AcceptInviteAsync(id, relation ?? inv.Relation, ct);
        if (e == FamilyError.Disabled) return NotFound();
        if (e != FamilyError.None) { Invite = inv; Error = FamilyTeamService.Message(e); return Page(); }
        Done = "joined:" + inv.TeamName;
        return Redirect($"/{Slug}/family-invite/{id}");
    }

    public async Task<IActionResult> OnPostLaterAsync(int id, CancellationToken ct)
    {
        var e = await join.SnoozeInvitesAsync(ct);
        if (e == FamilyError.Disabled) return NotFound();
        Done = "later";
        return Redirect($"/{Slug}/family-invite/{id}");
    }
}
