using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform` 플랫폼 홈(확정 시안: 플랫폼-홈): 숫자 4칸 → 챙길 일 → 부서별 오늘 | 최근 관리 기록.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class IndexModel(PlatformHomeService home) : PageModel, ITitled
{
    public string Title => "플랫폼 홈";
    public PlatformHome Data { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken ct) => Data = await home.GetAsync(User.AccountId()!.Value, ct);

    /// <summary>기록 시각: 오늘이면 시:분, 어제면 "어제", 그 전은 월/일(한국 시간).</summary>
    public string When(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(PlatformHomeService.PlatformTimeZone));
        var days = DateOnly.FromDateTime(Data.NowLocal).DayNumber - DateOnly.FromDateTime(local).DayNumber;
        return days switch { 0 => local.ToString("HH:mm"), 1 => "어제", _ => local.ToString("M'/'d") };
    }
}
