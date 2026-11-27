using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WeeklyWordRow = ChallengeBible.Infrastructure.Data.WeeklyWord;

namespace ChallengeBible.Web.Pages.WeeklyWord;

/// <summary>`/{slug}/weekly-word` 이번 주 말씀 보기(ADR 0142, R-374 · R-375): 부서가 직접 입력한 가장 최근 말씀 한 건. 누구나(손님 포함), 이 부서 것만.
/// 가장 최근 주일에서 7일이 지났거나 말씀이 없으면 「준비중」(지난 말씀은 보여 주지 않는다).</summary>
public class IndexModel(WeeklyWordService words, TenantContext tenant, TimeProvider time) : PageModel, ITitled, IPublicTenantPage
{
    public string Title => "이번 주 말씀";
    public TenantContext Tenant => tenant;
    public WeeklyWordRow? Item { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        var latest = await words.LatestAsync(ct);
        var today = TenantClock.ToTenantDate(time.GetUtcNow(), tenant.Current.TimeZoneId);
        Item = WeeklyWordService.IsStale(latest?.SundayDate, today) ? null : latest;
        return Page();
    }
}
