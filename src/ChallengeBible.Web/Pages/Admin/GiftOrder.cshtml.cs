using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>
/// `/{slug}/admin/gifts/{id}` 선물 한 건 자세히(확정 시안 관리-선물처리 ③, R-295): 받는 사람 · 가격 · 쓴 점수 · 쿠팡 링크 · 이력,
/// [샀어요] · [줬어요] · [되돌리기] · [선물 취소(점수 돌려주기)](R-284 · R-293). 부서 관리자만.
/// </summary>
[TenantRoles(Roles.TenantAdmin)]
public class GiftOrderModel(GiftOrderService orders, TeamGiftService teamGifts, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "선물 자세히";
    public TenantContext Tenant => tenant;
    [BindProperty(SupportsGet = true)] public long Id { get; set; }
    [BindProperty(SupportsGet = true, Name = "ok")] public string? Ok { get; set; }
    public GiftOrderRow? Row { get; private set; }
    public IReadOnlyList<GiftLogRow> Logs { get; private set; } = [];
    private string Me => User.UserName()!;
    private string Self => $"/{tenant.Current.Slug}/admin/gifts/{Id}";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        (Row, Logs) = await orders.GetAsync(Id, ct);
        return Row is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostMoveAsync(string act, CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        var e = act is GiftActions.Purchase or GiftActions.Deliver or GiftActions.Undo ? await orders.MoveAsync(Me, Id, act, ct) : GiftOrderError.BadState;
        return e == GiftOrderError.NotFound ? NotFound() : Redirect($"{Self}?ok={(e == GiftOrderError.None ? act : "bad")}");
    }

    public async Task<IActionResult> OnPostReopenAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        return Redirect($"{Self}?ok={(await teamGifts.ReopenAsync(Me, Id, ct) ? "reopen" : "bad")}");
    }

    public async Task<IActionResult> OnPostCancelAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        var e = await orders.CancelAsync(Me, Id, ct);
        return e == GiftOrderError.NotFound ? NotFound() : Redirect($"{Self}?ok={e switch { GiftOrderError.None => "cancel", GiftOrderError.LastYear => "lastyear", _ => "bad" }}");
    }

    public string? Message => Ok switch
    {
        "Purchase" => "샀어요로 바꿨어요.", "Deliver" => "줬어요로 바꿨어요.", "Undo" => "한 단계 되돌렸어요.", "cancel" => "선물을 취소하고 점수를 돌려줬어요.",
        "reopen" => "다시 열었어요. 7일 동안 고를 수 있어요.", "bad" => "이미 바뀐 선물이에요.", "lastyear" => "지난해 선물은 되돌릴 수 없어요.", _ => null,
    };

    public static string StateLabel(string s) => s switch
    {
        GiftStates.Picked => "살 것", GiftStates.Purchased => "줄 것", GiftStates.Delivered => "받음", GiftStates.Cancelled => "취소",
        GiftStates.YearClosed => "해 넘어 닫힘", GiftStates.Open => "고를 수 있음", GiftStates.Expired => "기한 지남", GiftStates.Skipped => "건너뜀", _ => s,
    };

    /// <summary>이력 한 줄의 동작(이름은 앞에 따로 — "윤동호 · 샀어요", 사용자 2026-11-14).</summary>
    public static string LogText(GiftLogRow l) => l.Action switch
    {
        GiftActions.Pick => "골랐어요", GiftActions.Change => "바꿨어요", GiftActions.Cancel => "취소했어요",
        GiftActions.Purchase => "샀어요", GiftActions.Deliver => "줬어요", GiftActions.Undo => "되돌렸어요",
        GiftActions.Reopen => "다시 열었어요", GiftActions.Skip => "건너뛰었어요", GiftActions.Expire => "기한이 지났어요",
        GiftActions.Open => "선물이 생겼어요", GiftActions.YearClose => "새해에 닫혔어요", GiftActions.Link => "링크를 만들었어요", _ => l.Action,
    };

    /// <summary>사람 없이 생기는 동작(선물이 생김 · 기한 지남 · 새해 닫힘)은 이름을 붙이지 않는다.</summary>
    public static bool NoActor(GiftLogRow l) => l.Action is GiftActions.Open or GiftActions.Expire or GiftActions.YearClose;
}
