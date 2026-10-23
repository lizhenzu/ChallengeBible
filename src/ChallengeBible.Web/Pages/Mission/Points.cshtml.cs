using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Mission;

/// <summary>
/// `/{slug}/points?month=2027-01` 내 점수(확정 시안: 말씀-내점수): 달력 42칸(R-70 ~ R-73) + 날짜별 표(R-76).
/// `/{slug}/calendar` 도 같은 화면으로 온다.
/// </summary>
public class PointsModel(MissionService missions, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "내 점수";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public string? Month { get; set; }

    public DateOnly First { get; private set; }
    public DateOnly Today { get; private set; }
    public IReadOnlyList<DayScore> Days { get; private set; } = [];
    public int? ParticipantId { get; private set; }
    public int SeasonPoints { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!tenant.Current.MissionEnabled) return NotFound();
        Today = missions.Today;
        First = DateOnly.TryParseExact((Month ?? "") + "-01", "yyyy-MM-dd", out var f) && f <= Today ? f : new DateOnly(Today.Year, Today.Month, 1);
        ParticipantId = await missions.ParticipantIdAsync(ct);
        if (ParticipantId is int p)
        {
            Days = await missions.MonthAsync(p, First.Year, First.Month, ct);
            SeasonPoints = (await missions.GetDayAsync(p, Today, ct)).SeasonPoints;
        }
        return Page();
    }

    /// <summary>R-70: 6주 × 7일 = 42칸, 일요일 시작.</summary>
    public IEnumerable<DateOnly> Cells()
    {
        var start = First.AddDays(-(int)First.DayOfWeek);
        return Enumerable.Range(0, 42).Select(start.AddDays);
    }

    public DayScore? ScoreOf(DateOnly d) => Days.FirstOrDefault(x => x.Date == d);
    public string MonthLink(int add) => $"/{tenant.Current.Slug}/points?month={First.AddMonths(add):yyyy-MM}";
    public bool HasNext => First.AddMonths(1) <= Today;
}
