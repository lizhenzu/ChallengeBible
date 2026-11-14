using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>
/// `/platform/gifts?s=&amp;q=&amp;p=&amp;r=&amp;n=` 선물 상품 목록(확정 시안 플랫폼-선물상품 B 줄 목록, ADR 0094 A · R-276 ~ R-279).
/// 판매 중 · 삭제 대상 · 목록에서 내림 탭, 이름 · 번호 찾기, 가격대, 삭제 대상 까닭.
/// </summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class GiftsModel(GiftCatalogService catalog) : PageModel, ITitled
{
    public string Title => "공통 상품";

    [BindProperty(SupportsGet = true, Name = "s")] public string? Status { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Q { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public string? Band { get; set; }
    [BindProperty(SupportsGet = true, Name = "r")] public string? Reason { get; set; }
    [BindProperty(SupportsGet = true, Name = "n")] public int Take { get; set; } = GiftCatalogService.PageSize;

    public IReadOnlyList<GiftItemRow> Rows { get; private set; } = [];
    public bool More { get; private set; }
    public GiftItemCounts Counts { get; private set; } = new(0, 0, 0, []);

    /// <summary>가격대(시안): 값 → (이름, 최소, 최대).</summary>
    public static readonly IReadOnlyList<(string Key, string Label, int? Min, int? Max)> Bands =
    [
        ("", "모든 가격", null, null), ("5k", "~ 5,000원", null, 5000), ("10k", "5,001 ~ 10,000원", 5001, 10000),
        ("20k", "10,001 ~ 20,000원", 10001, 20000), ("up", "20,001원 ~", 20001, null),
    ];

    public async Task OnGetAsync(CancellationToken ct)
    {
        var me = User.UserName()!;
        Status = Status is GiftItemStatus.Review or GiftItemStatus.Retired ? Status : GiftItemStatus.Active;
        Take = Math.Clamp(Take, GiftCatalogService.PageSize, 2000);
        var band = Bands.FirstOrDefault(b => b.Key == (Band ?? ""));
        Counts = await catalog.CountsAsync(me, ct);
        (Rows, More) = await catalog.ListAsync(me, Status, Q, band.Min, band.Max, Status == GiftItemStatus.Review ? Reason : null, Take, ct);
    }

    public string Url(string? s = null, string? r = null, int? n = null) =>
        $"/platform/gifts?s={s ?? Status}&q={Uri.EscapeDataString(Q ?? "")}&p={Band}&r={Uri.EscapeDataString(r ?? Reason ?? "")}&n={n ?? GiftCatalogService.PageSize}";

    public static string Img(GiftItemRow r) => r.HasImage ? $"/images/AwardItem/{r.Id}.jpg?v={r.UpdatedUtc.Ticks}" : "";
}
