using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Pages.Messages;

/// <summary>
/// `/messages/write` 단체쪽지(R-359, 2026-11-21): 교사 · 임원은 내 부서 전체에게, 학생 · 학부모는 답장만. 한 사람에게는 인명 검색의 [쪽지](R-358).
/// 플랫폼 관리자는 받는 사람을 [한 사람 · 모든 사용자 · 부서]로 — 전체 쪽지는 한 줄(R-327).
/// </summary>
[Authorize]
public class WriteModel(MessageService messages, ISystemDb systemDb, TenantResolver resolver, TenantContext tenant) : PageModel, ITitled
{
    /// <summary>부서가 있으면 ☰ 메뉴판 머리말(R-360).</summary>
    public bool HasDept { get; private set; }
    public TenantContext Tenant => tenant;
    public string Title => "단체쪽지";
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
    /// <summary>단체쪽지를 보낼 수 있는 부서(R-359) — 교사 · 임원인 부서. 비면 학생 · 학부모라 답장만.</summary>
    public List<(int Id, string Name, int Count)> GroupDepts { get; private set; } = [];
    /// <summary>단체쪽지를 보낼 부서(여럿이면 고른다).</summary>
    [BindProperty] public int? Tid { get; set; }
    /// <summary>한 사람에게 처음 보내기는 플랫폼 관리자만 — 나머지는 인명 검색의 [쪽지](R-358 · R-359).</summary>
    public bool CanPickOne => IsPlatformAdmin;
    private string Me => User.UserName()!;

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    private async Task LoadAsync(CancellationToken ct)
    {
        HasDept = await MsgDept.UseAsync(HttpContext, messages, resolver, ct);
        if (Reply is long rid && await messages.OpenAsync(Me, rid, ct) is { ReplyTo: { } rt }) To = rt;
        if (To is not null) Picked = await messages.PersonAsync(Me, To, ct);
        if (CanPickOne) Found = await messages.SearchAsync(Me, Q, ct);
        if (!IsPlatformAdmin && Reply is null) { GroupDepts = await messages.GroupDeptsAsync(Me, ct); Tid ??= GroupDepts.FirstOrDefault().Id; }
        if (IsPlatformAdmin)
        {
            await using var ctx = systemDb.Create(Me);
            Depts = (await ctx.Tenants.AsNoTracking().Where(t => !t.IsLobby && t.Status == TenantStatus.Active).Select(t => new { t.Id, t.Name }).ToListAsync(ct))
                .OrderBy(t => DeptOrder.Key(t.Name)).Select(t => (t.Id, MessageService.ShortDept(t.Name, false))).ToList();
        }
        else Mode = Reply is null ? "group" : "one";
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
        else if (!IsPlatformAdmin && Reply is null)   // 단체쪽지(R-359): 교사 · 임원인 부서 전체에게
            e = Tid is int tid ? await messages.GroupAsync(Me, tid, Body, ct) : MsgError.NotAllowed;
        else if (!CanPickOne && Reply is long rid0 && await messages.OpenAsync(Me, rid0, ct) is { ReplyTo: { } back })
            e = await messages.SendAsync(Me, back, Body, Reply, ct);   // 답장은 받은 쪽지를 보낸 사람에게만
        else e = CanPickOne ? await messages.SendAsync(Me, To, Body, Reply, ct) : MsgError.NotAllowed;
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
