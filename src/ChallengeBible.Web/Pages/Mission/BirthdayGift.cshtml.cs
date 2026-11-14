using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Mission;

/// <summary>`/{slug}/gifts/birthday/{id}` 생일 · 새친구 · 전도 선물 고르기(확정 시안 생일-선물 ② · 전도-선물, R-288 · R-286). 내 선물만, 금액 · 가격은 보이지 않는다.</summary>
public class BirthdayGiftModel(BirthdayGiftService gifts, NewcomerGiftService newcomers, ReadingGiftService readings, TenantContext tenant) : PageModel, ITitled
{
    public string Title => View is null ? "선물 고르기" : $"{View.Gift.KindLabel} 고르기";
    public string Slug => tenant.Current.Slug;
    /// <summary>← 돌아갈 곳: 완독 선물은 통독 홈, 나머지는 미션 홈(미션을 안 쓰는 부서도 통독 홈).</summary>
    public (string Href, string Label) Back => View?.Gift.Kind == "Reading" || !tenant.Current.MissionEnabled ? ($"/{Slug}/reading", "← 통독 홈") : ($"/{Slug}/word-mission", "← 미션 홈");
    [BindProperty(SupportsGet = true)] public long Id { get; set; }
    [BindProperty(SupportsGet = true)] public bool Change { get; set; }
    [TempData] public string? Msg { get; set; }
    public BirthdayPickView View { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!tenant.Current.MissionEnabled && !tenant.Current.ReadingEnabled) return NotFound();
        await gifts.EnsureAsync(ct);
        await newcomers.EnsureAsync(ct);
        await readings.EnsureAsync(ct);
        var g = (await gifts.MineAsync(ct)).FirstOrDefault(x => x.Id == Id);
        if (g is null) return NotFound();
        var items = g.State == "Open" || (Change && g.State == "Picked") ? await gifts.ItemsAsync(g, ct) : [];
        View = new BirthdayPickView(g, items, Change, $"/{Slug}/gifts/birthday/{Id}", MsgText(Msg), Link: false);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int item, CancellationToken ct)
    {
        if (!tenant.Current.MissionEnabled && !tenant.Current.ReadingEnabled) return NotFound();
        var e = await gifts.PickAsync(Id, item, ct);
        if (e is TeamPickError.NotMember or TeamPickError.NotFound) return NotFound();
        Msg = Code(e);
        return Redirect($"/{Slug}/gifts/birthday/{Id}");
    }

    public static string Code(TeamPickError e) => e switch { TeamPickError.None => "picked", TeamPickError.Closed => "closed", _ => "gone" };

    public static string? MsgText(string? code) => code switch
    {
        "picked" => "선물을 골랐어요! 선생님이 준비할 거예요.",
        "closed" => "지금은 고르거나 바꿀 수 없어요. 선생님이 이미 준비했거나 기한이 지났어요.",
        "gone" => "그 선물은 지금 고를 수 없어요.",
        _ => null,
    };
}
