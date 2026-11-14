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
public class GiftOrderModel(GiftOrderService orders, TenantContext tenant) : PageModel, ITitled
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

    public async Task<IActionResult> OnPostCancelAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        var e = await orders.CancelAsync(Me, Id, ct);
        return e == GiftOrderError.NotFound ? NotFound() : Redirect($"{Self}?ok={e switch { GiftOrderError.None => "cancel", GiftOrderError.LastYear => "lastyear", _ => "bad" }}");
    }

    public string? Message => Ok switch
    {
        "Purchase" => "샀어요로 바꿨어요.", "Deliver" => "줬어요로 바꿨어요.", "Undo" => "한 단계 되돌렸어요.", "cancel" => "선물을 취소하고 점수를 돌려줬어요.",
        "bad" => "이미 바뀐 선물이에요.", "lastyear" => "지난해 선물은 되돌릴 수 없어요.", _ => null,
    };

    public static string StateLabel(string s) => s switch
    {
        GiftStates.Picked => "살 것", GiftStates.Purchased => "줄 것", GiftStates.Delivered => "받음", GiftStates.Cancelled => "취소",
        GiftStates.YearClosed => "해 넘어 닫힘", GiftStates.Open => "고를 수 있음", GiftStates.Expired => "기한 지남", GiftStates.Skipped => "건너뜀", _ => s,
    };

    public static string LogText(GiftLogRow l, string who) => l.Action switch
    {
        GiftActions.Pick => $"{who}이(가) 골랐어요", GiftActions.Change => $"{who}이(가) 바꿨어요", GiftActions.Cancel => $"{who}이(가) 취소했어요",
        GiftActions.Purchase => $"{who}이(가) 샀어요", GiftActions.Deliver => $"{who}이(가) 줬어요", GiftActions.Undo => $"{who}이(가) 되돌렸어요",
        GiftActions.Reopen => $"{who}이(가) 다시 열었어요", GiftActions.Skip => $"{who}이(가) 건너뛰었어요", GiftActions.Expire => "기한이 지났어요",
        GiftActions.Open => "선물이 생겼어요", GiftActions.YearClose => "새해에 닫혔어요", GiftActions.Link => $"{who}이(가) 링크를 만들었어요", _ => l.Action,
    };
}
