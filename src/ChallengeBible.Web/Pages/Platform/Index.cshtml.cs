using ChallengeBible.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform` — 플랫폼 홈은 뒤 단계. 지금은 부서 관리로 보낸다.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Platform/Tenants");
}
