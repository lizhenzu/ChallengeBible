using ChallengeBible.Domain.Rules;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>
/// `/{slug}/admin/gifts?t=orders|list|ours|money` 선물 처리(구매 · 전달, 확정 시안 관리-선물처리) + 부서 선물 설정(확정 시안 관리-선물, ADR 0094): ① 선물 목록(공통 숨기기 R-276) ② 우리 부서 상품(공통에 올리기 R-277)
/// ③ 금액 · 점수(R-280 · R-283 · R-286 · R-288 · R-289). 부서 관리자만, 임시 부서는 없음.
/// </summary>
[TenantRoles(Roles.TenantAdmin)]
public class GiftsModel(DeptGiftService gifts, GiftCatalogService catalog, GiftOrderService orders, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "선물";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true, Name = "t")] public string? Tab { get; set; }
    [BindProperty(SupportsGet = true, Name = "v")] public string? Show { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Q { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public string? Band { get; set; }
    [BindProperty(SupportsGet = true, Name = "n")] public int Take { get; set; } = GiftCatalogService.PageSize;
    [BindProperty(SupportsGet = true, Name = "ok")] public string? Ok { get; set; }
    [BindProperty] public DeptGiftMoneyInput Money { get; set; } = new();
    /// <summary>선물 처리 탭의 칩: buy 살 것 · give 줄 것 · done 받음 · cancel 취소.</summary>
    [BindProperty(SupportsGet = true, Name = "s")] public string? Step { get; set; }
    public IReadOnlyList<GiftOrderRow> Orders { get; private set; } = [];
    public GiftOrderCounts OrderCounts { get; private set; } = new(0, 0, 0, 0);

    public IReadOnlyList<DeptGiftRow> Rows { get; private set; } = [];
    public bool More { get; private set; }
    public DeptGiftCounts Counts { get; private set; } = new(0, 0, 0);
    public IReadOnlyList<DeptGiftRow> Ours { get; private set; } = [];
    public GiftRules Rules { get; private set; } = GiftRules.Default;
    public Dictionary<string, string> Errors { get; private set; } = [];
    public string? Message { get; private set; }

    private string Slug => tenant.Current.Slug;
    private string Me => User.UserName()!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        Tab = Tab is "list" or "ours" or "money" ? Tab : "orders";   // 처음은 선물 처리(확정 시안 관리-선물처리)
        Message = Ok switch
        {
            "money" => "저장했어요. 앞으로 생기는 선물부터 적용돼요.", "shared" => "공통에 올렸어요. 이제 모든 부서에 보여요.",
            "Purchase" => "샀어요로 바꿨어요. \"줄 것\"에 있어요.", "Deliver" => "줬어요로 바꿨어요.", "Undo" => "한 단계 되돌렸어요.", "cancel" => "선물을 취소하고 점수를 돌려줬어요.",
            "bad" => "이미 바뀐 선물이에요. 목록을 다시 확인해 주세요.", "lastyear" => "지난해 선물은 되돌릴 수 없어요.", _ => null,
        };
        await LoadAsync(fillMoney: true, ct);
        return Page();
    }

    private async Task LoadAsync(bool fillMoney, CancellationToken ct)
    {
        Rules = await gifts.RulesAsync(ct);
        if (Tab == "orders")
        {
            Step = Step is GiftOrderService.Give or GiftOrderService.Done or GiftOrderService.Cancel ? Step : GiftOrderService.Buy;
            OrderCounts = await orders.CountsAsync(ct);
            Orders = await orders.ListAsync(Step, ct: ct);
        }
        else if (Tab == "list")
        {
            Take = Math.Clamp(Take, GiftCatalogService.PageSize, 2000);
            var band = Platform.GiftsModel.Bands.FirstOrDefault(b => b.Key == (Band ?? ""));
            Show = Show is "shown" or "hidden" ? Show : "all";
            (Rows, More, Counts) = await gifts.ListAsync(Show, Q, band.Min, band.Max, Take, ct);
        }
        else if (Tab == "ours") Ours = await gifts.OursAsync(ct);
        else if (fillMoney)
        {
            var m = await gifts.MoneyAsync(ct);
            static string? F(int? n) => n?.ToString("N0");
            Money = new DeptGiftMoneyInput
            {
                PersonalCap = F(m.PersonalCap), NewcomerMin = F(m.NewcomerMin), NewcomerMax = F(m.NewcomerMax), InviteMin = F(m.InviteMin), InviteMax = F(m.InviteMax),
                BirthdayMin = F(m.BirthdayMin), BirthdayMax = F(m.BirthdayMax),
                RateCredits = m.Rates.Select(r => (string?)r.Credits.ToString("N0")).ToList(), RateAmounts = m.Rates.Select(r => (string?)r.Amount.ToString("N0")).ToList(),
                Reading = m.Reading.Select(F).ToList(),
            };
        }
    }

    /// <summary>선물 처리 목록의 [샀어요] · [줬어요] · [되돌리기](R-293). 처리한 뒤 같은 칩으로 돌아간다.</summary>
    public async Task<IActionResult> OnPostMoveAsync(long id, string act, CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        var e = act is GiftActions.Purchase or GiftActions.Deliver or GiftActions.Undo ? await orders.MoveAsync(Me, id, act, ct) : GiftOrderError.BadState;
        if (e == GiftOrderError.NotFound) return NotFound();
        return Redirect($"/{Slug}/admin/gifts?s={Step ?? GiftOrderService.Buy}&ok={(e == GiftOrderError.None ? act : "bad")}");
    }

    /// <summary>① 공통 상품 숨기기 · 보이기(스위치를 누르면 바로).</summary>
    public async Task<IActionResult> OnPostHideAsync(int id, bool hide, CancellationToken ct)
    {
        if (tenant.IsLobby || !await gifts.SetHiddenAsync(Me, id, hide, ct)) return NotFound();
        return Redirect($"/{Slug}/admin/gifts?t=list&v={Uri.EscapeDataString(Show ?? "all")}&q={Uri.EscapeDataString(Q ?? "")}&p={Band}#g{id}");
    }

    /// <summary>② 공통에 올리기(R-277, 승인 없이 바로).</summary>
    public async Task<IActionResult> OnPostShareAsync(int id, CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        var r = await catalog.ShareAsync(GiftEditor.Dept(Me, tenant.Current.Id), id, ct);
        return r == GiftItemError.None ? Redirect($"/{Slug}/admin/gifts?t=ours&ok=shared") : NotFound();
    }

    /// <summary>③ 금액 · 점수 저장. 틀린 칸이 있으면 아무것도 저장하지 않고 쓴 값을 그대로 보인다.</summary>
    public async Task<IActionResult> OnPostMoneyAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        Tab = "money";
        Errors = await gifts.SaveMoneyAsync(Me, Money, ct);
        if (Errors.Count == 0) return Redirect($"/{Slug}/admin/gifts?t=money&ok=money");
        await LoadAsync(fillMoney: false, ct);
        return Page();
    }

    public string Err(string k) => Errors.GetValueOrDefault(k) ?? "";
    public string Url(string? v = null, int? n = null) => $"/{Slug}/admin/gifts?t=list&v={v ?? Show}&q={Uri.EscapeDataString(Q ?? "")}&p={Band}&n={n ?? GiftCatalogService.PageSize}";
    public static string Img(DeptGiftRow r) => r.HasImage ? $"/images/AwardItem/{r.Id}.jpg?v={r.UpdatedUtc.Ticks}" : "";
    public static string N(int n) => n.ToString("N0");
}
