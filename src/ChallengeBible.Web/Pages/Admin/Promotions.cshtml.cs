using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/promotions` 진급(확정 시안: 관리-진급, 0026 · 0030 · 0058). 영역 관리자만.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class PromotionsModel(PromotionService promotions, PromotionApprover approver, MemberAdminService members, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "진급";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "in";
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    public List<PromotionRow> Received { get; private set; } = [];
    public List<PromotionRow> Sent { get; private set; } = [];
    public List<(string Code, string Name)> RoleCodes { get; private set; } = [];
    public string? Error { get; private set; }
    public int? ErrorFor { get; private set; }
    private string Me => User.UserName()!;

    public IEnumerable<PromotionRow> Rows => Tab == "out" ? Sent : Received;
    public int ReceivedWaiting => Received.Count(r => r.Status == PromotionStatus.Pending);
    public int SentWaiting => Sent.Count(r => r.Status == PromotionStatus.Pending);

    public string OkMessage => Ok switch
    {
        "approve" => "받았어요 · 바로 미션을 할 수 있어요",
        "all" => "모두 받았어요 · 명부 · 기록이 함께 옮겨 왔어요",
        "reject" => "진급 요청을 거절했어요",
        "cancel" => "진급 요청을 취소했어요",
        "sent" => "진급 요청을 보냈어요 · 받는 부서가 승인하면 옮겨져요",
        _ => "",
    };

    public string Day(DateTime utc) => TenantClock.ToTenantDate(new DateTimeOffset(utc, TimeSpan.Zero), tenant.Current.TimeZoneId).ToString("M월 d일");

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public Task<IActionResult> OnPostApproveAsync(int id, string? roleCode, CancellationToken ct) =>
        Act(id, "approve", approver.ApproveAsync(Me, id, roleCode, ct), ct);

    public async Task<IActionResult> OnPostApproveAllAsync(string? roleCode, CancellationToken ct)
    {
        var (ok, failed) = await approver.ApproveAllAsync(Me, roleCode, ct);
        if (failed == 0) return RedirectToPage(new { slug = tenant.Current.Slug, Tab = "in", Ok = "all" });
        Error = $"{ok}명을 받았어요. {failed}명은 받지 못했어요 — 아래에서 한 명씩 확인해 주세요.";
        await LoadAsync(ct);
        return Page();
    }

    public Task<IActionResult> OnPostRejectAsync(int id, CancellationToken ct) => Act(id, "reject", promotions.RejectAsync(Me, id, ct), ct);
    public Task<IActionResult> OnPostCancelAsync(int id, CancellationToken ct) => Act(id, "cancel", promotions.CancelAsync(Me, id, ct), ct);

    private async Task<IActionResult> Act(int id, string ok, Task<PromotionError> work, CancellationToken ct)
    {
        var e = await work;
        if (e == PromotionError.None) return RedirectToPage(new { slug = tenant.Current.Slug, Tab, Ok = ok });
        Error = PromotionService.Message(e);
        ErrorFor = id;
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (Tab != "out") Tab = "in";
        Received = await promotions.ReceivedAsync(ct);
        Sent = await promotions.SentAsync(ct);
        RoleCodes = await members.RoleCodesAsync(ct);
    }

    public static string StatusLabel(string s) => s switch
    {
        PromotionStatus.Pending => "기다리는 중", PromotionStatus.Approved => "승인됨", PromotionStatus.Rejected => "거절됨", _ => "취소됨",
    };
}
