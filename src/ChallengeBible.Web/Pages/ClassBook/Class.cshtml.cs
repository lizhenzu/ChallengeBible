using ChallengeBible.Domain.Time;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.ClassBook;

/// <summary>`/{slug}/classbook/{id}` 반 교적(R-361, 확정 시안 교적부): 아이 카드 · 초성 검색 · 정보 빠짐 · ○월 생일 · [정보 수정](생일 · 전화 · 전화 2).</summary>
public class ClassModel(RosterBookService book, PeopleService people, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "교적부";
    public TenantContext Tenant => tenant;
    public string Slug => tenant.Current.Slug;
    [FromRoute] public int Id { get; set; }
    public BookClassPage Page_ { get; private set; } = null!;
    public int Month { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!IndexModel.CanUse(tenant)) return NotFound();
        var today = TenantClock.ToTenantDate(DateTimeOffset.UtcNow, tenant.Current.TimeZoneId);
        Month = today.Month;
        var pg = await book.ClassAsync(Id, today, ct);
        if (pg is null) return NotFound();
        Page_ = pg;
        return Page();
    }

    /// <summary>[정보 수정] 저장: 생일 · 전화 · 전화 2 (JSON { ok, message }).</summary>
    public async Task<IActionResult> OnPostSaveAsync([FromForm] int personId, [FromForm] int? month, [FromForm] int? day, [FromForm] string? phone1, [FromForm] string? phone2, CancellationToken ct)
    {
        if (!IndexModel.CanUse(tenant)) return NotFound();
        var e = await people.UpdateContactAsync(personId, month, day, phone1, phone2, ct);
        return new JsonResult(new { ok = e == PeopleError.None, message = e == PeopleError.None ? "저장했어요" : PeopleService.Message(e) });
    }
}
