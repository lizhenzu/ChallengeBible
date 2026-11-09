using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>
/// `/{slug}/admin/season` 시즌(확정 시안: 관리-시즌, 0028). 영역 관리자만.
/// `?d=` 로 새 시작일을 고르면 빠지는 점수를 서버에서 계산해 보여 주고, "확인했어요"에 체크해야 바꾼다.
/// </summary>
[TenantRoles(Roles.TenantAdmin)]
public class SeasonModel(SeasonService seasons, MilestoneService milestones, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "시즌";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public DateOnly? D { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }
    /// <summary>0077: 고치는 중인 축하 단계("1000,2000") · 더할 점수.</summary>
    [BindProperty(SupportsGet = true)] public string? M { get; set; }
    [BindProperty(SupportsGet = true)] public int? Add { get; set; }

    public SeasonView View { get; private set; } = null!;
    public SeasonImpact? Impact { get; private set; }
    public string? Error { get; private set; }
    public MilestoneView Ms { get; private set; } = null!;
    public IReadOnlyList<int> Draft => Ms.Current;
    public IReadOnlyDictionary<int, int> Passed => Ms.Passed;
    public string? MsError { get; private set; }

    public async Task<IActionResult> OnPostMilestonesAsync(string? m, CancellationToken ct)
    {
        var list = MilestoneService.Parse(m);
        var (changed, error) = list is null ? (false, "축하 단계를 하나 이상 넣어 주세요.") : await milestones.SaveAsync(User.UserName()!, list, ct);
        if (error is not null) { M = m; MsError = error; await LoadAsync(ct); return Page(); }
        return Redirect($"/{tenant.Current.Slug}/admin/season?ok={(changed ? "ms" : "same")}#ms");
    }

    public async Task<IActionResult> OnPostMilestonesResetAsync(CancellationToken ct)
    {
        var (changed, _) = await milestones.SaveAsync(User.UserName()!, null, ct);
        return Redirect($"/{tenant.Current.Slug}/admin/season?ok={(changed ? "msreset" : "same")}#ms");
    }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostAsync(DateOnly start, bool agree, CancellationToken ct)
    {
        if (!agree) { D = start; Error = "바꾸기 전에 \"위 내용을 확인했어요\"에 체크해 주세요."; await LoadAsync(ct); return Page(); }
        return await Save(start, "save", ct);
    }

    public Task<IActionResult> OnPostNextAsync(DateOnly start, CancellationToken ct) => Save(start, "next", ct);

    private async Task<IActionResult> Save(DateOnly start, string ok, CancellationToken ct)
    {
        var (changed, error) = await seasons.SaveAsync(User.UserName()!, start, ct);
        if (error is not null) { Error = error; await LoadAsync(ct); return Page(); }
        return RedirectToPage(new { slug = tenant.Current.Slug, Ok = changed ? ok : "same" });
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        View = await seasons.GetAsync(ct);
        if (D is { } d && d != View.Current.StartDate) Impact = await seasons.ImpactAsync(d, ct);
        var draft = MilestoneService.Parse(M)?.ToList();
        if (Add is int a)
        {
            draft ??= [.. (await milestones.GetAsync(null, ct)).Current];
            var next = draft.Append(a).Distinct().Order().ToList();
            if (MilestoneService.Check(next) is { } err) MsError = err; else draft = next;
        }
        Ms = await milestones.GetAsync(draft, ct);
    }

    public string Md(DateOnly d) => SeasonService.Md(d);
    public DateOnly Picked => D is { } d && d.Year == View.Today.Year ? d : View.Current.StartDate;
    /// <summary>진행 막대: 그해 날짜를 0~100%로.</summary>
    public string Pct(DateOnly d) => (Math.Clamp(d.DayOfYear - 1, 0, 365) * 100.0 / (DateTime.IsLeapYear(d.Year) ? 366 : 365))
        .ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
    public string LocalDate(DateTime utc) =>
        TenantClock.ToTenantDate(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)), tenant.Current.TimeZoneId).ToString("yyyy.MM.dd");
}
