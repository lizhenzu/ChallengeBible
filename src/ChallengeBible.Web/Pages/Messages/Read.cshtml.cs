using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Messages;

/// <summary>`/messages/{id}` 쪽지 읽기(확정 시안 쪽지 ⑤ ⑨): 받은 사람이 열면 읽음. [답장] · [지우기].</summary>
[Authorize]
public class ReadModel(MessageService messages, TenantResolver resolver, TenantContext tenant) : PageModel, ITitled
{
    /// <summary>부서가 있으면 ☰ 메뉴판 머리말(R-360).</summary>
    public bool HasDept { get; private set; }
    public TenantContext Tenant => tenant;
    public string Title => "단체쪽지";
    public MsgRow Row { get; private set; } = null!;
    public bool Mine { get; private set; }
    /// <summary>[보내기 취소]를 눌렀을 때 한 번 더 묻는 상자(주소 ?ask=cancel — 스크립트 없이도 된다).</summary>
    [FromQuery(Name = "ask")] public string? Ask { get; set; }
    public bool AskCancel => Ask == "cancel" && Mine && Row.Broadcast && !Row.Canceled;

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken ct)
    {
        var me = User.UserName()!;
        HasDept = await MsgDept.UseAsync(HttpContext, messages, resolver, ct);
        var r = await messages.OpenAsync(me, id, ct);
        if (r is null) return NotFound();
        Row = r;
        Mine = (await messages.SentAsync(me, ct)).Any(x => x.Id == id);
        return Page();
    }
}
