using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/intro` 부서 소개 화면 고치기(ADR 0140, R-369): 제목 · 소개 글 · 대표 사진. 부서 관리자만.
/// 2026-11-27(ADR 0142): 모든 부서가 직접 입력한다 — 줄이 없으면 처음 저장할 때 만든다.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class IntroModel(IntroService intro, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "소개 화면";
    public TenantContext Tenant => tenant;
    public TenantIntro Item { get; private set; } = null!;
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }
    public FieldErrors Errors { get; private set; } = new();
    public IntroInput? Last { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Item = await intro.GetAsync(includeHidden: true, ct) ?? new TenantIntro { TenantId = tenant.Current.Id, Title = tenant.Current.Name };   // 줄이 없으면 처음 저장할 때 만든다(ADR 0142)
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(string? title, string? body, bool active, bool removePhoto, IFormFile? photo, CancellationToken ct)
    {
        var input = new IntroInput(title, body, active, removePhoto);
        try
        {
            var errors = await intro.SaveAsync(User.UserName()!, input, photo, ct);
            if (errors.Ok) return Redirect($"/{tenant.Current.Slug}/admin/intro?ok=save");
            (Errors, Last) = (errors, input);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        Item = await intro.GetAsync(includeHidden: true, ct) ?? new TenantIntro { TenantId = tenant.Current.Id, Title = tenant.Current.Name };
        return Page();
    }
}
