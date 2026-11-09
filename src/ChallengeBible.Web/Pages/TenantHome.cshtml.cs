using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages;

/// <summary>`/{slug}` 부서 홈 메인(확정 시안: 홈메인, 0044). 부서 결정 · 소속 검사는 TenantPageFilter 가 한다.</summary>
public class TenantHomeModel(TenantContext tenant, FamilyTeamService teams, FamilyJoinService familyJoin, HomeService home, AccountService accounts,
    OnFamilyService onFamily, PhotoService photos) : PageModel, ITitled
{
    /// <summary>내 사진 파일 이름(0078) — 없으면 이름 첫 글자.</summary>
    public string? MyPhoto { get; private set; }
    public string Title => "";
    public TenantContext Tenant => tenant;
    /// <summary>"우리 가족팀" 칸(확정 시안 홈메인) — 내가 든 활성 가족팀이 없으면 null.</summary>
    public FamilyHome? Family { get; private set; }
    /// <summary>받은 가족팀 초대(확정 시안 home-invite, R-212) — 말씀 찾기 위 배너. 팀이 있거나 오늘 "다음에 할래요"를 눌렀으면 비어 있다.</summary>
    public IReadOnlyList<FamilyReceivedInvite> Invites { get; private set; } = [];
    /// <summary>온가족 한 줄(확정 시안 온가족 "부서 홈 칸", R-177) — 온가족이 없으면 null.</summary>
    public (string Name, int Total)? OnFamily { get; private set; }
    public bool IsGuardian => tenant.Has(Roles.Guardian);
    public HomeView Data { get; private set; } = null!;
    /// <summary>승인된 소속이 둘 이상이면 머리말 "부서 ▼" → 부서 고르기(확정 6).</summary>
    public bool ManyTenants { get; private set; }
    /// <summary>임시 부서(0064, R-172)에서 승인을 기다리는 내 신청의 부서 이름. 비어 있으면 "내 부서 찾기" 띠.</summary>
    public IReadOnlyList<string> Waiting { get; private set; } = [];
    /// <summary>`/{slug}/today` — 임시 부서의 "미션 화면"(0068). 임시 부서 `/{slug}` 는 1 · 2단계(윗부분만)이고 미션은 여기서 한다.</summary>
    [FromRoute(Name = "view")] public string? Screen { get; set; }
    public bool TodayScreen => Screen == "today";

    /// <summary>초대 배너 "다음에 할래요"(R-212): 오늘은 숨기고 내일 다시 보인다.</summary>
    public async Task<IActionResult> OnPostSnoozeInvitesAsync(CancellationToken ct)
    {
        await familyJoin.SnoozeInvitesAsync(ct);
        return Redirect($"/{tenant.Current.Slug}");
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (TodayScreen && !tenant.IsLobby) return Redirect($"/{tenant.Current.Slug}");   // 일반 부서 홈은 한 화면 그대로
        var me = User.UserName()!.Value;
        MyPhoto = await photos.FileOfAsync(me, ct);
        if (tenant.Current.MissionEnabled) Family = await teams.MyFamilyAsync(ct);
        if (tenant.Current.MissionEnabled && Family is null) Invites = (await familyJoin.StateAsync(ct)).Invites.Where(i => !i.SnoozedToday).ToList();
        OnFamily = await onFamily.LineAsync(me, ct);
        Data = await home.GetAsync(ct);
        ManyTenants = (await accounts.WhereToGoAsync(me, ct)).ApprovedCount > 1;
        if (tenant.IsLobby)
            Waiting = (await accounts.MyMembershipsAsync(me, ct))
                .Where(m => m.Status == MembershipStatus.Requested && m.TenantActive).Select(m => m.TenantName).ToList();
        return Page();
    }
}
