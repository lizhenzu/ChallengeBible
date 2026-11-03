using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages;

/// <summary>
/// `/{slug}/photo/{계정}` 얼굴 사진 보기(ADR 0078, R-201). 이 부서에 승인된 사람만(TenantPageFilter),
/// 이 부서에 승인된 소속이 있는 계정의 사진만. 임시 부서는 본인 것만(모르는 사람끼리 얼굴이 보이지 않게).
/// </summary>
public class PhotoModel(PhotoService photos, TenantContext tenant) : PageModel
{
    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        if (tenant.IsLobby && id != User.AccountId()) return NotFound();
        if (photos.FullPath(await photos.FileInTenantAsync(id, ct)) is not { } path) return NotFound();
        Response.Headers.CacheControl = "private, max-age=86400";   // 주소에 파일 이름(?v=)이 붙어 바뀌면 새로 받는다
        return PhysicalFile(path, PhotoService.ContentType(path));
    }
}
