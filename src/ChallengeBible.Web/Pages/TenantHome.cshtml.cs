using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages;

/// <summary>`/{slug}` 부서 홈 자리. 5단계(부서 경로 · 미션)에서 채운다.</summary>
public class TenantHomeModel : PageModel, ITitled
{
    public string Title => "부서 홈";
    public string Slug { get; private set; } = "";

    public void OnGet(string slug) => Slug = slug;
}
