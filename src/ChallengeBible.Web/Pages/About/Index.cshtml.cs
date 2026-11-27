using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.About;

/// <summary>`/{slug}/about` 부서 소개 보기(ADR 0142, R-377): 부서가 입력한 소개 글만(예배 안내 없음). 누구나(손님 포함), 이 부서 것만. 글이 없으면 준비중.</summary>
public class IndexModel(IntroService intro, TenantContext tenant) : PageModel, ITitled, IPublicTenantPage
{
    public string Title => $"{PlatformTodayService.ShortDept(tenant.Current.Name)} 소개";
    public TenantContext Tenant => tenant;
    public TenantIntro? Item { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        Item = await intro.GetAsync(ct: ct);
        return Page();
    }
}
