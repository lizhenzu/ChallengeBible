using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Messages;

/// <summary>`/messages/{id}` 쪽지 읽기(확정 시안 쪽지 ⑤ ⑨): 받은 사람이 열면 읽음. [답장] · [지우기].</summary>
[Authorize]
public class ReadModel(MessageService messages) : PageModel, ITitled
{
    public string Title => "쪽지";
    public MsgRow Row { get; private set; } = null!;
    public bool Mine { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken ct)
    {
        var me = User.UserName()!;
        var r = await messages.OpenAsync(me, id, ct);
        if (r is null) return NotFound();
        Row = r;
        Mine = (await messages.SentAsync(me, ct)).Any(x => x.Id == id);
        return Page();
    }
}
