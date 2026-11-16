using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Todo;

/// <summary>`/{slug}/todo` 오늘 할 일(ADR 0100, 확정 시안 오늘할일 ① · ②): 내가 볼 수 있는 일을 급한 순서로, 그 자리에서 버튼 하나로 처리. 위에 [홈으로] · 안 보기.</summary>
public class IndexModel(TodoService todo, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "오늘 할 일";
    public string Slug => tenant.Current.Slug;
    public TenantContext Tenant => tenant;
    public TodoBoard Board { get; private set; } = null!;
    [TempData] public string? Msg { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        Board = await todo.BoardAsync(ct);
        return Page();
    }

    private IActionResult Back(TodoError e, string ok)
    {
        Msg = e switch { TodoError.None => ok, TodoError.NotYours => "이 일을 처리할 수 없는 사람이에요.", TodoError.NotFound => "찾을 수 없어요.", _ => "이미 다른 사람이 처리했어요. 새로 고쳐 볼게요." };
        return Redirect($"/{Slug}/todo");
    }

    public async Task<IActionResult> OnPostBuyAsync(long id, CancellationToken ct) => Back(await todo.MoveGiftAsync(id, GiftActions.Purchase, ct), "구매 완료로 표시했어요. 이제 \"전해 주세요\"로 바뀌어요.");
    public async Task<IActionResult> OnPostDeliverAsync(long id, CancellationToken ct) => Back(await todo.MoveGiftAsync(id, GiftActions.Deliver, ct), "전달 완료! 모두의 할 일에서 사라졌어요.");

    /// <summary>[링크 복사](fetch): 링크 주소를 JSON 으로 돌려준다.</summary>
    public async Task<IActionResult> OnPostLinkAsync(long id, CancellationToken ct)
    {
        var (e, url) = await todo.LinkAsync(id, ct);
        return new JsonResult(new { ok = e == TodoError.None, url = url is null ? null : $"{Request.Scheme}://{Request.Host}{url}" });
    }

    public async Task<IActionResult> OnPostSnoozeAsync(int days, CancellationToken ct)
    {
        await todo.SnoozeAsync(days, ct);
        return Redirect($"/{Slug}");
    }
}
