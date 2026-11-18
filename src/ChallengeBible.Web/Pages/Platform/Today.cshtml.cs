using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/today` 오늘 현황(2026-11-18 사용자 확정 시안 플랫폼홈-숫자보기): 플랫폼 홈 숫자 → 부서 · 계정 · 미션 참여 · 통독 목록.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class TodayModel(PlatformTodayService today) : PageModel, ITitled
{
    public string Title => "오늘 현황";
    public PlatformToday Data { get; private set; } = null!;
    public string K { get; private set; } = PlatformTodayService.Missions;
    public int? T { get; private set; }
    public string F { get; private set; } = "new";

    public async Task OnGetAsync(string? k, int? t, string? f, CancellationToken ct)
    {
        K = k is PlatformTodayService.Depts or PlatformTodayService.Accounts or PlatformTodayService.Reads ? k : PlatformTodayService.Missions;
        F = f is "all" or "none" ? f : "new";
        T = K == PlatformTodayService.Depts ? null : t;
        Data = await today.GetAsync(User.UserName()!, K, T, F, ct);
    }

    public string Url(string k, int? t = null, string? f = null) =>
        $"/platform/today?k={k}" + (t is int x ? $"&t={x}" : "") + (f is { } y && y != "new" ? $"&f={y}" : "");

    public static string Acc(string userName) => $"/platform/accounts?q={Uri.EscapeDataString(userName)}&id={Uri.EscapeDataString(userName)}";
}
