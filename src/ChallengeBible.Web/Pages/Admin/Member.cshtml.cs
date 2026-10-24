using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/members/{id}?month=&amp;date=` 참여자 상세(확정 시안: 관리-참여자상세, 0035 · 0029). 영역 관리자만.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class MemberModel(MemberDetailService details, TenantContext tenant) : PageModel, ITitled
{
    public string Title => Detail?.Name ?? "참여자";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? Month { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ask { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    public MemberDetail? Detail { get; private set; }
    public DateOnly Today { get; private set; }
    public DateOnly ShownMonth { get; private set; }
    /// <summary>고른 날 활동, 아니면 최근 활동한 이틀.</summary>
    public List<(DateOnly Date, List<DetailDayLine> Lines)> Days { get; private set; } = [];
    public string? TemporaryPassword { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) => await LoadAsync(ct) ? Page() : NotFound();

    public async Task<IActionResult> OnPostResetPasswordAsync(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";   // 임시 비밀번호가 보이는 응답은 브라우저에 남기지 않는다
        (TemporaryPassword, Error) = await details.ResetPasswordAsync(User.AccountId()!.Value, Id, ct);
        return await LoadAsync(ct) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostResetMarksAsync(string? confirm, CancellationToken ct)
    {
        Error = await details.ResetMarksAsync(User.AccountId()!.Value, Id, confirm, ct);
        if (Error is null) return RedirectToPage(new { slug = tenant.Current.Slug, Id, Ok = "marks" });
        Ask = "marks";
        return await LoadAsync(ct) ? Page() : NotFound();
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        Today = details.Today;
        ShownMonth = DateOnly.TryParseExact(Month + "-01", "yyyy-MM-dd", out var m) ? m : Date is { } d ? new DateOnly(d.Year, d.Month, 1) : new DateOnly(Today.Year, Today.Month, 1);
        if (ShownMonth > new DateOnly(Today.Year, Today.Month, 1)) ShownMonth = new DateOnly(Today.Year, Today.Month, 1);
        Detail = await details.GetAsync(Id, ShownMonth, ct);
        if (Detail is null) return false;
        if (Detail.ParticipantId is int pid)
        {
            var dates = Date is { } one ? [one] : await details.RecentActiveDaysAsync(pid, 2, ct);
            foreach (var day in dates) Days.Add((day, await details.DayLogAsync(pid, day, ct)));
        }
        return true;
    }

    public string Url(DateOnly? month = null, DateOnly? date = null, string? ask = null) =>
        $"/{tenant.Current.Slug}/admin/members/{Id}?month={(month ?? ShownMonth):yyyy-MM}" + (date is { } d ? $"&date={d:yyyy-MM-dd}" : "") + (ask is null ? "" : $"&ask={ask}");

    public string Local(DateTime utc, string format) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(tenant.Current.TimeZoneId)).ToString(format, ReadingPageBaseKo);

    private static readonly System.Globalization.CultureInfo ReadingPageBaseKo = new("ko-KR");

    public static string MissionLabel(Domain.Rules.Mission m) => m switch
    {
        Domain.Rules.Mission.Word => "말씀", Domain.Rules.Mission.Prayer => "기도", Domain.Rules.Mission.Quiz => "퀴즈", _ => "암송",
    };
}
