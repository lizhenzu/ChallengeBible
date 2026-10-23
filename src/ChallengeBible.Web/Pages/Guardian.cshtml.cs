using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages;

/// <summary>
/// `/{slug}/guardian?child=&amp;date=` 보호자 화면(확정 시안: 보호자, 1차로 당김).
/// 연결된 자녀(여럿이면 고르기) — 올해 점수 · 연속 · 오늘 미션 · 이번 주 · 달력 · 그날 한 일. 보기만(R-132).
/// </summary>
[TenantRoles(Roles.Guardian)]
public class GuardianModel(GuardianService guardian, MissionService missions, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "보호자";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public int? Child { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }

    public List<ChildRow> Children { get; private set; } = [];
    public ChildRow? Current { get; private set; }
    public DateOnly Today { get; private set; }
    public DateOnly Day { get; private set; }
    public MissionDay? Data { get; private set; }
    public MissionDay? TodayData { get; private set; }
    public IReadOnlyList<(DateOnly Date, int Points)> Week { get; private set; } = [];
    public IReadOnlyList<DayScore> Month { get; private set; } = [];
    public List<DayLogLine> Log { get; private set; } = [];
    public int Streak { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!tenant.Current.MissionEnabled) return NotFound();
        Children = await guardian.ChildrenAsync(ct);
        Today = missions.Today;
        if (Children.Count == 0) return Page();

        Current = Child is int c ? Children.FirstOrDefault(x => x.ParticipantId == c) : Children[0];
        if (Current is null) return NotFound();   // 내 자녀가 아님(R-132)
        var pid = Current.ParticipantId;

        Day = Date is { } d && d <= Today ? d : Today;
        TodayData = await missions.GetDayAsync(pid, Today, ct);
        Data = Day == Today ? TodayData : await missions.GetDayAsync(pid, Day, ct);
        var weekStart = Domain.Time.Week.Of(Today, TodayData.Rules.WeekStart).Start;
        Week = await missions.RecentDaysAsync(pid, weekStart.AddDays(6), ct);
        Month = await missions.MonthAsync(pid, Day.Year, Day.Month, ct);
        Log = await guardian.DayLogAsync(pid, Day, ct);
        Streak = await guardian.StreakAsync(pid, ct);
        return Page();
    }

    public string Link(DateOnly? d = null) =>
        $"/{tenant.Current.Slug}/guardian?child={Current?.ParticipantId}" + (d is { } x && x != Today ? $"&date={x:yyyy-MM-dd}" : "");
}
