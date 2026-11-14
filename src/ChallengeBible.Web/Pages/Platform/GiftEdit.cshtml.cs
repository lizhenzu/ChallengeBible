using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>
/// `/platform/gifts/edit/{id?}` 선물 상품 등록 · 고치기(확정 시안 플랫폼-선물상품 "등록 · 수정", R-278 · R-279).
/// 사진은 등록 · 고치기와 한 폼에서 함께(정사각형으로 잘라 JPG, 2026-11-14 사용자). 점수는 상품과 별개라 여기서 다루지 않는다.
/// 아래에 목록에서 내리기 · 다시 올리기 · 삭제(고른 기록이 있으면 안 됨).
/// </summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class GiftEditModel(GiftCatalogService catalog) : PageModel, ITitled
{
    public string Title => Id is null ? "공통 상품 등록" : "상품 고치기";

    [FromRoute(Name = "id")] public int? Id { get; set; }
    [BindProperty] public GiftItemInput Input { get; set; } = new();

    public GiftItem? Item { get; private set; }
    public int Picked { get; private set; }
    public string? TenantName { get; private set; }
    public string? SharedByName { get; private set; }
    public Dictionary<string, string> Errors { get; private set; } = [];
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    private string Me => User.UserName()!;

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        if (Id is not int id) return true;
        (Item, Picked, TenantName, SharedByName) = await catalog.GetAsync(Me, id, ct);
        return Item is not null;
    }

    public async Task<IActionResult> OnGetAsync(string? m, CancellationToken ct)
    {
        if (!await LoadAsync(ct)) return NotFound();
        if (Item is { } i) Input = new GiftItemInput { Name = i.Name, Description = i.Description, Price = i.Price.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), CoupangUrl = i.CoupangUrl };
        Message = m switch { "saved" => "저장했어요.", "nophoto" => "저장했어요. 사진은 올리지 못했어요(JPG 사진만 받아요).", "retired" => "목록에서 내렸어요. 아이에게 보이지 않아요.", "restored" => "다시 올렸어요.", _ => null };
        return Page();
    }

    /// <summary>저장: 칸 + (골랐으면) 사진을 한 번에. 칸이 틀리면 아무것도 저장하지 않는다. 사진만 틀리면 상품은 저장하고 알린다.</summary>
    public async Task<IActionResult> OnPostAsync(IFormFile? photo, CancellationToken ct)
    {
        if (!await LoadAsync(ct)) return NotFound();
        var (errors, id) = await catalog.SaveAsync(Me, Id, Input, ct);
        if (errors.Count > 0) { Errors = errors; return Page(); }
        if (photo is { Length: > 0 })
        {
            await using var stream = photo.OpenReadStream();
            if (await catalog.SaveImageAsync(Me, id, stream, photo.Length, ct) != GiftItemError.None)
                return Redirect($"/platform/gifts/edit/{id}?m=nophoto");
        }
        return Redirect($"/platform/gifts/edit/{id}?m=saved");
    }

    public async Task<IActionResult> OnPostRetireAsync(CancellationToken ct) => await StatusAsync(catalog.RetireAsync, "retired", ct);
    public async Task<IActionResult> OnPostRestoreAsync(CancellationToken ct) => await StatusAsync(catalog.RestoreAsync, "restored", ct);

    private async Task<IActionResult> StatusAsync(Func<string, int, CancellationToken, Task<GiftItemError>> act, string m, CancellationToken ct)
    {
        if (Id is not int id) return NotFound();
        return await act(Me, id, ct) == GiftItemError.None ? Redirect($"/platform/gifts/edit/{id}?m={m}") : NotFound();
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken ct)
    {
        if (Id is not int id || !await LoadAsync(ct)) return NotFound();
        var r = await catalog.DeleteAsync(Me, id, ct);
        if (r == GiftItemError.None) return Redirect($"/platform/gifts?s={Item!.Status}");
        Error = "고른 기록이 있는 상품은 지울 수 없어요. 대신 \"목록에서 내리기\"를 해 주세요.";
        return await ReshowAsync(ct);
    }

    private async Task<IActionResult> ReshowAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (Item is { } i) Input = new GiftItemInput { Name = i.Name, Description = i.Description, Price = i.Price.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), CoupangUrl = i.CoupangUrl };
        return Page();
    }

    public string Err(string field) => Errors.GetValueOrDefault(field) ?? "";
}
