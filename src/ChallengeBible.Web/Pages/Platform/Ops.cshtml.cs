using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>
/// `/platform/ops?a=&amp;t=&amp;d=&amp;n=` 운영 점검(확정 시안: 플랫폼-운영점검): 서버 상태 6칸 → 최근 오류(서버 메모리 50건, 묶음) → 감사 로그(종류 · 부서 · 기간, 50건씩 더 보기).
/// 화면을 열 때마다 다시 점검한다.
/// </summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class OpsModel(OpsService ops, ErrorLog errors, TimeProvider time) : PageModel, ITitled
{
    public const int Page50 = 50;
    public string Title => "운영 점검";

    [BindProperty(SupportsGet = true, Name = "a")] public string? Action { get; set; }
    [BindProperty(SupportsGet = true, Name = "t")] public int? TenantId { get; set; }
    [BindProperty(SupportsGet = true, Name = "d")] public int Days { get; set; } = 30;
    [BindProperty(SupportsGet = true, Name = "n")] public int Take { get; set; } = Page50;

    public IReadOnlyList<OpsCheck> Checks { get; private set; } = [];
    public IReadOnlyList<ErrorGroup> Errors { get; private set; } = [];
    public IReadOnlyList<OpsAuditRow> Audit { get; private set; } = [];
    public bool More { get; private set; }
    public IReadOnlyList<string> Actions { get; private set; } = [];
    public IReadOnlyList<(int Id, string Name)> Tenants { get; private set; } = [];
    public DateTime NowLocal { get; private set; }
    /// <summary>적용 안 된 마이그레이션(있으면 "적용" 칸을 보인다).</summary>
    public IReadOnlyList<string> Pending { get; private set; } = [];
    public string? MigrateMessage { get; private set; }
    public bool MigrateOk { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var me = User.UserName()!;
        if (Days is not (7 or 30 or 0)) Days = 30;
        Take = Math.Clamp(Take, Page50, 1000);
        Checks = await ops.ChecksAsync(me, ct);
        Errors = errors.Groups();
        (Audit, More, Actions, Tenants) = await ops.AuditAsync(me, Action, TenantId, Days, Take, ct);
        NowLocal = Local(time.GetUtcNow().UtcDateTime);
        Pending = await ops.PendingAsync(me, ct);
    }

    /// <summary>적용 안 된 마이그레이션 적용: 확인 글자 APPLY, 화면에 보인 목록(expected)과 같을 때만.</summary>
    public async Task<IActionResult> OnPostMigrateAsync(string? confirm, string? expected, CancellationToken ct)
    {
        var me = User.UserName()!;
        if (!string.Equals(confirm?.Trim(), OpsService.MigrateConfirm, StringComparison.Ordinal))
            (MigrateOk, MigrateMessage) = (false, $"확인 칸에 {OpsService.MigrateConfirm} 를 써야 해요.");
        else
            (MigrateOk, MigrateMessage) = await ops.MigrateAsync(me, (expected ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), ct);
        await OnGetAsync(ct);
        return Page();
    }

    public static DateTime Local(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(OpsService.PlatformTimeZone));

    public string MoreUrl => $"/platform/ops?a={Uri.EscapeDataString(Action ?? "")}&t={TenantId}&d={Days}&n={Take + Page50}#audit";

    public static string StateText(CheckState s) => s switch { CheckState.Ok => "정상", CheckState.Warn => "확인 필요", _ => "문제" };
    public static string StateClass(CheckState s) => s switch { CheckState.Ok => "ok", CheckState.Warn => "warn", _ => "bad" };
}
