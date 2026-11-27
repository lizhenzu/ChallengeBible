using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/prayer365-stats` 365축복기도 통계(ADR 0106, 확정 시안 365축복기도 ④): 플랫폼 관리자 전체 보기.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class Prayer365StatsModel(Prayer365Service prayers) : PageModel, ITitled
{
    public string Title => "365축복기도 통계";
    public Prayer365Dashboard Data { get; private set; } = null!;
    public async Task OnGetAsync(CancellationToken ct) => Data = await prayers.DashboardAsync(User.UserName()!, ct);
}
