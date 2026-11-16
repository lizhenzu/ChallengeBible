using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Prayer365;

/// <summary>
/// `/{slug}/prayer365` 자녀를 위한 365 기도(ADR 0106, 확정 시안 365기도 ①): 오늘 기도문만(날짜 이동 없음).
/// 학생 · 새친구 · 로그인 안 한 사람에게는 없는 화면(R-316). [오늘 기도 했어요]는 하루 한 번(R-317).
/// </summary>
public class IndexModel(Prayer365Service prayers, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "자녀를 위한 365 기도";
    public string Slug => tenant.Current.Slug;
    public ChallengeBible.Infrastructure.Data.Prayer365 Item { get; private set; } = null!;
    public string MonthDay { get; private set; } = "";
    public DateOnly Today { get; private set; }
    public int Together { get; private set; }
    public Prayer365Stats Stats { get; private set; } = null!;
    [TempData] public string? Msg { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!prayers.CanSee(User.IsPlatformAdmin())) return NotFound();
        Today = prayers.Today;
        MonthDay = Prayer365Service.Md(Today);
        Item = await prayers.GetAsync(MonthDay, ct);
        if (!tenant.IsAdmin && !User.IsPlatformAdmin()) await prayers.AddViewAsync(MonthDay, ct);   // 관리자가 연 것은 세지 않는다
        Together = await prayers.TogetherTodayAsync(ct);
        Stats = await prayers.MyStatsAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostPrayAsync(CancellationToken ct)
    {
        if (!prayers.CanSee(User.IsPlatformAdmin())) return NotFound();
        var added = await prayers.PrayAsync(Prayer365Service.Md(prayers.Today), ct);
        Msg = added ? "오늘 기도를 남겼어요. 함께해 주셔서 고마워요." : "오늘은 이미 기도를 남겼어요.";
        return Redirect($"/{Slug}/prayer365");
    }
}
