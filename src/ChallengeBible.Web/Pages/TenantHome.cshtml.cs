using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages;

/// <summary>`/{slug}` 부서 홈 자리. 부서 결정 · 소속 검사는 TenantPageFilter 가 한다. 홈 내용은 미션 단계에서 채운다.</summary>
public class TenantHomeModel(TenantContext tenant) : PageModel, ITitled
{
    public string Title => tenant.Current.Name;
    public TenantContext Tenant => tenant;

    public void OnGet() { }
}
