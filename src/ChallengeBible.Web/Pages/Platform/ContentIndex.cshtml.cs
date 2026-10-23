using ChallengeBible.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/content` → 말씀부터.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class ContentIndexModel : PageModel
{
    public IActionResult OnGet() => Redirect("/platform/content/word");
}
