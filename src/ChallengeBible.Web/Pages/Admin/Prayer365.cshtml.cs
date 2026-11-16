using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/prayer365` 우리 부서 365 기도 참여자(ADR 0106, 확정 시안 365기도 ③). 부서 관리자만.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class Prayer365Model(Prayer365Service prayers, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "365 기도 참여자";
    public TenantContext Tenant => tenant;
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    public IReadOnlyList<Prayer365Participant> Rows { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct) => Rows = await prayers.ParticipantsAsync(Sort ?? "recent", ct);
}
