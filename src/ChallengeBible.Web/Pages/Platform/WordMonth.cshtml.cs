using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>
/// `/platform/content/word/month?month=yyyy-MM` 한 달 입력(유년부 /JuManNa/MonthlyBulk 그대로, 챌린지 테마, 확정 시안 퀴즈출제-말씀입력 ③).
/// 날짜마다 제목 · 성경 · 장절. 바꾼 날만 저장, 새 날은 초안. 이야기 · 해설 · 영상 구간은 건드리지 않는다. 플랫폼 관리자만.
/// </summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class WordMonthModel(WordToolsService tools, TimeProvider time) : PageModel, ITitled
{
    public string Title => "한 달 입력 · 말씀";
    [BindProperty(SupportsGet = true)] public string? Month { get; set; }
    [BindProperty] public List<WordMonthRow> Rows { get; set; } = [];
    public DateOnly First { get; private set; }
    public Dictionary<DateOnly, string> Errors { get; private set; } = [];
    [TempData] public string? Saved { get; set; }

    private DateOnly Resolve() => ContentCheckModel.ParseMonth(Month)
        ?? (Domain.Time.TenantClock.ToTenantDate(time.GetUtcNow().UtcDateTime, PlatformHomeService.PlatformTimeZone) is var t ? new DateOnly(t.Year, t.Month, 1) : default);

    public async Task OnGetAsync(CancellationToken ct)
    {
        First = Resolve();
        Rows = await tools.MonthAsync(User.UserName()!, First, ct);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        First = Resolve();
        foreach (var r in Rows) r.Changed = true;   // 서버가 지금 값과 비교해 바뀐 날만 저장한다
        var (added, updated, errors) = await tools.SaveMonthAsync(User.UserName()!, First, Rows, ct);
        if (errors.Count > 0)
        {
            Errors = errors;
            var now = (await tools.MonthAsync(User.UserName()!, First, ct)).ToDictionary(r => r.Date);
            foreach (var r in Rows) if (!errors.ContainsKey(r.Date) && now.TryGetValue(r.Date, out var n)) { r.Id = n.Id; r.Status = n.Status; }
            Saved = $"{added}일 새로 · {updated}일 고침 · 틀린 {errors.Count}일은 저장하지 않았어요";
            return Page();
        }
        Saved = added + updated == 0 ? "바뀐 날이 없어요" : added > 0 ? $"새로 {added}일 넣고(초안) · {updated}일 고쳤어요" : $"{updated}일 고쳤어요";
        return Redirect($"/platform/content/word/month?month={First:yyyy-MM}");
    }

    public string MonthText(DateOnly m) => m.ToString("yyyy-MM");
}
