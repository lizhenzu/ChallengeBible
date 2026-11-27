using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Bulletin;

/// <summary>`/{slug}/bulletin` 주보 보기(ADR 0117, 확정 시안 주보 ②): 모든 주보를 최근 순으로 아래로 쭉(?id 는 그 주보로 내려가기). 누구나(로그인 안 해도), 이 부서 주보만.</summary>
public class IndexModel(BulletinService bulletins, TenantContext tenant) : PageModel, ITitled, IPublicTenantPage
{
    public string Title => "주보";
    public TenantContext Tenant => tenant;
    public IReadOnlyList<BulletinView> All { get; private set; } = [];
    public int? FocusId { get; private set; }
    public bool CanEdit { get; private set; }
    [TempData] public string? Msg { get; set; }

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        All = await bulletins.ListAllAsync(ct);
        if (id is not null && All.All(x => x.Item.Id != id)) return NotFound();
        FocusId = id;
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
