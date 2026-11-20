using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace ChallengeBible.Web.Pages.Album;

/// <summary>`/{slug}/album/edit/{id?}` 앨범 만들기 · 고치기 · 지우기(ADR 0118): 이름 · 설명 · 촬영일 · 공개 범위 · 내려받기. 부서 관리자만.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class EditModel(AlbumService albums, TenantContext tenant) : AlbumPageBase, ITitled
{
    public string Title => Item is null ? "앨범 만들기" : "앨범 고치기";
    public TenantContext Tenant => tenant;
    public AlbumPage? Item { get; private set; }
    public string? TitleText { get; private set; }
    public string? Description { get; private set; }
    public DateOnly? EventDate { get; private set; }
    public string Visibility { get; private set; } = AlbumVisibility.Login;
    public bool AllowDownload { get; private set; } = true;
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        if (id is int i)
        {
            Item = await albums.GetAsync(i, Viewer, ct);
            if (Item is null) return NotFound();
            var a = Item.Album;
            (TitleText, Description, EventDate, Visibility, AllowDownload) = (a.Title, a.Description, a.EventDate, a.Visibility, a.AllowDownload);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id, string? title, string? description, DateOnly? eventDate, string? visibility, bool allowDownload, CancellationToken ct)
    {
        var (saved, error) = await albums.SaveAlbumAsync(Viewer, id, title, description, eventDate, visibility, allowDownload, ct);
        if (error is null) { TempData["Msg"] = id is null ? "앨범을 만들었어요. 사진을 올려 주세요." : "앨범을 고쳤어요."; return Redirect($"/{tenant.Current.Slug}/album/{saved}"); }
        Error = error;
        if (id is int i) Item = await albums.GetAsync(i, Viewer, ct);
        (TitleText, Description, EventDate, Visibility, AllowDownload) = (title, description, eventDate, visibility ?? AlbumVisibility.Login, allowDownload);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        var n = await albums.DeleteAlbumAsync(Viewer, id, ct);
        TempData["Msg"] = n < 0 ? "이미 지워진 앨범이에요." : $"앨범과 사진 {n}장을 지웠어요.";
        return Redirect($"/{tenant.Current.Slug}/album");
    }
}
