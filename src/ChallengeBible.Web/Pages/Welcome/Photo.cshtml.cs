using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Welcome;

/// <summary>`/{slug}/welcome/photo` 소개 화면 대표 사진 열기(ADR 0140): 이 부서의 것만(다른 부서 · 사진 없음은 404). 누구나.</summary>
public class PhotoModel(IntroService intro, TenantContext tenant) : PageModel, IPublicTenantPage
{
    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby || await intro.OpenPhotoAsync(ct) is not { } f) return NotFound();
        Response.Headers.CacheControl = "private, max-age=3600";
        return new PhysicalFileResult(f.Path, f.Type);
    }
}
