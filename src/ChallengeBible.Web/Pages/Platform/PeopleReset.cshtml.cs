using ChallengeBible.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>옛 주소 `/platform/people-reset`: 완전 초기화(ADR 0102)로 합쳤다.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class PeopleResetModel : PageModel
{
    public IActionResult OnGet() => Redirect("/platform/reset");
}
