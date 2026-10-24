using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/content/{kind}?date=` 부서 기도(그날) · 암송(그 주) 덮어쓰기(확정 시안: 관리-암송기도입력). 영역 관리자만.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class ContentEditModel(TenantContentService contents, TenantContext tenant) : PageModel, ITitled
{
    public string Title => Kind == ContentKind.Prayer ? "우리 부서 기도" : "우리 부서 암송";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public string KindName { get; set; } = "prayer";
    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }
    [BindProperty] public ContentInput Input { get; set; } = new();

    public ContentKind Kind => KindName == "recite" ? ContentKind.Recite : ContentKind.Prayer;
    public TenantContentEdit Edit { get; private set; } = null!;
    public FieldErrors Errors { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (KindName is not ("prayer" or "recite")) return NotFound();
        Edit = await contents.LoadAsync(Kind, Date ?? contents.Today, ct);
        Input = Edit.Input;
        return Page();
    }

    public Task<IActionResult> OnPostPublishAsync(CancellationToken ct) => SaveAsync(true, ct);
    public Task<IActionResult> OnPostDraftAsync(CancellationToken ct) => SaveAsync(false, ct);

    public async Task<IActionResult> OnPostRevertAsync(CancellationToken ct)
    {
        await contents.RevertAsync(Kind, Date ?? contents.Today, ct);
        return RedirectToPage(new { slug = tenant.Current.Slug, KindName, Date, Ok = "revert" });
    }

    private async Task<IActionResult> SaveAsync(bool publish, CancellationToken ct)
    {
        var date = Date ?? contents.Today;
        Errors = await contents.SaveAsync(User.AccountId()!.Value, Kind, date, Input, publish, ct);
        if (Errors.Ok) return RedirectToPage(new { slug = tenant.Current.Slug, KindName, Date = date, Ok = publish ? "publish" : "draft" });
        Edit = await contents.LoadAsync(Kind, date, ct);
        return Page();
    }

    public string? E(string key) => Errors.GetValueOrDefault(key);

    public string CalendarUrl => $"/{tenant.Current.Slug}/admin/content?month={Edit.From:yyyy-MM}&date={(Date ?? Edit.From):yyyy-MM-dd}#dT";
}
