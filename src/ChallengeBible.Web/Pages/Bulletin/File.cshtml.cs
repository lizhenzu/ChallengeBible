using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Bulletin;

/// <summary>`/{slug}/bulletin/file/{id}` 주보 파일 열기(ADR 0117): 이 부서의 파일만(다른 부서 번호면 404). 사진은 화면에, PDF 는 새 탭에서.</summary>
public class FileModel(BulletinService bulletins, TenantContext tenant) : PageModel, IPublicTenantPage
{
    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        if (tenant.IsLobby || await bulletins.OpenAsync(id, ct) is not { } f) return NotFound();
        Response.Headers.CacheControl = "private, max-age=3600";
        return new PhysicalFileResult(f.Path, f.Type) { EnableRangeProcessing = true };
    }
}
