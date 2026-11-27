using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Messages;

/// <summary>`/messages` 쪽지함(ADR 0112, 확정 시안 쪽지 ② ③ ⑧): 받은 쪽지 · 보낸 쪽지, 지우기, 전체 쪽지 [보내기 취소], 부서 홈 접수대(개선 · 불편) 보내기.</summary>
[Authorize]
public class IndexModel(MessageService messages, TenantResolver resolver, TenantContext tenant) : PageModel, ITitled
{
    /// <summary>부서가 있으면 ☰ 메뉴판 머리말(R-360).</summary>
    public bool HasDept { get; private set; }
    public TenantContext Tenant => tenant;
    public string Title => "단체쪽지";
    /// <summary>[단체쪽지 쓰기]는 교사 · 임원(보낼 부서가 있을 때) · 플랫폼 관리자만(R-359).</summary>
    public bool CanWrite { get; private set; }
    [BindProperty(SupportsGet = true)] public string Box { get; set; } = "in";
    [TempData] public string? Msg { get; set; }
    public List<MsgRow> Rows { get; private set; } = [];
    public int Unread { get; private set; }
    public bool IsPlatformAdmin => User.IsPlatformAdmin();
    private string Me => User.UserName()!;

    public async Task OnGetAsync(CancellationToken ct)
    {
        HasDept = await MsgDept.UseAsync(HttpContext, messages, resolver, ct);
        Box = Box == "sent" ? "sent" : "in";
        Unread = await messages.UnreadAsync(Me, ct);
        CanWrite = IsPlatformAdmin || (await messages.GroupDeptsAsync(Me, ct)).Count > 0;
        Rows = Box == "sent" ? await messages.SentAsync(Me, ct) : await messages.InboxAsync(Me, ct);
    }

    public async Task<IActionResult> OnPostDeleteAsync(long id, string? box, CancellationToken ct)
    {
        Msg = await messages.DeleteAsync(Me, id, ct) == MsgError.None ? "지웠어요(내 쪽지함에서만)." : MessageService.Text(MsgError.NotFound);
        return Redirect("/messages" + (box == "sent" ? "?box=sent" : ""));
    }

    public async Task<IActionResult> OnPostCancelAsync(long id, CancellationToken ct)
    {
        var e = await messages.CancelAsync(Me, id, ct);
        Msg = e == MsgError.None ? "보내기를 취소했어요. 모두의 쪽지함에서 사라졌어요." : MessageService.Text(e);
        return Redirect("/messages?box=sent");
    }

    /// <summary>부서 홈 개선 · 불편 접수대(확정 시안 쪽지 ①): superadmin 에게. 익명이면 부서만.</summary>
    public async Task<IActionResult> OnPostFeedbackAsync(int tid, string? body, bool anon, string? back, CancellationToken ct)
    {
        var e = await messages.FeedbackAsync(Me, tid, body, anon, ct);
        TempData["fb"] = e == MsgError.None ? (anon ? "익명으로 보냈어요. 고마워요!" : "플랫폼 관리자에게 보냈어요. 답장은 쪽지함에서 볼 수 있어요.") : MessageService.Text(e);
        var url = back is { Length: > 0 } b && Url.IsLocalUrl(b) ? b : "/";
        return Redirect(url + "#fbT");
    }
}
