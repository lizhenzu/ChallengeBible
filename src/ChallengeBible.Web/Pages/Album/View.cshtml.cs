using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace ChallengeBible.Web.Pages.Album;

/// <summary>
/// `/{slug}/album/{id}` 앨범(ADR 0118, 확정 시안 사진첩 ③ · ④ · ⑤): 격자 · 크게 보기 · 선택해서 ZIP · 지우기 · 사진 올리기(한 장씩, 진행률 · 실패만 다시).
/// 볼 수 없는 앨범은 없는 것처럼 404.
/// </summary>
[RequestSizeLimit(AlbumService.MaxBytes + 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = AlbumService.MaxBytes + 1024 * 1024)]
public class ViewModel(AlbumService albums, TenantContext tenant) : AlbumPageBase, ITitled, IPublicTenantPage
{
    public string Title => Item?.Album.Title ?? "앨범";
    public TenantContext Tenant => tenant;
    public AlbumPage? Item { get; private set; }
    [TempData] public string? Msg { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        Item = await albums.GetAsync(id, Viewer, ct);
        return Item is null ? NotFound() : Page();
    }

    /// <summary>사진 한 장 올리기(fetch, 화면이 차례로 보낸다): JSON { ok, id, error }.</summary>
    public async Task<IActionResult> OnPostUploadAsync(int id, IFormFile? file, CancellationToken ct)
    {
        var r = await albums.UploadAsync(Viewer, id, file, ct);
        return new JsonResult(new { ok = r.Error is null, id = r.PhotoId, error = r.Error });
    }

    /// <summary>선택한 사진 지우기(교사는 본인이 올린 것만).</summary>
    public async Task<IActionResult> OnPostDeleteAsync(int id, List<int> ids, CancellationToken ct)
    {
        var n = await albums.DeletePhotosAsync(Viewer, id, ids ?? [], ct);
        Msg = n > 0 ? $"사진 {n}장을 지웠어요." : "지울 수 있는 사진이 없어요(교사는 본인이 올린 사진만 지워요).";
        return Redirect($"/{tenant.Current.Slug}/album/{id}");
    }

    /// <summary>대표 사진으로(부서 관리자).</summary>
    public async Task<IActionResult> OnPostCoverAsync(int id, int photo, CancellationToken ct)
    {
        if (!albums.IsManager(Viewer)) return Forbid();
        Msg = await albums.SetCoverAsync(Viewer, id, photo, ct) ? "대표 사진으로 정했어요." : "사진을 찾을 수 없어요.";
        return Redirect($"/{tenant.Current.Slug}/album/{id}");
    }

    /// <summary>ZIP 내려받기(ids 가 없으면 전체). 압축 파일을 서버에 만들지 않고 바로 보낸다.</summary>
    public async Task<IActionResult> OnGetZipAsync(int id, string? ids, CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        var pick = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s, out var n) ? n : 0).Where(n => n > 0).ToList();
        var page = await albums.GetAsync(id, Viewer, ct);
        if (page is null || !page.CanDownload) return NotFound();
        if (HttpContext.Features.Get<IHttpBodyControlFeature>() is { } body) body.AllowSynchronousIO = true;   // ZipArchive 가 끝에 동기로 쓴다
        Response.ContentType = "application/zip";
        var name = Uri.EscapeDataString($"{page.Album.Title}.zip");
        Response.Headers.ContentDisposition = $"attachment; filename=\"album.zip\"; filename*=UTF-8''{name}";
        await albums.ZipAsync(id, pick, Viewer, Response.Body, ct);
        return new EmptyResult();
    }
}
