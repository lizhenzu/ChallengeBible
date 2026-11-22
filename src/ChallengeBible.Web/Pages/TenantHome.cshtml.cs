using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages;

/// <summary>`/{slug}` 부서 홈 메인(확정 시안: 홈메인, 0044). 부서 결정 · 소속 검사는 TenantPageFilter 가 한다.</summary>
public class TenantHomeModel(TenantContext tenant, FamilyTeamService teams, FamilyJoinService familyJoin, HomeService home, AccountService accounts,
    PhotoService photos, CopyService copy, TodoService todo, Prayer365Service prayer365, MessageService msgs, GiftOrderService orders, WorshipService worship, BulletinService bulletins, BirthdayGiftService birthdays) : PageModel, ITitled
{
    /// <summary>로그인할 때 한 번 "새 쪽지" 띠(ADR 0112, 확정 시안 쪽지 ①). 로그인이 남긴다.</summary>
    public const string MsgBandKey = "msg-band";
    public int NewMessages { get; private set; }
    /// <summary>팝업에 보일 새 쪽지(최근 3개, 확정 시안 쪽지-팝업).</summary>
    public IReadOnlyList<MsgRow> NewList { get; private set; } = [];
    /// <summary>내 사진 파일 이름(0078) — 없으면 이름 첫 글자.</summary>
    public string? MyPhoto { get; private set; }
    public string Title => "";
    public TenantContext Tenant => tenant;
    /// <summary>"우리 가족팀" 칸(확정 시안 홈메인) — 내가 든 활성 가족팀이 없으면 null.</summary>
    public FamilyHome? Family { get; private set; }
    /// <summary>받은 가족팀 초대(확정 시안 home-invite, R-212) — 말씀 찾기 위 배너. 팀이 있거나 오늘 "다음에 할래요"를 눌렀으면 비어 있다.</summary>
    public IReadOnlyList<FamilyReceivedInvite> Invites { get; private set; } = [];
    /// <summary>온가족 한 줄(확정 시안 온가족 "부서 홈 칸", R-177) — 온가족이 없으면 null.</summary>
    public bool IsGuardian => tenant.Has(Roles.Guardian);
    public HomeView Data { get; private set; } = null!;
    /// <summary>성경필사 카드(확정 시안 성경필사 ④): 필사를 켠 부서만, 아니면 null.</summary>
    public CopySummary? CopyData { get; private set; }
    /// <summary>자녀를 위한 365 기도 카드(ADR 0106, 확정 시안 365기도 ②): 학생 · 새친구가 아니면. (오늘 기도문, 우리 부서 오늘 기도한 사람, 내 기록)</summary>
    public (ChallengeBible.Infrastructure.Data.Prayer365 Item, Prayer365Stats Me)? PrayerCard { get; private set; }
    /// <summary>인사 카드 이름 아래 역할 칩(R-275, 확정 시안 C).</summary>
    public IReadOnlyList<RoleChip> RoleChips { get; private set; } = [];
    /// <summary>"누가 선물 받나요"(R-297): 미션 · 개인 선물 통합이라 부서 홈에(2026-11-18 사용자). 3명과 전체 수.</summary>
    public (IReadOnlyList<GiftBoardRow> Rows, int Total) Board { get; private set; } = ([], 0);
    /// <summary>승인된 소속이 둘 이상이면 머리말 "부서 ▼" → 부서 고르기(확정 6).</summary>
    public bool ManyTenants { get; private set; }
    /// <summary>임시 부서(0064, R-172)에서 승인을 기다리는 내 신청의 부서 이름. 비어 있으면 "내 부서 찾기" 띠.</summary>
    public IReadOnlyList<string> Waiting { get; private set; } = [];
    /// <summary>`/{slug}/today` — 임시 부서의 "미션 화면"(0068). 임시 부서 `/{slug}` 는 1 · 2단계(윗부분만)이고 미션은 여기서 한다.</summary>
    [FromRoute(Name = "view")] public string? Screen { get; set; }
    public bool TodayScreen => Screen == "today";
    /// <summary>로그인 안 한 사람의 둘러보기(R-336): 개인 정보 없이 부서 소개 · 오늘의 말씀 · 사람 수만.</summary>
    public bool Guest { get; private set; }
    /// <summary>예배 안내(ADR 0116, R-337): 말씀 찾기 위 카드. 없으면 카드를 숨긴다. 임시 부서는 없음.</summary>
    public IReadOnlyList<ChallengeBible.Infrastructure.Data.WorshipSchedule> Worship { get; private set; } = [];
    /// <summary>가장 최근 주보 날짜(ADR 0117) — 예배 카드 [주보 보기]. 없으면 null.</summary>
    public DateOnly? BulletinDate { get; private set; }
    /// <summary>교사 · 관리자: 주보가 없어도 [주보 올리기].</summary>
    public bool CanBulletin => bulletins.CanEdit;

    /// <summary>내 생일 선물(ADR 0123, R-346): 예배 안내 카드 바로 아래 카드. 성경미션 홈에서 옮겨 왔다(미션을 안 하는 아이도 보게).</summary>
    public IReadOnlyList<BirthdayGiftView> Birthdays { get; private set; } = [];
    /// <summary>홈을 열면 띄우는 "생일 축하해요!" 팝업 — 띄울 때가 아니면 null(R-346).</summary>
    public BirthdayGiftView? BirthdayPopup { get; private set; }

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
        if (TodayScreen && !tenant.IsLobby) return Redirect($"/{tenant.Current.Slug}");   // 일반 부서 홈은 한 화면 그대로
        if (User.UserId() is null)   // 로그인 안 한 사람(R-336, 2026-11-18 사용자): 둘러보기. 임시 부서 · 그 밖의 화면은 로그인으로
        {
            if (tenant.IsLobby || TodayScreen) return Redirect("/account/login?returnUrl=" + Uri.EscapeDataString(Request.Path + Request.QueryString));
            Guest = true;
            Worship = await worship.ListAsync(ct: ct);
            BulletinDate = await bulletins.LatestDateAsync(ct);
            Data = GuestView(await home.GetAsync(ct));   // 로그인 화면과 같은 모양, 내 기록은 0(2026-11-18 확정 시안)
            if (tenant.Current.CopyEnabled) CopyData = await copy.SummaryAsync(ct);   // 사람 기록은 계정마다라 로그인 안 하면 0
            return Page();
        }
        // 오늘 할 일(ADR 0100, R-309): 로그인 직후 할 일이 있고 안 보기 기간이 아니면 할 일 화면으로
        // 리디렉트 응답은 TempData 를 저절로 남기므로(IKeepTempDataResult) 직접 지운다 — 로그인 직후 한 번만
        if (TempData.Remove(ChallengeBible.Web.Pages.Account.LoginModel.TodoCheckKey) && await todo.ShouldRedirectAsync(ct)) return Redirect($"/{tenant.Current.Slug}/todo");
        var me = User.UserName()!;
        // 확정 시안 쪽지 ① · 쪽지-팝업: 로그인 직후 한 번 띠 + 팝업, 그 뒤로는 새 쪽지가 오면 홈을 새로 열 때(F5) 팝업 한 번(2026-11-17 사용자)
        var afterLogin = TempData.Remove(MsgBandKey);
        (var popN, NewList) = await MsgPopup.DecideAsync(HttpContext, msgs, me, afterLogin, ct);
        if (afterLogin || popN > 0) NewMessages = popN > 0 ? popN : await msgs.UnreadAsync(me, ct);
        MyPhoto = await photos.FileOfAsync(me, ct);
        if (tenant.Current.MissionEnabled) Family = await teams.MyFamilyAsync(ct);
        if (tenant.Current.MissionEnabled && Family is null) Invites = (await familyJoin.StateAsync(ct)).Invites.Where(i => !i.SnoozedToday).ToList();
        Data = await home.GetAsync(ct);
        if (!tenant.IsLobby) { Worship = await worship.ListAsync(ct: ct); BulletinDate = await bulletins.LatestDateAsync(ct); }
        if (!tenant.IsLobby && tenant.Has(Roles.Participant))   // 생일 선물(ADR 0123, R-346): 생일 30일 전이 되면 생긴다
        {
            await birthdays.EnsureAsync(ct);
            Birthdays = (await birthdays.MineAsync(ct)).Where(g => g.Kind == GiftKinds.Birthday).ToList();
            // 새 쪽지 팝업이 뜨는 때는 그다음에. 고르거나 [다음에 할래요]를 누르기 전까지는 홈을 열 때마다 뜬다
            if (NewList.Count == 0) BirthdayPopup = await birthdays.PopupAsync(Birthdays, ct);
        }
        if (tenant.Current.CopyEnabled) CopyData = await copy.SummaryAsync(ct);   // 임시 부서(승인 전)에도(ADR 0110)
        if (prayer365.CanSee(User.IsPlatformAdmin()))
            PrayerCard = (await prayer365.GetAsync(Prayer365Service.Md(prayer365.Today), ct), await prayer365.MyStatsAsync(ct));
        if (tenant.IsApproved && !tenant.IsLobby) Board = await orders.BoardAsync(GiftOrderService.BoardTake, ct);
        RoleChips = await home.RoleChipsAsync(User.UserId()!, ct);
        ManyTenants = (await accounts.WhereToGoAsync(me, ct)).ApprovedCount > 1;
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
