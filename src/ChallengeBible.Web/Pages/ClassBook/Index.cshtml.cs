using ChallengeBible.Domain.Time;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.ClassBook;

/// <summary>
/// `/{slug}/classbook` 교적부 처음(R-361, 확정 시안 교적부): 모든 반 · 담임 · 학생 수 · 전체 반에서 이름 찾기. 교사 · 관리자만 — 학생 · 학부모는 메뉴에도 없고 주소로도 못 연다.
/// </summary>
public class IndexModel(RosterBookService book, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "교적부";
    public TenantContext Tenant => tenant;
    public string Slug => tenant.Current.Slug;
    [BindProperty(SupportsGet = true, Name = "searchName")] public string? Search { get; set; }
    public BookHome Home { get; private set; } = null!;
    public int Year { get; private set; }

    /// <summary>교사 · 관리자(인명 검색 · 생일자 상세와 같은 기준).</summary>
    public static bool CanUse(TenantContext t) => People.IndexModel.CanUse(t);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!CanUse(tenant)) return NotFound();
        Year = TenantClock.ToTenantDate(DateTimeOffset.UtcNow, tenant.Current.TimeZoneId).Year;
        Home = await book.HomeAsync(Search, ct);
        return Page();
    }
}
