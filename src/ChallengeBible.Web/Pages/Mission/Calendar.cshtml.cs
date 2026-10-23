using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Mission;

/// <summary>`/{slug}/calendar` — 달력은 내 점수 화면에 함께 있다(확정 시안).</summary>
public class CalendarModel(TenantContext tenant) : PageModel
{
    public IActionResult OnGet(string? month) =>
        Redirect($"/{tenant.Current.Slug}/points" + (string.IsNullOrEmpty(month) ? "" : $"?month={Uri.EscapeDataString(month)}"));
}
