using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>
/// `/platform/tenants/{id}` 부서 화면(2026-11-18 사용자 확정 시안 플랫폼부서-화면분리): 목록에서 펼치지 않고 따로, 탭 4개
/// (기본 정보 · 프로그램 · 참여 · 관리자 · 운영). 저장은 목록 화면(TenantsModel)의 처리기를 그대로 쓰고 여기로 돌아온다.
/// </summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class TenantModel(PlatformTenantService platform) : PageModel, ITitled
{
    public const string ErrKey = "tn-err";
    public static readonly (string Key, string Label)[] Tabs = [("info", "기본 정보"), ("prog", "프로그램 · 참여"), ("admin", "관리자"), ("ops", "운영")];

    public string Title => Row is null ? "부서" : PlatformTodayService.ShortDept(Row.Name);
    public TenantRow? Row { get; private set; }
    public string Tab { get; private set; } = "info";
    public string? Ok { get; private set; }
    public string? Error { get; private set; }

    public static string Url(int id, string? tab, string? ok) =>
        $"/platform/tenants/{id}" + (tab is { Length: > 0 } && tab != "info" ? $"?tab={tab}" : "") + (ok is { Length: > 0 } ? (tab is { Length: > 0 } && tab != "info" ? "&" : "?") + $"ok={ok}" : "");

    /// <summary>저장 결과 → 돌아갈 탭.</summary>
    public static string TabOf(string ok) => ok switch
    {
        "saved" or "code" or "listed" or "unlisted" or "missionbook-on" or "missionbook-off" => "prog",
        "admin" => "admin",
        "suspended" or "resumed" => "ops",
        _ => "info",
    };

    public async Task<IActionResult> OnGetAsync(int id, string? tab, string? ok, CancellationToken ct)
    {
        Row = (await platform.ListAsync(User.UserName()!, ct)).FirstOrDefault(r => r.Id == id);
        if (Row is null) return NotFound();
        Tab = Tabs.Any(x => x.Key == tab) ? tab! : "info";
        Ok = ok;
        Error = TempData[ErrKey] as string;
        return Page();
    }

    public string OkMessage => Ok switch
    {
        "created" => "부서를 만들었어요. 참여 코드를 선생님께 알려 주세요.",
        "saved" => "프로그램을 저장했어요.",
        "info" => "기본 정보를 저장했어요.",
        "code" => "새 참여 코드를 만들었어요. 예전 코드로는 신청할 수 없어요.",
        "admin" => "부서 관리자를 바꿨어요.",
        "suspended" => "부서를 정지했어요. 데이터는 지우지 않아요.",
        "resumed" => "부서를 다시 열었어요.",
        "listed" => "참여 신청 화면의 부서 목록에 보여요.",
        "unlisted" => "참여 신청 화면의 부서 목록에서 숨겼어요. 부서 주소 · 코드로는 신청할 수 있어요.",
        _ => "",
    };

    /// <summary>"이 부서 관리 화면": 그 부서 관리자로 가장(R-335와 같은 길) — "부서주소-admin" 이 있으면 그 계정.</summary>
    public string? AdminImpUrl => Row is { } r && r.Admins.Count > 0
        ? AccountsModel.ImpToAdminUrl(r.Admins.Select(a => a.UserName).OrderBy(n => n.Equals($"{r.Slug}-admin", StringComparison.OrdinalIgnoreCase) ? 0 : 1).First(), r.Slug)
        : null;
}
