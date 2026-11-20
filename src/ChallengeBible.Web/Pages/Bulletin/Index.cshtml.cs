using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Bulletin;

/// <summary>`/{slug}/bulletin` 주보 보기(ADR 0117, 확정 시안 주보 ②): 가장 최근(또는 ?id) 주보 + 지난 주보. 누구나(로그인 안 해도), 이 부서 주보만.</summary>
public class IndexModel(BulletinService bulletins, TenantContext tenant) : PageModel, ITitled, IPublicTenantPage
{
    public string Title => "주보";
    public TenantContext Tenant => tenant;
    public BulletinView? Item { get; private set; }
    public IReadOnlyList<BulletinRow> Rows { get; private set; } = [];
    public bool CanEdit { get; private set; }
    [TempData] public string? Msg { get; set; }

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        Item = await bulletins.GetAsync(id, ct);
        if (id is not null && Item is null) return NotFound();
        Rows = await bulletins.ListAsync(ct);
        CanEdit = User.UserId() is not null && bulletins.CanEdit;
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        if (User.UserId() is null || !bulletins.CanEdit) return Forbid();
        Msg = await bulletins.DeleteAsync(User.UserName()!, id, ct) ? "주보를 지웠어요." : "이미 지워진 주보예요.";
        return Redirect($"/{tenant.Current.Slug}/bulletin");
    }
}
