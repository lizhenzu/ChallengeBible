using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Account;

public class DeniedModel : PageModel, ITitled
{
    public string Title => "볼 수 없음";

    public void OnGet() { }
}
