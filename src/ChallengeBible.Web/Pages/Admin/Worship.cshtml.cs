using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/worship` 예배 안내 고치기(ADR 0116, R-337). 부서 관리자만. 부서 홈 "예배 안내" 카드에 바로 보인다.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class WorshipModel(WorshipService worship, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "예배 안내";
    public TenantContext Tenant => tenant;
    public IReadOnlyList<WorshipSchedule> Rows { get; private set; } = [];
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }
    public FieldErrors Errors { get; private set; } = new();
    /// <summary>오류가 난 줄(새 줄이면 0).</summary>
    public int? ErrorRow { get; private set; }
    public WorshipInput? Last { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => Rows = await worship.ListAsync(includeHidden: true, ct);

    public async Task<IActionResult> OnPostSaveAsync(int? id, string? title, int dayOfWeek, string? startTime, string? place, string? address, int sortOrder, bool active, CancellationToken ct)
    {
        var input = new WorshipInput(id, title, dayOfWeek, startTime, place, address, null, null, sortOrder, active);
        var (_, errors) = await worship.SaveAsync(User.UserName()!, input, ct);
        if (errors.Ok) return Redirect($"/{tenant.Current.Slug}/admin/worship?ok=save");
        (Errors, ErrorRow, Last) = (errors, id ?? 0, input);
        Rows = await worship.ListAsync(includeHidden: true, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        await worship.DeleteAsync(User.UserName()!, id, ct);
        return Redirect($"/{tenant.Current.Slug}/admin/worship?ok=delete");
    }
}
