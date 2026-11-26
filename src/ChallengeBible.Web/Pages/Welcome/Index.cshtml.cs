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
/// (2026-11-26 사용자: 이제 자동으로 보내지 않는다 — 첫 방문자에게 전체 주소로 알려 주고, 홈 맨 아래 버튼으로 온다) 예전에는 부서 홈이 들어올 때 이쪽으로 먼저 보냈다(계정이 「1주일 후」 · 「다시 보지 않기」를 고르지 않았고, 이번 방문에서 「가기」를 누르지 않았다면).
/// </summary>
public class IndexModel(IntroService intro, WorshipService worship, BulletinService bulletins, GiftOrderService gifts, TenantContext tenant, TimeProvider time) : PageModel, ITitled, IPublicTenantPage
{
    public string Title => $"{PlatformTodayService.ShortDept(tenant.Current.Name)} 소개";
    public TenantContext Tenant => tenant;
    public TenantIntro Item { get; private set; } = null!;
    public IReadOnlyList<WorshipSchedule> Worship { get; private set; } = [];
    public DateOnly? LatestBulletin { get; private set; }
    /// <summary>한 줄 선물 알림(2026-11-26 사용자: 첫 화면 · 부서 홈에서 웰컴 하단으로 옮김). 이 부서 승인 회원에게만, 대상자가 있을 때.</summary>
    public GiftTargetBoard? GiftBoard { get; private set; }
    /// <summary>오늘(부서 시간대) — 위쪽 날짜 줄(2026-11-24 새 퍼블리시본).</summary>
    public DateOnly Today => TenantClock.ToTenantDate(time.GetUtcNow(), tenant.Current.TimeZoneId);
    /// <summary>로그인한 사람만 「1주일 후 다시 보기」 · 「다시 보지 않기」(계정 기준).</summary>
    public bool Member => User.UserName() is not null;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby || await intro.GetAsync(ct: ct) is not { } item) return Redirect($"/{tenant.Current.Slug}?intro=0");
        Item = item;
        Worship = await worship.ListAsync(ct: ct);
        LatestBulletin = await bulletins.LatestDateAsync(ct);
        if (tenant.IsApproved && !tenant.IsLobby) GiftBoard = await gifts.TargetBoardAsync(3, ct);
        return Page();
    }

    /// <summary>「도전!!! 성경말씀으로 가기」: 소개 화면에서 온 요청이라 부서 홈이 다시 보내지 않는다(기억하는 것은 없다).</summary>
    public IActionResult OnGetGo() => Redirect($"/{tenant.Current.Slug}");
}
