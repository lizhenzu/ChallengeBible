using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Mission;

/// <summary>
/// `/{slug}/ranking?t=&amp;f=&amp;s=&amp;q=` 참여자 순위(확정 시안: 말씀-순위 "개인" · "가족팀", R-80 ~ R-84 · R-92 · R-94). 이 부서 승인 소속원 모두.
/// 가족팀 탭(t=team)은 기본 규칙 "가족팀" 스위치가 켜졌을 때만(0012).
/// </summary>
public class RankingModel(RankingService ranking, FamilyTeamService families, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "참여자";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true, Name = "t")] public string Tab { get; set; } = "person";
    [BindProperty(SupportsGet = true, Name = "f")] public string Kind { get; set; } = "all";
    [BindProperty(SupportsGet = true, Name = "s")] public string Sort { get; set; } = "pts";
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public Ranking Data { get; private set; } = null!;
    public IReadOnlyList<RankRow> Rows { get; private set; } = [];
    public FamilyRanking Families { get; private set; } = null!;
    public IReadOnlyList<FamilyRankTeam> Teams { get; private set; } = [];
    public bool TeamTab => Tab == "team" && Families.Enabled;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!tenant.Current.MissionEnabled) return NotFound();
        Data = await ranking.GetAsync(ct);
        if (!Data.Enabled) return NotFound();   // 기본 규칙의 "순위" 스위치
        Families = await families.RankingAsync(ct);
        if (TeamTab)
        {
            var q = Q?.Trim();
            var ko = StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false);
            var shown = Families.Teams.Where(t => string.IsNullOrEmpty(q) || t.Name.Contains(q) || t.Members.Any(m => m.Name.Contains(q)));
            Teams = (Sort == "name" ? shown.OrderBy(t => t.Name, ko) : shown.OrderBy(t => t.Rank)).ToList();
        }
        else Rows = RankingService.View(Data.Rows, Kind, Q, Sort).ToList();
        return Page();
    }

    public string Url(string? kind = null, string? sort = null) =>
        $"/{tenant.Current.Slug}/ranking?f={kind ?? Kind}&s={sort ?? Sort}" + (string.IsNullOrWhiteSpace(Q) ? "" : $"&q={Uri.EscapeDataString(Q)}");

    public string TeamUrl(string? sort = null) =>
        $"/{tenant.Current.Slug}/ranking?t=team&s={sort ?? Sort}" + (string.IsNullOrWhiteSpace(Q) ? "" : $"&q={Uri.EscapeDataString(Q)}");

    public string Last(DateOnly? d) => d is not { } x ? "아직 없음" : x == Data.Today ? "오늘" : x == Data.Today.AddDays(-1) ? "어제" : x.ToString("M월 d일", MissionPageBase.Ko);

    public static string Initial(string name) => name.Length > 1 ? name[1..2] : name;

    public static string KindLabel(RankKind k) => k switch { RankKind.Parent => "부모님", RankKind.Teacher => "선생님", _ => "" };

    /// <summary>가계도: 위 줄 = 학생이 아닌 구성원(부모님 · 조부모 등), 아래 줄 = 학생.</summary>
    public static bool IsKid(string relation) => relation == FamilyRelation.Student;
}
