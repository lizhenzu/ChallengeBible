using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Pages.Messages;

/// <summary>
/// `/messages/write` 쪽지 쓰기(확정 시안 쪽지 ④ ⑦): 이름으로 찾아 고르기(이름 · 부서 · 역할만) · 답장(받는 사람이 정해짐).
/// 플랫폼 관리자는 받는 사람을 [한 사람 · 모든 사용자 · 부서]로 — 전체 쪽지는 한 줄(R-327).
/// </summary>
[Authorize]
public class WriteModel(MessageService messages, ISystemDb systemDb) : PageModel, ITitled
{
    public string Title => "쪽지 쓰기";
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public long? Reply { get; set; }
    [BindProperty] public string? To { get; set; }
    [BindProperty] public string? Body { get; set; }
    [BindProperty(SupportsGet = true)] public string Mode { get; set; } = "one";
    [BindProperty] public List<int> Tids { get; set; } = [];
    public List<MsgPerson> Found { get; private set; } = [];
    public MsgPerson? Picked { get; private set; }
    public string? Error { get; private set; }
    public bool IsPlatformAdmin => User.IsPlatformAdmin();
    public List<(int Id, string Name)> Depts { get; private set; } = [];
    private string Me => User.UserName()!;

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    private async Task LoadAsync(CancellationToken ct)
    {
        if (Reply is long rid && await messages.OpenAsync(Me, rid, ct) is { ReplyTo: { } rt }) To = rt;
        if (To is not null) Picked = await messages.PersonAsync(Me, To, ct);
        Found = await messages.SearchAsync(Me, Q, ct);
        if (IsPlatformAdmin)
        {
            await using var ctx = systemDb.Create(Me);
            Depts = (await ctx.Tenants.AsNoTracking().Where(t => !t.IsLobby && t.Status == TenantStatus.Active).Select(t => new { t.Id, t.Name }).ToListAsync(ct))
                .OrderBy(t => DeptOrder.Key(t.Name)).Select(t => (t.Id, MessageService.ShortDept(t.Name, false))).ToList();
        }
        else Mode = "one";
    }

    /// <summary>받는 사람 수(확정 시안 쪽지 ⑦ "124명에게 가요").</summary>
    public async Task<IActionResult> OnGetCountAsync([FromQuery] List<int> tids, CancellationToken ct) =>
        IsPlatformAdmin ? new JsonResult(new { n = await messages.AudienceCountAsync(Me, tids, ct) }) : Forbid();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        MsgError e;
        if (IsPlatformAdmin && Mode is "all" or "dept")
        {
            if (Mode == "dept" && Tids.Count == 0) e = MsgError.NoRecipient;
            else e = await messages.BroadcastAsync(Me, Mode == "all" ? [] : Tids, Body, ct);
        }
        else e = await messages.SendAsync(Me, To, Body, Reply, ct);
        if (e == MsgError.None)
        {
            TempData["Msg"] = "보냈어요.";
            return Redirect("/messages?box=sent");
        }
        Error = MessageService.Text(e);
        await LoadAsync(ct);
        return Page();
    }
}
