using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Birthdays;

/// <summary>
/// `/{slug}/birthdays` 생일(ADR 0121, R-344, 확정 시안 생일-시안): 교사 · 관리자만. 탭 = 다가오는(기본, 7 · 14 · 30 · 60일) · 월별(달력 · 명단) · 연간(인쇄) · 생일 없음(바로 넣기).
/// 선물은 상태만 — 사기 · 전하기는 관리 › 선물 처리.
/// </summary>
public class IndexModel(BirthdayListService birthdays, BirthdayGiftService gifts, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "생일";
    public const string Up = "up", MonthTab = "month", YearTab = "year", None = "none";

    [BindProperty(SupportsGet = true)] public string? Tab { get; set; }
    [BindProperty(SupportsGet = true)] public int Days { get; set; } = BirthdayListService.DefaultDays;
    [BindProperty(SupportsGet = true)] public int M { get; set; }
    [BindProperty(SupportsGet = true)] public string? V { get; set; }
    [BindProperty(SupportsGet = true)] public int D { get; set; }
    [TempData] public string? Msg { get; set; }
    /// <summary>선생님이 고르기(ADR 0122): 이 선물의 고르기 창을 연다.</summary>
    [BindProperty(SupportsGet = true, Name = "pick")] public long? PickId { get; set; }
    /// <summary>링크 창(선물 처리와 같은 팝업).</summary>
    [BindProperty(SupportsGet = true, Name = "link")] public long? LinkId { get; set; }
    public BirthdayGiftView? PickGift { get; private set; }
    public IReadOnlyList<TeamPickItem> PickItems { get; private set; } = [];
    public (BirthdayGiftView View, string Url, string Text)? Share { get; private set; }

    /// <summary>지금 보는 탭 · 기간 · 달을 지키는 주소(버튼을 누른 뒤 돌아올 곳).</summary>
    public string Back(long? pick = null, long? link = null) =>
        $"/{Slug}/birthdays?Tab={Current}&Days={Days}&M={M}" + (V is null ? "" : "&V=" + V) + (D > 0 ? "&D=" + D : "") + (pick is long p ? "&pick=" + p : "") + (link is long l ? "&link=" + l : "");

    public TenantContext Tenant => tenant;
    public string Slug => tenant.Current.Slug;
    public BirthdayBoard Board { get; private set; } = null!;
    public string Current => Tab is MonthTab or YearTab or None ? Tab : Up;
    public bool CalendarView => V != "list";

    /// <summary>교사 · 관리자만(R-344): 부서 관리자, 교사 꼬리표, 역할이 교사인 사람.</summary>
    public static bool CanSee(TenantContext t) => !t.IsLobby && t.IsApproved && (t.IsStaff || UserRole.KindOf(t.RoleCode) == UserRoleKind.Teacher);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!CanSee(tenant)) return NotFound();
        Board = await birthdays.BoardAsync(User.UserId(), ct, detail: Current is Up or MonthTab);
        if (!BirthdayListService.DayChoices.Contains(Days)) Days = BirthdayListService.DefaultDays;
        if (M is < 1 or > 12) M = Board.Today.Month;
        if (D == 0 && M == Board.Today.Month) D = Board.Today.Day;
        if (PickId is long pid) (PickGift, PickItems) = await gifts.StaffItemsAsync(pid, ct);
        if (LinkId is long lid && await gifts.ShareAsync(User.UserName()!, lid, ct) is { } sh)
        {
            var url = $"{Request.Scheme}://{Request.Host}/gift/{sh.Token}";
            Share = (sh.View, url, BirthdayGiftService.ShareText(sh.View, url, sh.Linked));
        }
        return Page();
    }

    /// <summary>선생님이 아이 대신 고르기 · 바꾸기(R-345): 교사 · 관리자 모두.</summary>
    public async Task<IActionResult> OnPostPickAsync(long gift, int item, CancellationToken ct)
    {
        if (!CanSee(tenant)) return NotFound();
        var e = await gifts.StaffPickAsync(User.UserName()!, User.DisplayName() ?? User.UserName()!, gift, item, ct);
        Msg = e == TeamPickError.None ? "골랐어요 · 선물 처리 \"살 것\"으로 넘어갔어요" : e == TeamPickError.TooExpensive ? "고를 수 없는 상품이에요. 다시 골라 주세요." : "이 선물은 지금 고를 수 없어요(이미 샀거나 안 주기로 했어요).";
        return Redirect(Back() + "#g" + gift);
    }

    /// <summary>[링크 복사] · [새 링크]: 링크를 만들고 선물 처리와 같은 팝업으로 보여 준다.</summary>
    public async Task<IActionResult> OnPostLinkAsync(long gift, bool fresh, CancellationToken ct)
    {
        if (!CanSee(tenant)) return NotFound();
        if (await gifts.LinkAsync(User.UserName()!, gift, fresh, ct) is null) { Msg = "이 선물은 링크를 만들 수 없어요."; return Redirect(Back()); }
        return Redirect(Back(link: gift) + "#g" + gift);
    }

    /// <summary>[선물 안 주기] · [다시 주기](예전 건너뛰기 · 되돌리기, 2026-11-20 사용자 "쉬운 말로").</summary>
    public async Task<IActionResult> OnPostSkipAsync(long gift, bool skip, CancellationToken ct)
    {
        if (!CanSee(tenant)) return NotFound();
        Msg = await gifts.SkipAsync(User.UserName()!, gift, skip, ct) ? (skip ? "이번 생일 선물은 안 주기로 했어요. [다시 주기]로 되돌릴 수 있어요." : "다시 선물을 주기로 했어요.") : "바꿀 수 없어요.";
        return Redirect(Back() + "#g" + gift);
    }

    public async Task<IActionResult> OnPostBirthdayAsync(int pid, int? month, int? day, CancellationToken ct)
    {
        if (!CanSee(tenant)) return NotFound();
        var e = await birthdays.SetBirthdayAsync(pid, month, day, ct);
        Msg = e == PeopleError.None ? "생일을 넣었어요." : e == PeopleError.BadBirthday ? "월과 일을 맞게 골라 주세요." : PeopleService.Message(e);
        return Redirect($"/{Slug}/birthdays?Tab={None}");
    }
}
