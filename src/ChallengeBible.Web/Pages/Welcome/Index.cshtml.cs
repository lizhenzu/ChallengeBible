using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Welcome;

/// <summary>
/// `/{slug}/welcome` 부서 소개 화면(ADR 0140, R-368 ~ R-371, 2026-11-23 확정 시안 유년부-소개 C안): 소개 글 · 예배 안내 · 주보 · 사진첩 · 「도전!!! 성경말씀으로 가기」.
/// 줄(app.TenantIntro)이 있는 부서(유년부)에만 있다 — 없거나 꺼져 있으면 부서 홈으로. 누구나(손님 포함) 본다.
/// 부서 홈(`/{slug}`)이 들어올 때 이쪽으로 먼저 보낸다(계정이 「1주일 후」 · 「다시 보지 않기」를 고르지 않았고, 이번 방문에서 「가기」를 누르지 않았다면).
/// </summary>
public class IndexModel(IntroService intro, WorshipService worship, BulletinService bulletins, TenantContext tenant, TimeProvider time) : PageModel, ITitled, IPublicTenantPage
{
    /// <summary>**이 부서 화면에서** 옮겨 온 요청인가(Referer 가 같은 주소의 `/{부서}` 또는 `/{부서}/…`).
    /// 부서 홈은 밖에서 들어올 때(주소창 · 즐겨찾기 · QR · 앱 아이콘 · 첫 화면 · 로그인 · 다른 사이트)만 소개 화면으로 보내고,
    /// 그 부서 화면들 사이를 오갈 때(소개 화면의 「가기」 · 주보 · 사진첩에서 「부서 홈」)는 끼어들지 않는다.
    /// (처음엔 「가기」를 누르면 브라우저를 닫을 때까지 건너뛰는 쿠키를 썼는데, 두 번째 방문에도 안 나오는 버그였다 — 2026-11-23 사용자 신고.)</summary>
    public static bool IsFromSameDept(HttpRequest r, string slug) =>
        Uri.TryCreate(r.Headers.Referer.ToString(), UriKind.Absolute, out var from)
        && from.Authority.Equals(r.Host.Value, StringComparison.OrdinalIgnoreCase)
        && (from.AbsolutePath.Equals($"/{slug}", StringComparison.OrdinalIgnoreCase) || from.AbsolutePath.StartsWith($"/{slug}/", StringComparison.OrdinalIgnoreCase));

    public string Title => $"{PlatformTodayService.ShortDept(tenant.Current.Name)} 소개";
    public TenantContext Tenant => tenant;
    public TenantIntro Item { get; private set; } = null!;
    public IReadOnlyList<WorshipSchedule> Worship { get; private set; } = [];
    public DateOnly? LatestBulletin { get; private set; }
    /// <summary>오늘(부서 시간대) — 위쪽 날짜 줄(2026-11-24 새 퍼블리시본).</summary>
    public DateOnly Today => TenantClock.ToTenantDate(time.GetUtcNow(), tenant.Current.TimeZoneId);
    /// <summary>로그인한 사람만 「1주일 후 다시 보기」 · 「다시 보지 않기」(계정 기준).</summary>
    public bool Member => User.UserName() is not null;

    /// <summary>같은 사이트 안에서 온 요청인가(Referer 가 이 사이트). 앱 아이콘 · 주소창 · 즐겨찾기는 Referer 가 없다.</summary>
    public static bool IsSameOrigin(HttpRequest r) =>
        Uri.TryCreate(r.Headers.Referer.ToString(), UriKind.Absolute, out var from) && from.Authority.Equals(r.Host.Value, StringComparison.OrdinalIgnoreCase);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby || await intro.GetAsync(ct: ct) is not { } item) return Redirect($"/{tenant.Current.Slug}?intro=0");
        Item = item;
        LastDept.Set(HttpContext, tenant.Current.Slug);   // 마지막으로 본 부서 기억(앱을 열면 `/` 가 여기로 보낸다)
        Worship = await worship.ListAsync(ct: ct);
        LatestBulletin = await bulletins.LatestDateAsync(ct);
        return Page();
    }

    /// <summary>「도전!!! 성경말씀으로 가기」: 소개 화면에서 온 요청이라 부서 홈이 다시 보내지 않는다(기억하는 것은 없다).</summary>
    public IActionResult OnGetGo() => Redirect($"/{tenant.Current.Slug}");
}
