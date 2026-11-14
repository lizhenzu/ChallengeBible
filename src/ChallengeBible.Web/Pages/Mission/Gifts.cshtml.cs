using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Mission;

/// <summary>
/// 개인 선물 고르기(확정 시안 2판 docs/design/시안/말씀-선물고르기-시안.png, ADR 0094 B · 정한 것 8, R-280 ~ R-285).
/// 받기 전 선물이 있으면 그 선물(⑤ 고른 뒤), 없으면 고르기(③). 고르기 확인(④)은 화면 안 창, 스크립트가 없으면 바로 보낸다.
/// </summary>
public class GiftsModel(PersonalGiftService gifts, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "선물 고르기";
    public string Slug => tenant.Current.Slug;
    public GiftWallet? Wallet { get; private set; }
    public IReadOnlyList<PickItem> Items { get; private set; } = [];
    [BindProperty(SupportsGet = true)] public bool All { get; set; }
    [TempData] public string? Msg { get; set; }

    public IEnumerable<PickItem> Shown => All ? Items : Items.Where(i => i.Need <= (Wallet?.Usable ?? 0));
    public int CanCount => Items.Count(i => i.Need <= (Wallet?.Usable ?? 0));

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!tenant.Current.MissionEnabled) return NotFound();
        Wallet = await gifts.WalletAsync(ct);
        if (Wallet is { Student: true, Active: null }) Items = await gifts.ItemsAsync(Wallet, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostPickAsync(int item, CancellationToken ct)
    {
        if (!tenant.Current.MissionEnabled) return NotFound();
        var (result, _) = await gifts.PickAsync(item, ct);
        Msg = result switch
        {
            PickResult.Ok => "picked",
            PickResult.NotEnough => "short",
            PickResult.AlreadyPicked => "already",
            PickResult.NotFound => "gone",
            _ => "busy",
        };
        return Redirect($"/{Slug}/gifts");
    }

    public async Task<IActionResult> OnPostCancelAsync(long id, CancellationToken ct)
    {
        if (!tenant.Current.MissionEnabled) return NotFound();
        Msg = await gifts.CancelAsync(id, ct) ? "cancelled" : "nocancel";
        return Redirect($"/{Slug}/gifts");
    }

    public string? MsgText => Msg switch
    {
        "picked" => "선물을 골랐어요! 선생님이 준비할 거예요.",
        "short" => "점수가 모자라요. 조금 더 모아 볼까요?",
        "already" => "이미 고른 선물이 있어요. 받은 뒤에 다음 선물을 골라요.",
        "gone" => "그 선물은 지금 고를 수 없어요.",
        "busy" => "잠시 뒤에 다시 해 주세요.",
        "cancelled" => "선물을 취소했어요. 점수가 돌아왔어요.",
        "nocancel" => "선생님이 준비한 뒤에는 취소할 수 없어요.",
        _ => null,
    };

    public static string Img(int? id, DateTime? v = null) => $"/images/AwardItem/{id}.jpg" + (v is { } t ? $"?v={t.Ticks}" : "");
}
