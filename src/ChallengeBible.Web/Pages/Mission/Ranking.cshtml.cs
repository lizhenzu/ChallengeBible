using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Mission;

/// <summary>`/{slug}/ranking?f=&amp;s=&amp;q=` 참여자 순위(확정 시안: 말씀-순위 "개인", R-80 ~ R-84). 이 부서 승인 소속원 모두. 가족팀 탭은 가족팀이 생기면.</summary>
public class RankingModel(RankingService ranking, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "참여자";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true, Name = "f")] public string Kind { get; set; } = "all";
    [BindProperty(SupportsGet = true, Name = "s")] public string Sort { get; set; } = "pts";
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public Ranking Data { get; private set; } = null!;
    public IReadOnlyList<RankRow> Rows { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!tenant.Current.MissionEnabled) return NotFound();
        Data = await ranking.GetAsync(ct);
        if (!Data.Enabled) return NotFound();   // 기본 규칙의 "순위" 스위치
        Rows = RankingService.View(Data.Rows, Kind, Q, Sort).ToList();
        return Page();
    }

    public string Url(string? kind = null, string? sort = null) =>
        $"/{tenant.Current.Slug}/ranking?f={kind ?? Kind}&s={sort ?? Sort}" + (string.IsNullOrWhiteSpace(Q) ? "" : $"&q={Uri.EscapeDataString(Q)}");

    public string Last(DateOnly? d) => d is not { } x ? "아직 없음" : x == Data.Today ? "오늘" : x == Data.Today.AddDays(-1) ? "어제" : x.ToString("M월 d일", MissionPageBase.Ko);

    public static string Initial(string name) => name.Length > 1 ? name[1..2] : name;

    public static string KindLabel(RankKind k) => k switch { RankKind.Parent => "부모님", RankKind.Teacher => "선생님", _ => "" };
}
