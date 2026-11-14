using ChallengeBible.Web.Pages.Mission;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace ChallengeBible.Web.Pages;

/// <summary>
/// `/gift/{token}` 로그인 없는 선물 링크(확정 시안 생일-선물 ③, R-287 · 정한 것 3): 32자 무작위 열쇠만으로 연다. 화면에는 아이 이름 · 선물 종류만.
/// 구매 완료 전까지 바꾸고, 뒤에는 "준비 중이에요". 같은 접속 주소가 짧은 시간에 많이 열면 잠시 막는다(GiftLinkLimit).
/// </summary>
[AllowAnonymous]
[EnableRateLimiting(GiftLinkModel.Limit)]
public class GiftLinkModel(BirthdayGiftService gifts) : PageModel, ITitled
{
    public const string Limit = "gift-link";
    public string Title => View is null ? "선물" : $"{View.Gift.Name} 어린이 생일 선물";
    [BindProperty(SupportsGet = true)] public string Token { get; set; } = "";
    [BindProperty(SupportsGet = true)] public bool Change { get; set; }
    [TempData] public string? Msg { get; set; }
    public BirthdayPickView? View { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var g = await gifts.ByTokenAsync(Token, ct);
        if (g is null) return Page();   // 없는 링크 · 새 링크로 막힌 링크: "열 수 없는 링크예요"
        var items = g.State == "Open" || (Change && g.State == "Picked") ? await gifts.ItemsByTokenAsync(g, ct) : [];
        View = new BirthdayPickView(g, items, Change, $"/gift/{Token}", BirthdayGiftModel.MsgText(Msg), Link: true);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int item, CancellationToken ct)
    {
        var e = await gifts.PickByTokenAsync(Token, item, ct);
        if (e == TeamPickError.NotFound) return Redirect($"/gift/{Token}");
        Msg = BirthdayGiftModel.Code(e);
        return Redirect($"/gift/{Token}");
    }
}
