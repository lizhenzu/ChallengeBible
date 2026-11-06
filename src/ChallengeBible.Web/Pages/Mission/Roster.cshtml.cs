using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Mission;

/// <summary>
/// `/{slug}/roster?t=&amp;f=&amp;q=` 부서 명단(확정 시안: dept-roster, ADR 0085, R-214). 이 부서 승인 소속원 모두.
/// t = all(전체, 기본) · participant(참여자), f = student · parent · teacher. 임시 부서 · 미션을 끈 부서는 404.
/// </summary>
public class RosterModel(RosterService roster, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "명단";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true, Name = "t")] public string Tab { get; set; } = "all";
    [BindProperty(SupportsGet = true, Name = "f")] public string Kind { get; set; } = "all";
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public Roster Data { get; private set; } = null!;
    public bool ParticipantTab => Tab == "participant";
    /// <summary>탭에 속한 모든 사람(걸러 보기 전) — 구분 버튼의 숫자.</summary>
    public IReadOnlyList<RosterPerson> InTab { get; private set; } = [];
    /// <summary>구분 · 찾기까지 거른 사람.</summary>
    public IReadOnlyList<RosterPerson> Shown { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby || !tenant.Current.MissionEnabled) return NotFound();
        Data = await roster.GetAsync(ct);
        InTab = ParticipantTab ? Data.People.Where(p => p.IsParticipant).ToList() : Data.People;
        var q = Q?.Trim();
        Shown = InTab.Where(p => KindOf(Kind) is not { } k || p.Kind == k)
            .Where(p => string.IsNullOrEmpty(q) || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Email.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        return Page();
    }

    private static RankKind? KindOf(string? f) => f switch { "student" => RankKind.Student, "parent" => RankKind.Parent, "teacher" => RankKind.Teacher, _ => null };

    public static string KindKey(RankKind k) => k switch { RankKind.Parent => "parent", RankKind.Teacher => "teacher", _ => "student" };
    public static string KindName(RankKind k) => k switch { RankKind.Parent => "부모님", RankKind.Teacher => "선생님", _ => "학생" };

    /// <summary>탭 · 구분 · 찾기 글자를 함께 싣는 주소.</summary>
    public string Url(string? tab = null, string? kind = null)
    {
        var t = tab ?? Tab;
        var f = tab is not null ? "all" : kind ?? Kind;   // 탭을 바꾸면 구분은 처음으로
        var parts = new List<string>();
        if (t == "participant") parts.Add("t=participant");
        if (f is "student" or "parent" or "teacher") parts.Add($"f={f}");
        if (!string.IsNullOrWhiteSpace(Q)) parts.Add($"q={Uri.EscapeDataString(Q)}");
        return $"/{tenant.Current.Slug}/roster" + (parts.Count == 0 ? "" : "?" + string.Join("&", parts));
    }

    public static string Initial(string name) => name.Length > 1 ? name[1..2] : name;
}
