using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Todo;

/// <summary>`/{slug}/todo` 오늘 할 일(ADR 0100, 확정 시안 오늘할일 ① · ②): 내가 볼 수 있는 일을 급한 순서로, 설명 · 정보 · 타임라인을 보고 업무 화면으로 간다(카드에서 완료하지 않는다, 확정 시안 할일타임라인). 위에 [홈으로] · 안 보기.</summary>
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

    /// <summary>부모님께 보낼 안내글(JSON): 링크가 없으면 만든다. 내 할 일 카드에 없는 선물이면 404.</summary>
    public async Task<IActionResult> OnPostShareAsync(long id, CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        if (await todo.ShareAsync(id, ct) is not { } r) return NotFound();
        var url = $"{Request.Scheme}://{Request.Host}/gift/{r.Token}";
        return new JsonResult(new { url, text = BirthdayGiftService.ShareText(r.View, url, r.Linked) });
    }

    public async Task<IActionResult> OnPostSnoozeAsync(int days, CancellationToken ct)
    {
        await todo.SnoozeAsync(days, ct);
        return Redirect($"/{Slug}");
    }
}
