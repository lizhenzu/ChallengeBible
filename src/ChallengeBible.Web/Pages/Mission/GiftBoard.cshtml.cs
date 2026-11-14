using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Mission;

/// <summary>`/{slug}/gift-board` 부서 카드의 [더 보기](R-297): 골랐어요 · 준비했어요 · 받았어요(30일까지) 전체, 최근 순. 부서 소속만, 가격 없음.</summary>
public class GiftBoardModel(GiftOrderService orders, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "누가 선물 받나요";
    public string Slug => tenant.Current.Slug;
    public IReadOnlyList<GiftBoardRow> Rows { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!tenant.Current.MissionEnabled || !tenant.IsApproved) return NotFound();
        Rows = (await orders.BoardAsync(500, ct)).Rows;
        return Page();
    }
}
