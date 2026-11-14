using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>
/// `/{slug}/admin/gifts/edit/{id?}` 부서 상품 등록 · 고치기(확정 시안 관리-선물 ②, 폼은 플랫폼과 같음 _GiftItemForm).
/// 부서는 자기 부서 상품과 자기가 공통에 올린 상품만 고친다(R-277). 내리기 · 삭제는 자기 부서 상품만.
/// </summary>
[TenantRoles(Roles.TenantAdmin)]
public class GiftEditModel(GiftCatalogService catalog, TenantContext tenant) : PageModel, ITitled
{
    public string Title => Id is null ? "부서 상품 등록" : "상품 고치기";
    public TenantContext Tenant => tenant;

    [FromRoute(Name = "id")] public int? Id { get; set; }
    [BindProperty] public GiftItemInput Input { get; set; } = new();

    public GiftItem? Item { get; private set; }
    public int Picked { get; private set; }
    public Dictionary<string, string> Errors { get; private set; } = [];
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    private GiftEditor Editor => GiftEditor.Dept(User.UserName()!, tenant.Current.Id);
    public string Back => $"/{tenant.Current.Slug}/admin/gifts?t=ours";
    private string Self(int id) => $"/{tenant.Current.Slug}/admin/gifts/edit/{id}";

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return false;
        if (Id is not int id) return true;
        (Item, Picked, _, _) = await catalog.GetAsync(Editor, id, ct);
        return Item is not null;
    }

    private void Fill()
    {
        if (Item is { } i) Input = new GiftItemInput { Name = i.Name, Description = i.Description, Price = i.Price.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), CoupangUrl = i.CoupangUrl };
    }

    public async Task<IActionResult> OnGetAsync(string? m, CancellationToken ct)
    {
        if (!await LoadAsync(ct)) return NotFound();
        Fill();
        Message = m switch { "saved" => "저장했어요.", "nophoto" => "저장했어요. 사진은 올리지 못했어요(JPG 사진만 받아요).", "taken" => "저장했어요. 서버에 같은 번호의 사진이 이미 있어 덮어쓰지 않았어요.", "retired" => "목록에서 내렸어요. 아이에게 보이지 않아요.", "restored" => "다시 올렸어요.", _ => null };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(IFormFile? photo, CancellationToken ct)
    {
        if (!await LoadAsync(ct)) return NotFound();
        var (errors, id) = await catalog.SaveAsync(Editor, Id, Input, ct);
        if (errors.Count > 0) { Errors = errors; return Page(); }
        if (photo is { Length: > 0 })
        {
            await using var stream = photo.OpenReadStream();
            var pr = await catalog.SaveImageAsync(Editor, id, stream, photo.Length, ct);
            if (pr != GiftItemError.None) return Redirect(Self(id) + $"?m={(pr == GiftItemError.ImageTaken ? "taken" : "nophoto")}");
        }
        return Redirect(Self(id) + "?m=saved");
    }

    public async Task<IActionResult> OnPostRetireAsync(CancellationToken ct) => await StatusAsync(catalog.RetireAsync, "retired", ct);
    public async Task<IActionResult> OnPostRestoreAsync(CancellationToken ct) => await StatusAsync(catalog.RestoreAsync, "restored", ct);

    private async Task<IActionResult> StatusAsync(Func<GiftEditor, int, CancellationToken, Task<GiftItemError>> act, string m, CancellationToken ct)
    {
        if (Id is not int id || tenant.IsLobby) return NotFound();
        return await act(Editor, id, ct) == GiftItemError.None ? Redirect(Self(id) + $"?m={m}") : NotFound();
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken ct)
    {
        if (Id is not int id || !await LoadAsync(ct)) return NotFound();
        var r = await catalog.DeleteAsync(Editor, id, ct);
        if (r == GiftItemError.None) return Redirect(Back);
        Error = r == GiftItemError.HasPicks ? "고른 기록이 있는 상품은 지울 수 없어요. 대신 \"목록에서 내리기\"를 해 주세요." : "이 상품은 지울 수 없어요.";
        Fill();
        return Page();
    }
}
