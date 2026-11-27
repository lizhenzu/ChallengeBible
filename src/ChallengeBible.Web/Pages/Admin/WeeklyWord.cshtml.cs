using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WeeklyWordRow = ChallengeBible.Infrastructure.Data.WeeklyWord;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/weekly-word` 이번 주 말씀 입력(ADR 0142, R-374): 주일 날짜 · 말씀 제목 · 설교 내용. 부서 관리자만. 같은 주일을 다시 올리면 고친다.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class WeeklyWordModel(WeeklyWordService words, TenantContext tenant, TimeProvider time) : PageModel, ITitled
{
    public string Title => "이번 주 말씀";
    public TenantContext Tenant => tenant;
    public IReadOnlyList<WeeklyWordRow> Rows { get; private set; } = [];
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }
    public FieldErrors Errors { get; private set; } = new();
    /// <summary>폼에 채울 값(고치기 · 오류로 되돌아온 값 · 처음엔 가장 가까운 지난 주일).</summary>
    public DateOnly SelectedDate { get; private set; }
    public string FormTitle { get; private set; } = "";
    public string FormBody { get; private set; } = "";
    public IReadOnlyList<DateOnly> Sundays { get; private set; } = [];

    private DateOnly Today => TenantClock.ToTenantDate(time.GetUtcNow(), tenant.Current.TimeZoneId);
    private static DateOnly SundayOnOrBefore(DateOnly d) => d.AddDays(-(int)d.DayOfWeek);

    private void FillSundays(DateOnly selected)
    {
        var start = SundayOnOrBefore(Today).AddDays(-7 * 12);   // 지난 12주 ~ 앞으로 4주
        var list = Enumerable.Range(0, 17).Select(i => start.AddDays(7 * i)).ToList();
        if (!list.Contains(selected) && selected.DayOfWeek == DayOfWeek.Sunday) { list.Add(selected); list.Sort(); }
        Sundays = list;
    }

    public async Task<IActionResult> OnGetAsync(string? d, CancellationToken ct)
    {
        Rows = await words.ListAsync(ct);
        SelectedDate = DateOnly.TryParse(d, out var pd) && pd.DayOfWeek == DayOfWeek.Sunday ? pd : SundayOnOrBefore(Today);
        if (await words.GetAsync(SelectedDate, ct) is { } cur) (FormTitle, FormBody) = (cur.Title, cur.Body);
        FillSundays(SelectedDate);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(string? date, string? title, string? body, CancellationToken ct)
    {
        DateOnly? sunday = DateOnly.TryParse(date, out var pd) ? pd : null;
        var errors = await words.SaveAsync(User.UserName()!, new WeeklyWordInput(sunday, title, body), ct);
        if (errors.Ok) return Redirect($"/{tenant.Current.Slug}/admin/weekly-word?ok=save");
        Errors = errors; FormTitle = title ?? ""; FormBody = body ?? ""; SelectedDate = sunday ?? SundayOnOrBefore(Today);
        Rows = await words.ListAsync(ct);
        FillSundays(SelectedDate);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string? date, CancellationToken ct)
    {
        if (DateOnly.TryParse(date, out var pd)) await words.DeleteAsync(User.UserName()!, pd, ct);
        return Redirect($"/{tenant.Current.Slug}/admin/weekly-word?ok=delete");
    }
}
