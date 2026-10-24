using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/branding` 이름 · 색(확정 시안: 관리-이름색, 0033 · 0038). 영역 관리자만.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class BrandingModel(BrandingService branding, TenantContext tenant, TenantClock clock) : PageModel, ITitled
{
    public string Title => "이름 · 색";
    public TenantContext Tenant => tenant;

    [BindProperty] public string? AppName { get; set; }
    [BindProperty] public string? AppShortName { get; set; }
    [BindProperty] public string ThemeColor { get; set; } = "blue";
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    public IReadOnlyList<BrandingLog> Log { get; private set; } = [];
    public Dictionary<string, string> Errors { get; private set; } = [];
    public DateOnly Today { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var v = await LoadAsync(ct);
        (AppName, AppShortName, ThemeColor) = (v.AppName, v.AppShortName, v.ThemeColor);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var (changed, errors) = await branding.SaveAsync(User.AccountId()!.Value, AppName, AppShortName, ThemeColor, ct);
        if (errors.Count > 0)
        {
            Errors = errors;
            await LoadAsync(ct);
            return Page();
        }
        return RedirectToPage(new { slug = tenant.Current.Slug, Ok = changed ? "save" : "same" });
    }

    private async Task<BrandingView> LoadAsync(CancellationToken ct)
    {
        var v = await branding.GetAsync(ct);
        Log = v.Log;
        Today = clock.Today(tenant.Current.TimeZoneId);
        return v;
    }

    public string LocalDate(DateTime utc) =>
        TenantClock.ToTenantDate(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)), tenant.Current.TimeZoneId).ToString("yyyy.MM.dd");
}
