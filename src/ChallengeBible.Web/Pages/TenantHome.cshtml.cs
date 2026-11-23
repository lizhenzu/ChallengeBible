using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages;

/// <summary>
/// `/{slug}` 부서 홈 메인. 2026-11-22 사용자 확정(ADR 0127): dodan 시안 화면 하나로 교체 —
/// 머리말 · 로고 · 검색 · 프로그램 4칸 · 오늘의 말씀 · 나의 도전 현황만 둔다.
/// 부서 결정 · 소속 검사는 TenantPageFilter 가 한다.
/// </summary>
public class TenantHomeModel(TenantContext tenant, FamilyTeamService teams, FamilyJoinService familyJoin, HomeService home, AccountService accounts,
    PhotoService photos, TodoService todo, Prayer365Service prayer365, MessageService msgs,
    BirthdayGiftService birthdays, ChallengeCardService cards) : PageModel, ITitled
{
    /// <summary>로그인이 남기는 표식. 새 쪽지 팝업을 로그인 직후 한 번 띄우는 데 쓴다(ADR 0112).</summary>
    public const string MsgBandKey = "msg-band";

    public int NewMessages { get; private set; }
    /// <summary>팝업에 보일 새 쪽지(최근 3개).</summary>
    public IReadOnlyList<MsgRow> NewList { get; private set; } = [];
    /// <summary>내 사진 파일 이름(0078) — 없으면 이름 첫 글자.</summary>
    public string? MyPhoto { get; private set; }
    public string Title => "";
    public TenantContext Tenant => tenant;
    /// <summary>학부모 — 홈 맨 아래 "자녀 미션 보기" 작은 링크(2026-11-22 확정).</summary>
    public bool IsGuardian => tenant.Has(Roles.Guardian);
    /// <summary>우리 가족팀 카드 — 내가 든 팀이 없으면 null(2026-11-22 3차: 홈 아래에 다시).</summary>
    public FamilyHome? Family { get; private set; }
    /// <summary>받은 가족팀 초대 — 오늘 "다음에 할래요"를 누른 것은 뺀다(R-212).</summary>
    public IReadOnlyList<FamilyReceivedInvite> Invites { get; private set; } = [];
    /// <summary>친구팀을 만들 수 있는 학생인지(빈 상태 카드의 친구팀 버튼).</summary>
    public bool FamilyIsStudent { get; private set; }
    /// <summary>가족팀에 들어갈 수 있는 역할인지(교사·임원 제외). 빈 상태 만들기 카드 판단.</summary>
    public bool FamilyCanJoin { get; private set; }
    public HomeView Data { get; private set; } = null!;
    /// <summary>임시 부서(0064)에서 승인을 기다리는 내 신청의 부서 이름.</summary>
    public IReadOnlyList<string> Waiting { get; private set; } = [];
    /// <summary>`/{slug}/today` — 임시 부서의 "미션 화면".</summary>
    [FromRoute(Name = "view")] public string? Screen { get; set; }
    public bool TodayScreen => Screen == "today";
    /// <summary>로그인 안 한 사람의 둘러보기(R-336).</summary>
    public bool Guest { get; private set; }
    /// <summary>생일 30일 전이 되면 생기는 선물(ADR 0123) — 팝업 판단에만 쓴다. 홈 카드는 2026-11-22 제거.</summary>
    public IReadOnlyList<BirthdayGiftView> Birthdays { get; private set; } = [];
    /// <summary>홈을 열면 띄우는 "생일 축하해요!" 팝업 — 띄울 때가 아니면 null(R-346).</summary>
    public BirthdayGiftView? BirthdayPopup { get; private set; }
    /// <summary>365 기도는 학생 · 새친구에게 보이지 않는다(ADR 0106). 홈에서는 아이콘 노출만 판단한다.</summary>
    public bool PrayerVisible { get; private set; }
    /// <summary>나의 도전 현황 통합 카드(2026-11-23 사용자 확정): 오늘 미션 · 통독 · 필사 · 기도365 · 포인트. 손님이면 null.</summary>
    public ChallengeCard? Card { get; private set; }

    /// <summary>생일 팝업 "다음에 할래요" — 내일 · 3일 · 5일 뒤(R-346).</summary>
    public async Task<IActionResult> OnPostSnoozeBirthdayAsync(int days, CancellationToken ct)
    {
        await birthdays.SnoozePopupAsync(days, ct);
        return Redirect($"/{tenant.Current.Slug}");
    }

    /// <summary>초대 배너 "다음에 할래요"(R-212): 오늘은 숨기고 내일 다시 보인다.</summary>
    public async Task<IActionResult> OnPostSnoozeInvitesAsync(CancellationToken ct)
    {
        await familyJoin.SnoozeInvitesAsync(ct);
        return Redirect($"/{tenant.Current.Slug}");
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (TodayScreen && !tenant.IsLobby) return Redirect($"/{tenant.Current.Slug}");
        if (User.UserId() is null)
        {
            if (tenant.IsLobby || TodayScreen) return Redirect("/account/login?returnUrl=" + Uri.EscapeDataString(Request.Path + Request.QueryString));
            Guest = true;
            Data = GuestView(await home.GetAsync(ct));
            return Page();
        }

        if (TempData.Remove(ChallengeBible.Web.Pages.Account.LoginModel.TodoCheckKey) && await todo.ShouldRedirectAsync(ct))
            return Redirect($"/{tenant.Current.Slug}/todo");

        var me = User.UserName()!;
        var afterLogin = TempData.Remove(MsgBandKey);
        (var popN, NewList) = await MsgPopup.DecideAsync(HttpContext, msgs, me, afterLogin, ct);
        if (afterLogin || popN > 0) NewMessages = popN > 0 ? popN : await msgs.UnreadAsync(me, ct);
        MyPhoto = await photos.FileOfAsync(me, ct);

        if (tenant.Current.MissionEnabled)
        {
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

        Data = await home.GetAsync(ct);

        if (!tenant.IsLobby && tenant.Has(Roles.Participant))
        {
            await birthdays.EnsureAsync(ct);
            Birthdays = (await birthdays.MineAsync(ct)).Where(g => g.Kind == GiftKinds.Birthday).ToList();
            if (NewList.Count == 0) BirthdayPopup = await birthdays.PopupAsync(Birthdays, ct);
        }

        PrayerVisible = prayer365.CanSee(User.IsPlatformAdmin());
        Card = await cards.GetAsync(Data, User.IsPlatformAdmin(), ct);

        if (tenant.IsLobby)
            Waiting = (await accounts.MyMembershipsAsync(me, ct))
                .Where(m => m.Status == MembershipStatus.Requested && m.TenantActive).Select(m => m.TenantName).ToList();
        return Page();
    }

    /// <summary>둘러보기 화면 값(R-336): 오늘 콘텐츠 · 부서 사람 수는 그대로, 내 점수 · 진도 · 미션은 0 · "대기 중".</summary>
    public static HomeView GuestView(HomeView v) => new()
    {
        Today = v.Today, SeasonYear = v.SeasonYear, SeasonDay = v.SeasonDay, Participant = true, SeasonPoints = 0, Streak = 0, FullDays = 0, DayPoints = 0,
        NextMilestone = MissionService.NextMilestone(v.Rules, 0), Rules = v.Rules,
        VerseText = v.VerseText, VerseRef = v.VerseRef, WordRange = v.WordRange, WordTitle = v.WordTitle,
        Growth = ChallengeBible.Domain.Points.PointMath.GrowthOf(0, v.Rules.GrowthStages),
        ReciteRange = v.ReciteRange, ReciteStart = v.ReciteStart, ReciteEnd = v.ReciteEnd, PrayerTrait = v.PrayerTrait, PrayerRange = v.PrayerRange,
        Missions = v.Missions.Select(m => m with { Done = false, State = m.Has ? "대기 중" : m.State }).ToList(),
        NextMission = v.Missions.FirstOrDefault(m => m.Has) is { } n ? n with { Done = false, State = "대기 중" } : null,
        Reading = null, ResumeText = null, Week = v.Week.Select(w => w with { MissionPoints = 0, AllFour = false, Chapters = 0 }).ToList(),
        Dept = v.Dept,
    };
}
