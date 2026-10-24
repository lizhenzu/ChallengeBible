using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>`/{slug}/admin/content?month=yyyy-MM&amp;date=&amp;kind=` 콘텐츠 달력(확정 시안: 관리-콘텐츠달력, R-120 ~ R-123). 영역 관리자만, 1차는 보기만.</summary>
[TenantRoles(Roles.TenantAdmin)]
public class ContentModel(ContentCalendarService calendar, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "콘텐츠 달력";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public string? Month { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }
    [BindProperty(SupportsGet = true)] public string Kind { get; set; } = "all";

    public ContentMonth Data { get; private set; } = null!;
    public CalendarDay Selected { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken ct)
    {
        DateOnly? month = DateOnly.TryParseExact(Month + "-01", "yyyy-MM-dd", out var m) ? m : Date is { } d ? new DateOnly(d.Year, d.Month, 1) : null;
        Data = await calendar.GetAsync(month, ct);
        if (Kind is not ("w" or "q" or "p" or "r")) Kind = "all";
        Selected = Data.Days.FirstOrDefault(x => x.Date == Date)
                   ?? Data.Days.FirstOrDefault(x => x.Date == Data.Today) ?? Data.Days[0];
    }

    public string Url(DateOnly month, DateOnly? date = null, string? kind = null) =>
        $"/{tenant.Current.Slug}/admin/content?month={month:yyyy-MM}" + (date is { } d ? $"&date={d:yyyy-MM-dd}" : "") + $"&kind={kind ?? Kind}";

    public static string SourceClass(ContentSource s) => s switch
    {
        ContentSource.Over => "s-over", ContentSource.Common => "s-common", ContentSource.Draft => "s-draft", _ => "s-none",
    };

    public static string SourceLabel(ContentSource s) => s switch
    {
        ContentSource.Over => "우리 부서", ContentSource.Common => "공용", ContentSource.Draft => "초안", _ => "없음",
    };
}
