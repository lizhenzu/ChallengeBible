using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages;

/// <summary>
/// `/` 공통 첫 화면(0045, 확정 시안 첫화면-시안B · 0061). 2026-11-23 사용자: 로그인한 사람도 이 화면에 머문다 —
/// 머리말에 "000님", 부서가 하나면 그 부서 홈 바로가기, ☰ 는 그 부서의 기존 메뉴판(부서 하나일 때)을 연다.
/// 플랫폼 관리자이면서 소속이 없을 때만 플랫폼 홈으로 보낸다.
/// </summary>
public class IndexModel(AccountService accounts, TenantClock clock, LandingService landing,
    TenantResolver tenants, TenantContext tenant,
    FamilyTeamService teams, FamilyJoinService familyJoin, RuleService rules, HomeService home, ChallengeCardService cards,
    GiftOrderService gifts) : PageModel, ITitled
{
    public string Title => "";
    /// <summary>맨 아래 © 연도: 부서가 없는 화면이라 한국 시간 기준 올해(하드코딩 안 함).</summary>
    public int Year => clock.Today("Asia/Seoul").Year;
    public LandingView Data { get; private set; } = null!;
    /// <summary>로그인한 사람의 머리말 정보(이름 · 승인된 부서 수 · 하나뿐인 부서 slug). 로그인 전에는 null.</summary>
    public LandingMe? Me { get; private set; }
    /// <summary>☰ 가 여는 기존 부서 메뉴판. 부서가 꼭 하나이고 승인된 소속일 때만 채운다.</summary>
    public TenantContext? DrawerTenant { get; private set; }

    /// <summary>
    /// 가족팀 카드(2026-11-23 사용자 · ADR 0132): 부서 홈과 같은 세 상태(초대 배너 → 우리 팀 카드 → 만들기 유도).
    /// 부서가 꼭 하나이고 그 부서에서 가족팀을 켰을 때만 채운다. 하나뿐인 부서 = 가족팀 화면이 있는 부서.
    /// </summary>
    public FamilyHome? Family { get; private set; }
    /// <summary>받은 가족팀 초대 — 오늘 "다음에 할래요"를 누른 것은 뺀다(R-212).</summary>
    public IReadOnlyList<FamilyReceivedInvite> Invites { get; private set; } = [];
    /// <summary>친구팀을 만들 수 있는 학생인지(만들기 카드의 친구팀 버튼, R-221).</summary>
    public bool FamilyIsStudent { get; private set; }
    /// <summary>가족팀에 들어갈 수 있는 역할인지(교사·임원 제외). 만들기 카드 판단.</summary>
    public bool FamilyCanJoin { get; private set; }
    /// <summary>가족팀 카드를 그릴 수 있는 상태인지(부서 하나 · 미션 켜짐 · 규칙 Features.FamilyTeam).</summary>
    public bool FamilyOn { get; private set; }

    /// <summary>나의 도전 현황 통합 카드(2026-11-23 사용자 확정). 하나뿐인 부서의 내 기록으로 채운다.</summary>
    public ChallengeCard? Card { get; private set; }
    /// <summary>그 부서 주소(카드 링크). 카드가 없으면 null.</summary>
    public string? CardSlug { get; private set; }
    /// <summary>홈 상단 포인트 링(2026-11-23 사용자 확정 B안 · ADR 0136): 누르면 그 부서의 「나의 도전」. 참여자만.</summary>
    public PointRing? Ring { get; private set; }
    /// <summary>선물 대상자와 상태 카드(2026-11-23 사용자 확정 C안 · ADR 0134). 부서 하나뿐인 사람만.</summary>
    public GiftTargetBoard? GiftBoard { get; private set; }

    /// <summary>초대 배너 "다음에 할래요"(R-212): 오늘은 숨기고 내일 다시 보인다.</summary>
    public async Task<IActionResult> OnPostSnoozeInvitesAsync(CancellationToken ct)
    {
        if (User.UserName() is { } id)
        {
            var go = await accounts.WhereToGoAsync(id, ct);
            if (go.ApprovedCount == 1 && go.OnlyTenantSlug is { } slug
                && await tenants.ResolveAsync(HttpContext, slug) == TenantResolution.Ok)
                await familyJoin.SnoozeInvitesAsync(ct);
        }
        return Redirect("/");
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        // 앱은 언제나 `/` 로 열린다. 메인화면(웰컴)으로 자동으로 보내지 않는다(2026-11-26 사용자: 메인화면은 첫 방문자에게 전체 주소로 알려 주고, 홈 맨 아래 버튼으로만 간다).
        // 공용 자료는 부서 영역 없는 상태에서 먼저 읽는다(부서를 정하면 공용 콘텐츠가 가려진다).
        Data = await landing.GetAsync(ct);
        if (User.UserName() is { } id)
        {
            var go = await accounts.WhereToGoAsync(id, ct);
            if (go.ApprovedCount == 0 && User.IsPlatformAdmin()) return Redirect("/platform");
            // 2026-11-27 사용자: 바탕화면 아이콘으로 열 때도 로그인한 뒤와 같은 화면 = 부서 홈(새 퍼블리시본). 부서가 하나뿐인 사람은 그 홈으로(?home=1 은 이 첫 화면)
            if (go.ApprovedCount == 1 && go.OnlyTenantSlug is { } only && Request.Query["home"] != "1") return Redirect("/" + only);
            Me = new(User.DisplayName(), go.ApprovedCount, go.OnlyTenantSlug);
            // ☰ 는 "기존 햄버거 메뉴 그대로"(2026-11-23 사용자) — 부서가 하나면 그 부서 메뉴판을 쓴다.
            if (go.ApprovedCount == 1 && go.OnlyTenantSlug is { } slug
                && await tenants.ResolveAsync(HttpContext, slug) == TenantResolution.Ok)
            {
                DrawerTenant = tenant;
                await LoadFamilyAsync(ct);
                var data = await home.GetAsync(ct);
                Card = await cards.GetAsync(data, User.IsPlatformAdmin(), ct);
                CardSlug = slug;
                Ring = ChallengeCardService.RingOf(data, slug);
                if (tenant.IsApproved && !tenant.IsLobby) GiftBoard = await gifts.TargetBoardAsync(3, ct);
            }
        }
        return Page();
    }

    /// <summary>
    /// 부서 홈과 같은 가족팀 세 상태를 채운다(2026-11-22 3차 확정 · ADR 0132).
    /// 부서가 하나뿐인 사람만 — 그 부서의 가족팀 화면(`/{slug}/family-team`)으로 이어진다.
    /// </summary>
    private async Task LoadFamilyAsync(CancellationToken ct)
    {
        var t = tenant.Current;
        if (!t.MissionEnabled) return;
        var r = await rules.ForDateAsync(clock.Today(t.TimeZoneId), ct);
        if (!r.Features.FamilyTeam) return;

        FamilyOn = true;
        Family = await teams.MyFamilyAsync(ct);
        var kind = UserRole.KindOf(tenant.RoleCode);
        FamilyIsStudent = kind == UserRoleKind.Student;
        FamilyCanJoin = UserRole.CanJoinFamily(kind);
        if (Family is null)
        {
            var fam = await familyJoin.StateAsync(ct);
            Invites = fam.Invites.Where(i => !i.SnoozedToday).ToList();
        }
    }
}

/// <summary>첫 화면 머리말에 쓰는 로그인 정보(이름 · 부서 수 · 하나뿐인 부서 slug).</summary>
public sealed record LandingMe(string Name, int DeptCount, string? Slug);
