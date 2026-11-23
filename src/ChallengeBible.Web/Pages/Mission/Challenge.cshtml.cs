using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Mission;

/// <summary>
/// `/{slug}/challenge` 「나의 도전」(2026-11-23 사용자 확정 · ADR 0136): A안 링 히어로 + B안 「이번 주」 막대.
/// 홈 상단 포인트 링을 누르면 온다. 오늘 미션 · 프로그램 진행 · 참여자 숫자는 홈에서 이 화면으로 옮겼다.
/// 선물 대상자 · 우리 가족팀은 이 화면에 넣지 않는다(각자 화면 · ADR 0134 · 0132 그대로).
/// </summary>
public class ChallengeModel(HomeService home, ChallengeCardService cards, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "나의 도전";
    public string Slug => tenant.Current.Slug;
    public HomeView Data { get; private set; } = null!;
    public ChallengeCard? Card { get; private set; }
    /// <summary>링 히어로 값(참여자만). 이 화면에서는 누를 곳이 없으므로 Href 없이 그린다.</summary>
    public PointRing? Ring { get; private set; }
    public DateOnly Today => Data.Today;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!tenant.Current.MissionEnabled) return NotFound();
        Data = await home.GetAsync(ct);
        Card = await cards.GetAsync(Data, User.IsPlatformAdmin(), ct);
        Ring = ChallengeCardService.RingOf(Data, Slug) is { } r ? r with { Href = null } : null;
        return Page();
    }

    /// <summary>진행 줄 주소 — 성경통독 · 성경필사 · 기도365.</summary>
    public string Href(string key) => key switch
    {
        "reading" => "reading",
        "copy" => "copy",
        _ => "prayer365",
    };
}
