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
public class IndexModel(BirthdayListService birthdays, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "생일";
    public const string Up = "up", MonthTab = "month", YearTab = "year", None = "none";

    [BindProperty(SupportsGet = true)] public string? Tab { get; set; }
    [BindProperty(SupportsGet = true)] public int Days { get; set; } = BirthdayListService.DefaultDays;
    [BindProperty(SupportsGet = true)] public int M { get; set; }
    [BindProperty(SupportsGet = true)] public string? V { get; set; }
    [BindProperty(SupportsGet = true)] public int D { get; set; }
    [TempData] public string? Msg { get; set; }

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
        Board = await birthdays.BoardAsync(User.UserId(), ct);
        if (!BirthdayListService.DayChoices.Contains(Days)) Days = BirthdayListService.DefaultDays;
        if (M is < 1 or > 12) M = Board.Today.Month;
        if (D == 0 && M == Board.Today.Month) D = Board.Today.Day;
        return Page();
    }

    public async Task<IActionResult> OnPostBirthdayAsync(int pid, int? month, int? day, CancellationToken ct)
    {
        if (!CanSee(tenant)) return NotFound();
        var e = await birthdays.SetBirthdayAsync(pid, month, day, ct);
        Msg = e == PeopleError.None ? "생일을 넣었어요." : e == PeopleError.BadBirthday ? "월과 일을 맞게 골라 주세요." : PeopleService.Message(e);
        return Redirect($"/{Slug}/birthdays?Tab={None}");
    }
}
