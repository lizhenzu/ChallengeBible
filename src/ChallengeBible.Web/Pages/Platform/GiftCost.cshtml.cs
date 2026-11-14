using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>
/// `/platform/gift-cost?per=&amp;from=&amp;to=&amp;d=&amp;k=` 모든 부서 선물 총비용(확정 시안 선물-총비용 ②, ADR 0094 G-2 · 정한 것 13, R-294).
/// 기간 · 부서(d) · 종류(k), 종류별 · 부서별 합계, 엑셀(.xlsx). 플랫폼 관리자만.
/// </summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class GiftCostModel(GiftCostService cost) : PageModel, ITitled
{
    public string Title => "선물 총비용";

    [BindProperty(SupportsGet = true, Name = "per")] public string? Per { get; set; }
    [BindProperty(SupportsGet = true, Name = "from")] public string? From { get; set; }
    [BindProperty(SupportsGet = true, Name = "to")] public string? To { get; set; }
    [BindProperty(SupportsGet = true, Name = "d")] public int? Dept { get; set; }
    [BindProperty(SupportsGet = true, Name = "k")] public string? Kind { get; set; }

    public GiftCostSummary Summary { get; private set; } = null!;
    public IReadOnlyList<(int Id, string Name)> Tenants { get; private set; } = [];

    private GiftCostPeriod P => GiftCostService.Period(Per, From, To, cost.PlatformToday);

    public async Task OnGetAsync(CancellationToken ct)
    {
        var me = User.UserName()!;
        Tenants = await cost.TenantsAsync(me, ct);
        if (Dept is int d && Tenants.All(t => t.Id != d)) Dept = null;
        if (GiftCostService.KindRows.All(k => k.Key != Kind)) Kind = null;
        Summary = await cost.PlatformAsync(me, P, Dept, Kind, ct);
    }

    public async Task<IActionResult> OnGetExcelAsync(CancellationToken ct)
    {
        var p = P;
        return File(await cost.PlatformExcelAsync(User.UserName()!, p, Dept, Kind, ct), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", GiftCostService.FileName("모든부서", p));
    }

    public string Action => "/platform/gift-cost";
    public string SumLabel => (Dept is int d ? Tenants.FirstOrDefault(t => t.Id == d).Name : null ?? "모든 부서") + " · 받은 선물 금액 합계";

    /// <summary>기간을 바꿔도 부서 · 종류는 그대로.</summary>
    public IReadOnlyDictionary<string, string?> Keep => new Dictionary<string, string?> { ["d"] = Dept?.ToString(), ["k"] = Kind };

    public string Query(GiftCostPeriod p, int? dept) =>
        $"per={p.Key}&from={p.From:yyyy-MM-dd}&to={p.To:yyyy-MM-dd}" + (dept is int d ? $"&d={d}" : "") + (Kind is { } k ? $"&k={k}" : "");
}
