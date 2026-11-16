using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Todo;

/// <summary>
/// `/{slug}/todo/gift/{id}` 선물 처리 화면(확정 시안 할일타임라인 ②, 2026-11-16): 무슨 일 · 정보 · 진행 타임라인(시각 · 사람) → 실제로 하고 [구매 완료] · [전달 완료] · [링크 복사].
/// 선물 그룹 · 선물 알림 · 우리 반 담임만(관리자가 아니어도). 전달 완료면 할 일로 돌아가고 카드는 사라진다. 취소 · 되돌리기는 관리 › 선물(관리자)에서.
/// </summary>
public class GiftModel(TodoService todo, GiftOrderService orders, TenantContext tenant, TimeProvider time) : PageModel, ITitled
{
    public string Title => "선물 처리";
    public string Slug => tenant.Current.Slug;
    public GiftOrderRow Row { get; private set; } = null!;
    public IReadOnlyList<TimelineStep> Steps { get; private set; } = [];
    public bool CanMove { get; private set; }
    public bool CanLink { get; private set; }
    public string? Why { get; private set; }
    [TempData] public string? Msg { get; set; }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken ct)
    {
        var (open, move, link) = await todo.GiftAccessAsync(id, ct);
        if (!open) return NotFound();
        var (row, logs) = await orders.GetAsync(id, ct);
        if (row is null) return NotFound();
        Row = row;
        CanMove = move;
        CanLink = link && BirthdayGiftService.LinkKinds.Contains(row.Kind) && row.State == GiftStates.Open && !row.Unassigned;   // 이미 고른 선물에는 링크가 필요 없다
        Steps = GiftTimeline.Build(row.Kind, row.State, row.NoAccount, row.CreatedUtc, row.PickedUtc, logs, time.GetUtcNow().UtcDateTime, TodoService.LateDays);
        Why = row.State switch
        {
            GiftStates.Picked => $"{row.Who}{(Batchim(row.Who) ? "이" : "가")} 고른 선물을 사서 전해 주세요. 사면 [구매 완료], 아이에게 건네면 [전달 완료]를 눌러요. 누가 했는지 아래에 남아요.",
            GiftStates.Purchased => "이미 샀어요. 아이에게 직접 건넨 뒤 [전달 완료]를 눌러 주세요. 누르면 할 일에서 사라져요.",
            GiftStates.Open => row.NoAccount ? "계정이 없는 아이예요. [링크 복사]로 부모님께 보내면 로그인 없이 골라요. 고르면 \"사 주세요\"로 바뀌어요." : "아이가 직접 고를 차례예요. 고르면 \"사 주세요\"로 바뀌어요.",
            GiftStates.Delivered => "전달까지 끝난 선물이에요.",
            _ => "부서 관리자가 관리 › 선물에서 처리해요.",
        };
        return Page();
    }

    private static bool Batchim(string s) => s.Length > 0 && s[^1] is >= '가' and <= '힣' && (s[^1] - '가') % 28 != 0;

    private IActionResult Back(long id, TodoError e, string ok, bool toList = false)
    {
        Msg = e switch { TodoError.None => ok, TodoError.NotYours => "이 일을 처리할 수 없는 사람이에요.", TodoError.NotFound => "찾을 수 없어요.", _ => "이미 다른 사람이 처리했어요. 새로 고쳐 볼게요." };
        return Redirect(e == TodoError.None && toList ? $"/{Slug}/todo" : $"/{Slug}/todo/gift/{id}");
    }

    public async Task<IActionResult> OnPostBuyAsync(long id, CancellationToken ct) =>
        Back(id, await todo.MoveGiftAsync(id, GiftActions.Purchase, ct), "구매 완료로 표시했어요. 아이에게 건넨 뒤 [전달 완료]를 눌러 주세요.");

    public async Task<IActionResult> OnPostDeliverAsync(long id, CancellationToken ct) =>
        Back(id, await todo.MoveGiftAsync(id, GiftActions.Deliver, ct), "전달 완료! 할 일에서 사라졌어요.", toList: true);

    /// <summary>[링크 복사](fetch): 링크 주소를 JSON 으로. 처음 만들면 이력에 "링크 보냄 · 누가"가 남는다.</summary>
    public async Task<IActionResult> OnPostLinkAsync(long id, CancellationToken ct)
    {
        var (e, url) = await todo.LinkAsync(id, ct);
        return new JsonResult(new { ok = e == TodoError.None, url = url is null ? null : $"{Request.Scheme}://{Request.Host}{url}" });
    }
}
