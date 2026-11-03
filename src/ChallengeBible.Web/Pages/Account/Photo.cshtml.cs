using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Account;

/// <summary>`/account/photo` 내 사진(ADR 0078 확정 시안 ②): 홈 동그라미 · 내 계정에서 들어와 올리기 · 바꾸기 · 지우기. 가장 중에는 바꾸지 않는다(0067).</summary>
public class PhotoModel(PhotoService photos, AccountService accounts) : PageModel, ITitled
{
    public string Title => "내 사진";

    [BindProperty(SupportsGet = true)] public string? Back { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }
    public IFormFile? Photo { get; set; }

    public string? File { get; private set; }
    public string Name { get; private set; } = "";
    public string? Error { get; private set; }
    private int Me => User.AccountId()!.Value;

    /// <summary>돌아갈 곳: 이 사이트 안 주소만(바깥 주소로 보내지 않는다).</summary>
    public string BackUrl => Back is { Length: > 1 } b && b[0] == '/' && b[1] != '/' && b[1] != '\\' ? b : "/account";

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    /// <summary>본인 사진 파일(부서 밖에서도 — 내 계정 화면).</summary>
    public async Task<IActionResult> OnGetImgAsync(CancellationToken ct)
    {
        if (photos.FullPath(await photos.FileOfAsync(Me, ct)) is not { } path) return NotFound();
        Response.Headers.CacheControl = "private, max-age=86400";
        return PhysicalFile(path, PhotoService.ContentType(path));
    }

    public async Task<IActionResult> OnPostAsync(IFormFile? photo, CancellationToken ct)
    {
        if (User.ImpersonatorId() is not null) return Forbid();
        Error = await photos.SaveAsync(Me, photo, Me, ct: ct);
        if (Error is not null) { await LoadAsync(ct); return Page(); }
        return Redirect($"/account/photo?ok=1&back={Uri.EscapeDataString(BackUrl)}");
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken ct)
    {
        if (User.ImpersonatorId() is not null) return Forbid();
        await photos.DeleteAsync(Me, Me, ct: ct);
        return Redirect($"/account/photo?ok=0&back={Uri.EscapeDataString(BackUrl)}");
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        File = await photos.FileOfAsync(Me, ct);
        Name = (await accounts.ProfileAsync(Me, ct)).Name;
    }
}
