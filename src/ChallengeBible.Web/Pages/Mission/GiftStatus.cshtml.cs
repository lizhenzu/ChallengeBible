using ChallengeBible.Domain.Time;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Mission;

/// <summary>
/// `/{slug}/gift-status` 「선물 대상자와 상태」(2026-11-23 사용자 확정 B안 · ADR 0138).
/// 홈 「오늘의 말씀」 아래 한 줄 알림을 누르면 온다 — 사람마다 한 칸, 3단계 눈금(골랐어요 → 샀어요 → 줬어요).
/// 부서 소속만 · 승인 소속 · 임시 부서 아님. 가격 없음(R-297), 받은 선물은 받은 지 30일까지.
/// </summary>
public class GiftStatusModel(GiftOrderService gifts, TenantContext tenant, TenantClock clock) : PageModel, ITitled
{
    public string Title => "선물 대상자와 상태";
    public string Slug => tenant.Current.Slug;
    public GiftTargetBoard? Board { get; private set; }
    public DateOnly Today { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!tenant.IsApproved || tenant.IsLobby) return NotFound();
        Today = clock.Today(tenant.Current.TimeZoneId);
        Board = await gifts.TargetBoardAsync(200, ct);
        return Page();
    }
}
