using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace ChallengeBible.Web.Pages.Album;

/// <summary>`/{slug}/album/photo/{id}` 사진 파일(ADR 0118): ?t=1 썸네일, ?dl=1 내려받기(허용일 때만). 볼 수 있는 앨범의 이 부서 사진만, 아니면 404.</summary>
public class PhotoModel(AlbumService albums, TenantContext tenant) : AlbumPageBase, IPublicTenantPage
{
    public async Task<IActionResult> OnGetAsync(int id, string? t, string? dl, CancellationToken ct)
    {
        bool thumb = t is "1" or "true", download = dl is "1" or "true";   // 주소에는 ?t=1 · ?dl=1 로 쓴다
        if (tenant.IsLobby || await albums.OpenAsync(id, thumb, download, Viewer, ct) is not { } f) return NotFound();
        Response.Headers.CacheControl = "private, max-age=86400";
        return download ? new PhysicalFileResult(f.Path, f.Type) { FileDownloadName = f.Name } : new PhysicalFileResult(f.Path, f.Type);
    }
}
