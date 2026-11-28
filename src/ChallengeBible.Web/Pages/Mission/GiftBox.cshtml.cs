using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Mission;

/// <summary>`/{slug}/gift-box` 선물함(2026-11-27 사용자 민트 시안): [신청한 선물 / 내가 받은 선물] 탭 · 선물마다 점수(개인 선물만) · 날짜 · 상태.</summary>
public class GiftBoxModel(GiftBoxService box, TenantContext tenant, TimeProvider time) : PageModel, ITitled
{
    public string Title => "선물함";
    public string Slug => tenant.Current.Slug;
    public TenantContext Tenant => tenant;
    public GiftBox? Box { get; private set; }
    /// <summary>탭: req(신청한 선물, 기본) · got(내가 받은 선물).</summary>
    [BindProperty(SupportsGet = true)] public string? Tab { get; set; }
    public bool Got => Tab == "got";
    public DateOnly Today => ChallengeBible.Domain.Time.TenantClock.ToTenantDate(time.GetUtcNow(), tenant.Current.TimeZoneId);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!tenant.Current.MissionEnabled) return NotFound();
        Box = await box.BoxAsync(ct);
        return Page();
    }

    /// <summary>날짜(부서 시간대).</summary>
    public string When(DateTime? utc) => utc is { } u ? ChallengeBible.Domain.Time.TenantClock.ToTenantDate(new DateTimeOffset(DateTime.SpecifyKind(u, DateTimeKind.Utc)), tenant.Current.TimeZoneId).ToString("M월 d일") : "";

    public string KindText(string kind) => GiftOrderService.KindLabel(kind);
}
