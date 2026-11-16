using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/prayer365/edit?md=0101` 기도문 고치기 · 검수(ADR 0106). 비어 있는 날은 샘플을 채운 채로 연다.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class Prayer365EditModel(Prayer365Service prayers) : PageModel, ITitled
{
    public string Title => "365 기도문 고치기";
    [BindProperty(SupportsGet = true)] public string? Md { get; set; }
    public ChallengeBible.Infrastructure.Data.Prayer365 Item { get; private set; } = null!;
    public bool Missing { get; private set; }
    [TempData] public string? Msg { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!Prayer365Service.TryParse(Md, out _, out _)) return Redirect("/platform/prayer365");
        var row = await prayers.FindAsync(User.UserName()!, Md!, ct);
        Missing = row is null;
        Item = row ?? Prayer365Service.Sample(Md!);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string title, string? verseRef, string? verseText, string body, bool reviewed, CancellationToken ct)
    {
        if (!Prayer365Service.TryParse(Md, out _, out _)) return Redirect("/platform/prayer365");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body)) { Msg = "제목과 본문을 써 주세요."; return Redirect($"/platform/prayer365/edit?md={Md}"); }
        await prayers.SaveAsync(User.UserName()!, Md!, title, verseRef, verseText, body, reviewed, ct);
        Msg = "저장했어요.";
        return Redirect($"/platform/prayer365/edit?md={Md}");
    }
}
