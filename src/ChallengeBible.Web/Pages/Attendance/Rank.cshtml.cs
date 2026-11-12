using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Attendance;

/// <summary>
/// `/{slug}/attendance/rank?date=` 아이별 출석 등수 — 유년부 AdminStudentRank3Weeks 와 같은 모양 · 기준(5/9). 부서 관리자만.
/// 올해 첫 주일 ~ 기준 주일 출석 수로 공동 등수(1, 1, 2 …). 학생은 R-243 명단(지금 반 편성 + 미배정반).
/// </summary>
[TenantRoles(Roles.TenantAdmin)]
public class RankModel(AttendanceService attendance, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "아이별 출석 등수";
    public TenantContext Tenant => tenant;
    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }
    public AttendanceService.SundayReport Rep { get; private set; } = null!;
    public List<Row> Rows { get; private set; } = [];

    public sealed record Row(int Rank, string Name, string ClassName, string Group, int ClassOrder, int Count);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        Rep = await attendance.SundayReportAsync(Date, ct);
        Rows = Ranked(Rep);
        return Page();
    }

    /// <summary>출석 수 많은 순 공동 등수(DENSE), 같으면 이름 순.</summary>
    public static List<Row> Ranked(AttendanceService.SundayReport r)
    {
        var ko = StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false);
        var all = r.Classes.SelectMany((c, i) => c.Kids.Select(k => (k, c, i))).ToList();
        var ranks = all.Select(x => x.k.YearCount).Distinct().OrderByDescending(n => n).Select((n, i) => (n, i + 1)).ToDictionary(x => x.n, x => x.Item2);
        return all.Select(x => new Row(ranks[x.k.YearCount], x.k.Name, x.c.Name, x.c.Group, x.i, x.k.YearCount))
            .OrderBy(x => x.Rank).ThenBy(x => x.Name, ko).ToList();
    }
}
