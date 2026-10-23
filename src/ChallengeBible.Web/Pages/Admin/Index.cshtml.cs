using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin` 부서 관리 홈(확정 시안: 관리-홈). 영역 관리자만.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class IndexModel(AdminHomeService home, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "관리 홈";
    public TenantContext Tenant => tenant;
    public AdminHome Data { get; private set; } = null!;

    /// <summary>오래 안 한 친구 목록에서 한 번에 보여 줄 수(나머지는 "더 보기").</summary>
    public const int MissingShown = 6;

    /// <summary>오늘 안 한 친구를 반(그룹 라벨)으로 거른다. 없으면 전체, "-" 는 반 없음.</summary>
    [BindProperty(SupportsGet = true)] public string? Group { get; set; }
    [BindProperty(SupportsGet = true)] public bool More { get; set; }

    public IReadOnlyList<string> Groups { get; private set; } = [];
    public IReadOnlyList<MissingMember> Shown { get; private set; } = [];
    public int MissingTotal { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Data = await home.GetAsync(ct);
        Groups = Data.Missing.Select(m => m.Group).Where(g => g is not null).Select(g => g!).Distinct().Order().ToList();
        var list = string.IsNullOrEmpty(Group) ? Data.Missing
            : Group == "-" ? Data.Missing.Where(m => m.Group is null).ToList()
            : Data.Missing.Where(m => m.Group == Group).ToList();
        MissingTotal = list.Count;
        Shown = More ? list : list.Take(MissingShown).ToList();
    }

    public string MissUrl(string? group, bool more = false) =>
        $"/{tenant.Current.Slug}/admin?" + string.Join("&", new[] { group is { Length: > 0 } ? $"Group={Uri.EscapeDataString(group)}" : null, more ? "More=true" : null }.Where(x => x is not null));

    public static string AgoText(int? days) => days switch { null => "아직 없음", 1 => "어제", 0 => "오늘", _ => $"{days}일 전" };
    public static string AgoBadge(int? days) => days switch { null => "시작 전", >= 7 => "7일 넘음", _ => AgoText(days) };
}
