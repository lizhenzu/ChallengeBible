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
public class IndexModel(IntroService intro, WorshipService worship, BulletinService bulletins, TenantContext tenant) : PageModel, ITitled, IPublicTenantPage
{
    /// <summary>이번 방문에서 소개 화면을 이미 봤다는 표시(브라우저를 닫으면 사라지는 쿠키). 부서마다 따로.</summary>
    public static string SeenCookie(int tenantId) => $"cb_intro_{tenantId}";

    public string Title => $"{PlatformTodayService.ShortDept(tenant.Current.Name)} 소개";
    public TenantContext Tenant => tenant;
    public TenantIntro Item { get; private set; } = null!;
    public IReadOnlyList<WorshipSchedule> Worship { get; private set; } = [];
    public DateOnly? LatestBulletin { get; private set; }
    /// <summary>로그인한 사람만 「1주일 후 다시 보기」 · 「다시 보지 않기」(계정 기준).</summary>
    public bool Member => User.UserName() is not null;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby || await intro.GetAsync(ct: ct) is not { } item) return Redirect($"/{tenant.Current.Slug}?intro=0");
        Item = item;
        Worship = await worship.ListAsync(ct: ct);
        LatestBulletin = await bulletins.LatestDateAsync(ct);
        return Page();
    }

    /// <summary>「도전!!! 성경말씀으로 가기」: 이번 방문 동안은 부서 홈이 다시 소개 화면으로 보내지 않는다.</summary>
    public IActionResult OnGetGo()
    {
        MarkSeen();
        return Redirect($"/{tenant.Current.Slug}");
    }

    public async Task<IActionResult> OnPostLaterAsync(CancellationToken ct)
    {
        if (User.UserName() is { } me) await intro.LaterAsync(me, ct);
        MarkSeen();
        return Redirect($"/{tenant.Current.Slug}");
    }

    public async Task<IActionResult> OnPostNeverAsync(CancellationToken ct)
    {
        if (User.UserName() is { } me) await intro.NeverAsync(me, ct);
        MarkSeen();
        return Redirect($"/{tenant.Current.Slug}");
    }

    private void MarkSeen() => Response.Cookies.Append(SeenCookie(tenant.Current.Id), "1",
        new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = Request.IsHttps, IsEssential = true });
}
