using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Bulletin;

/// <summary>`/{slug}/bulletin/edit/{id?}` 주보 올리기 · 고치기(ADR 0117, 확정 시안 주보 ③): 날짜 · 제목 · 내용 + 여러 파일. 그 부서 교사 · 관리자만.</summary>
[TenantRoles(Roles.TenantAdmin, Roles.Teacher)]
[RequestSizeLimit(BulletinService.MaxFiles * BulletinService.MaxBytes + 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = BulletinService.MaxFiles * BulletinService.MaxBytes + 1024 * 1024)]
public class EditModel(BulletinService bulletins, TenantContext tenant, TimeProvider time) : PageModel, ITitled
{
    public string Title => "주보 올리기";
    public TenantContext Tenant => tenant;
    public BulletinView? Item { get; private set; }
    public DateOnly Date { get; private set; }
    public string? TitleText { get; private set; }
    public string? Body { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        if (id is int i)
        {
            Item = await bulletins.GetAsync(i, ct);
            if (Item is null) return NotFound();
            (Date, TitleText, Body) = (Item.Item.Date, Item.Item.Title, Item.Item.Body);
        }
        else Date = NextSunday();
        return Page();
    }

    /// <summary>오늘이 주일이면 오늘, 아니면 다음 주일(부서 날짜).</summary>
    private DateOnly NextSunday()
    {
        var today = ChallengeBible.Domain.Time.TenantClock.ToTenantDate(time.GetUtcNow(), tenant.Current.TimeZoneId);
        return today.AddDays((7 - (int)today.DayOfWeek) % 7);
    }

    public async Task<IActionResult> OnPostAsync(int? id, DateOnly? date, string? title, string? body, List<IFormFile> files, List<int> remove, CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        var (saved, error) = await bulletins.SaveAsync(User.UserName()!, id, date, title, body, files ?? [], remove ?? [], ct);
        if (error is null) { TempData["Msg"] = id is null ? "주보를 올렸어요." : "주보를 고쳤어요."; return Redirect($"/{tenant.Current.Slug}/bulletin?id={saved}"); }
        Error = error;
        if (id is int i) Item = await bulletins.GetAsync(i, ct);
        (Date, TitleText, Body) = (date ?? NextSunday(), title, body);
        return Page();
    }
}
