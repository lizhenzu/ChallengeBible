using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Mission;

/// <summary>
/// `/{slug}/roster?f=&amp;q=&amp;s=&amp;o=` 참여자 화면(2026-11-27 사용자 민트 시안 「참여자」, R-214). 이 부서 승인 소속원 가운데 이번 시즌 점수가 1점 이상인 사람.
/// f = student · parent · teacher(구분, 기본 전체) · q = 이름 · 아이디 · s = score(점수순, 기본) · date(수행일자순) · name(이름순) · o = asc · desc.
/// 같은 정렬 버튼을 다시 누르면 방향이 바뀐다. 점수가 같으면 이름 가나다순. 임시 부서 · 미션을 끈 부서는 404.
/// </summary>
public class RosterModel(RosterService roster, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "참여자";
    public TenantContext Tenant => tenant;
    public string Slug => tenant.Current.Slug;

    [BindProperty(SupportsGet = true, Name = "f")] public string Kind { get; set; } = "all";
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true, Name = "s")] public string Sort { get; set; } = "score";
    [BindProperty(SupportsGet = true, Name = "o")] public string? Order { get; set; }

    public ParticipantBoard Data { get; private set; } = null!;
    /// <summary>구분 버튼의 숫자 — 구분 · 찾기로 거르기 전 전체.</summary>
    public IReadOnlyList<ParticipantRow> All => Data.People;
    /// <summary>구분 · 찾기로 거르고 정렬한 사람.</summary>
    public IReadOnlyList<ParticipantRow> Shown { get; private set; } = [];

    public string SortKey => Sort is "date" or "name" ? Sort : "score";
    /// <summary>방향: 점수 · 수행일자는 기본 내림(높은 · 최근 먼저), 이름은 기본 오름(가나다).</summary>
    public bool Asc => Order is "asc" or "desc" ? Order == "asc" : SortKey == "name";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby || !tenant.Current.MissionEnabled) return NotFound();
        Data = await roster.ParticipantsAsync(ct);
        var q = Q?.Trim();
        var ko = StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false);
        var list = Data.People.Where(p => KindOf(Kind) is not { } k || p.Kind == k)
            .Where(p => string.IsNullOrEmpty(q) || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || p.UserName.Contains(q, StringComparison.OrdinalIgnoreCase));
        // 실제 값으로 정렬한다(「오늘」 · 「어제」 같은 글자가 아니라 날짜 · 점수) — 같으면 이름 가나다순
        IOrderedEnumerable<ParticipantRow> ordered = SortKey switch
        {
            "date" => Asc ? list.OrderBy(p => p.LastDay ?? DateOnly.MinValue) : list.OrderByDescending(p => p.LastDay ?? DateOnly.MinValue),
            "name" => Asc ? list.OrderBy(p => p.Name, ko) : list.OrderByDescending(p => p.Name, ko),
            _ => Asc ? list.OrderBy(p => p.Points) : list.OrderByDescending(p => p.Points),
        };
        Shown = (SortKey == "name" ? ordered : ordered.ThenBy(p => p.Name, ko)).ToList();
        return Page();
    }

    private static RankKind? KindOf(string? f) => f switch { "student" => RankKind.Student, "parent" => RankKind.Parent, "teacher" => RankKind.Teacher, _ => null };

    public static string KindKey(RankKind k) => k switch { RankKind.Parent => "parent", RankKind.Teacher => "teacher", _ => "student" };
    public static string KindName(RankKind k) => k switch { RankKind.Parent => "부모님", RankKind.Teacher => "선생님", _ => "학생" };
    public static string SortName(string key) => key switch { "date" => "수행일자순", "name" => "이름순", _ => "점수순" };

    /// <summary>구분 · 찾기 · 정렬을 함께 싣는 주소. sort 를 주면 그 버튼을 누른 것(같은 버튼이면 방향 반전, 다른 버튼이면 기본 방향).</summary>
    public string Url(string? kind = null, string? sort = null)
    {
        var f = kind ?? Kind;
        var s = sort ?? SortKey;
        var asc = sort is null ? Asc : sort == SortKey ? !Asc : s == "name";
        var parts = new List<string>();
        if (f is "student" or "parent" or "teacher") parts.Add($"f={f}");
        if (!string.IsNullOrWhiteSpace(Q)) parts.Add($"q={Uri.EscapeDataString(Q)}");
        if (s != "score") parts.Add($"s={s}");
        if (asc != (s == "name")) parts.Add($"o={(asc ? "asc" : "desc")}");
        return $"/{Slug}/roster" + (parts.Count == 0 ? "" : "?" + string.Join("&", parts));
    }

    public static string LastText(DateOnly? last, DateOnly today) =>
        last is not { } d ? "–" : d == today ? "오늘" : d == today.AddDays(-1) ? "어제" : d.ToString("M월 d일");
}
