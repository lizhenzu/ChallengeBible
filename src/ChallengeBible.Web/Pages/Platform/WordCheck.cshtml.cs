using System.Globalization;
using System.Text.Json;
using ChallengeBible.Domain.Time;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/content/word/check?month=&amp;date=` 말씀 입력 점검(확정 시안 word-check, R-216). 서버는 한 달치 자료만 주고 달력 · 칩 · 그날 칸은 word-check.js 가 그린다.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class WordCheckModel(ContentAdminService content, TimeProvider time) : PageModel, ITitled
{
    public string Title => "말씀 입력 점검 · 플랫폼";

    [BindProperty(SupportsGet = true)] public string? Month { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }

    public DateOnly First { get; private set; }
    /// <summary>스크립트에 넘기는 자료. 기본 직렬화라 한글과 태그 글자가 \uXXXX 로 들어가 script 안에서 안전하다.</summary>
    public string DataJson { get; private set; } = "{}";

    public string MonthText(DateOnly m) => m.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    public string Link(DateOnly m) => $"/platform/content/word/check?month={MonthText(m)}";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var today = TenantClock.ToTenantDate(time.GetUtcNow().UtcDateTime, PlatformHomeService.PlatformTimeZone);
        First = ParseMonth(Month) ?? new DateOnly(today.Year, today.Month, 1);
        var last = First.AddMonths(1).AddDays(-1);
        var initial = Date is { } d && d >= First && d <= last ? d : today >= First && today <= last ? today : First;
        var days = await content.WordCheckMonthAsync(User.AccountId()!.Value, First, ct);
        DataJson = JsonSerializer.Serialize(new { Month = MonthText(First), Initial = initial.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Days = days }, JsonSerializerOptions.Web);
        return Page();
    }

    /// <summary>"2026-11" 만 받는다. 형식이 틀리거나 지나치게 먼 해는 null(이번 달).</summary>
    public static DateOnly? ParseMonth(string? month) =>
        DateOnly.TryParseExact((month ?? "") + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) && f.Year is >= 2000 and <= 2100 ? f : null;
}
