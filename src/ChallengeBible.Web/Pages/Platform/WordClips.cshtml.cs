using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>
/// `/platform/content/word/clips?month=yyyy-MM` 영상 구간 편집(유년부 /JuManNa/SegmentEditor 그대로, 챌린지 테마, 확정 시안 퀴즈출제-말씀입력 ④).
/// ▶ 로 장 듣기 영상을 열고 [시작은 지금] · [끝은 지금]을 누르면 바로 저장(그날 구간 목록을 통째로, R-127). 플랫폼 관리자만.
/// </summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class WordClipsModel(WordToolsService tools, TimeProvider time) : PageModel, ITitled
{
    public string Title => "영상 구간 편집 · 말씀";
    [BindProperty(SupportsGet = true)] public string? Month { get; set; }
    public DateOnly First { get; private set; }
    public IReadOnlyList<ClipDay> Days { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        First = ContentCheckModel.ParseMonth(Month)
            ?? (Domain.Time.TenantClock.ToTenantDate(time.GetUtcNow().UtcDateTime, PlatformHomeService.PlatformTimeZone) is var t ? new DateOnly(t.Year, t.Month, 1) : default);
        Days = await tools.ClipMonthAsync(User.UserName()!, First, ct);
    }

    /// <summary>한 장의 시작(s) · 끝(e) 저장. sec 가 비면 기본(처음 · 끝).</summary>
    public async Task<IActionResult> OnPostSaveAsync(DateOnly date, short chapter, string which, int? sec, CancellationToken ct) =>
        await tools.SaveClipAsync(User.UserName()!, date, chapter, which, sec, ct) ? new JsonResult(new { ok = true }) : BadRequest();

    public string MonthText(DateOnly m) => m.ToString("yyyy-MM");
    public static string Mmss(int? sec) => sec is int s ? $"{s / 60}:{s % 60:00}" : "";
}
