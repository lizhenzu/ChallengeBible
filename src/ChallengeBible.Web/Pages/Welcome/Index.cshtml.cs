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
/// 2026-11-27(ADR 0142): 임시 부서 말고 **모든 부서**에 있다(글이 없으면 부서 이름). 누구나(손님 포함) 본다.
/// (2026-11-26 사용자: 이제 자동으로 보내지 않는다 — 첫 방문자에게 전체 주소로 알려 주고, 홈 맨 아래 버튼으로 온다) 예전에는 부서 홈이 들어올 때 이쪽으로 먼저 보냈다(계정이 「1주일 후」 · 「다시 보지 않기」를 고르지 않았고, 이번 방문에서 「가기」를 누르지 않았다면).
/// </summary>
public class IndexModel(IntroService intro, WorshipService worship, BulletinService bulletins, GiftOrderService gifts, WeeklyWordService words, AlbumService albums, TenantContext tenant, TimeProvider time) : PageModel, ITitled, IPublicTenantPage
{
    public string Title => "메인화면";
    public TenantContext Tenant => tenant;
    public TenantIntro Item { get; private set; } = null!;
    public IReadOnlyList<WorshipSchedule> Worship { get; private set; } = [];
    public DateOnly? LatestBulletin { get; private set; }
    /// <summary>「준비중」 판단(ADR 0142, R-375): 칸마다 준비가 됐나. 주보 · 말씀은 가장 최근 것에서 7일이 안 지났을 때, 앨범은 볼 수 있는 사진이 있을 때, 소개는 소개 글이 있을 때.</summary>
    public bool BulletinReady { get; private set; }
    public bool WordReady { get; private set; }
    public bool AlbumReady { get; private set; }
    public bool AboutReady { get; private set; }
    /// <summary>「부모님과 함께하는 미션북」 칸 보이기(부서 스위치, 처음엔 유년부만).</summary>
    public bool MissionBookOn => tenant.Current.MissionBookEnabled;
    /// <summary>한 줄 선물 알림(2026-11-26 사용자: 첫 화면 · 부서 홈에서 웰컴 하단으로 옮김). 이 부서 승인 회원에게만, 대상자가 있을 때.</summary>
    public GiftTargetBoard? GiftBoard { get; private set; }
    /// <summary>오늘(부서 시간대) — 위쪽 날짜 줄(2026-11-24 새 퍼블리시본).</summary>
    public DateOnly Today => TenantClock.ToTenantDate(time.GetUtcNow(), tenant.Current.TimeZoneId);
    /// <summary>로그인한 사람만 「1주일 후 다시 보기」 · 「다시 보지 않기」(계정 기준).</summary>
    public bool Member => User.UserName() is not null;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return Redirect($"/{tenant.Current.Slug}?intro=0");   // 임시 부서(처음 오신 분)만 메인화면이 없다
        Item = await intro.GetAsync(ct: ct) ?? new TenantIntro { TenantId = tenant.Current.Id, Title = tenant.Current.Name };   // 모든 부서에 있다(ADR 0142) — 글이 없으면 부서 이름
        Worship = await worship.ListAsync(ct: ct);
        LatestBulletin = await bulletins.LatestDateAsync(ct);
        BulletinReady = LatestBulletin is { } lb && Today.DayNumber - lb.DayNumber < WeeklyWordService.StaleDays;
        WordReady = !WeeklyWordService.IsStale((await words.LatestAsync(ct))?.SundayDate, Today);
        AlbumReady = (await albums.ListAsync(new AlbumViewer(User.UserName(), User.IsPlatformAdmin()), ct: ct)).Any(a => a.Photos > 0);
        AboutReady = !string.IsNullOrWhiteSpace(Item.Body);
        if (tenant.IsApproved && !tenant.IsLobby) GiftBoard = await gifts.TargetBoardAsync(3, ct);
        return Page();
    }

    /// <summary>「도전!!! 성경말씀으로 가기」: 소개 화면에서 온 요청이라 부서 홈이 다시 보내지 않는다(기억하는 것은 없다).</summary>
    public IActionResult OnGetGo() => Redirect($"/{tenant.Current.Slug}");
}
