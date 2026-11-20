using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace ChallengeBible.Web.Pages.Album;

/// <summary>`/{slug}/album` 사진첩 — 앨범 목록(ADR 0118, 확정 시안 사진첩 ②): 볼 수 있는 앨범만, 검색 · 정렬. 앨범 만들기는 부서 관리자.</summary>
public class IndexModel(AlbumService albums, TenantContext tenant) : AlbumPageBase, ITitled, IPublicTenantPage
{
    public string Title => "사진첩";
    public TenantContext Tenant => tenant;
    public IReadOnlyList<AlbumCard> Items { get; private set; } = [];
    public bool CanManage { get; private set; }
    public long Used { get; private set; }
    public long Quota => albums.QuotaBytes;
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true, Name = "s")] public string? Sort { get; set; }
    [TempData] public string? Msg { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        Items = await albums.ListAsync(Viewer, Q, Sort, ct);
        CanManage = albums.IsManager(Viewer);
        if (CanManage) Used = await albums.UsedAsync(ct);
        return Page();
    }
}
