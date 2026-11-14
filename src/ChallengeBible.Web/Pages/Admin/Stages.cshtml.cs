using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>
/// `/{slug}/admin/teams/stages` 부서 시상 기준(R-219, ADR 0093, 확정 시안 관리-시상기준-10단계). 영역 관리자만.
/// 1,000점 단위 10단계마다 선물 켜기 · 금액 한도. 기본 규칙 "가족팀" 스위치가 꺼지면 404. `?D=true` 는 처음 값을 칸에 채워 보여 준다(저장은 따로).
/// </summary>
[TenantRoles(Roles.TenantAdmin)]
public class StagesModel(FamilyStageService stages, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "시상 기준";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public int? Y { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }
    [BindProperty(SupportsGet = true)] public bool D { get; set; }

    public StageBoard Board { get; private set; } = null!;
    /// <summary>칸에 보일 값(오류가 나면 쓴 값 그대로).</summary>
    public IReadOnlyList<(bool On, string Amount)> Form { get; private set; } = [];
    public IReadOnlyList<int> Bad { get; private set; } = [];
    public string? Error { get; private set; }
    public string OkMessage => Ok == "saved" ? "저장했어요" : "";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await LoadAsync(ct)) return NotFound();
        Form = D ? Board.Defaults.Select(a => (true, a.ToString("N0"))).ToList()
                 : Board.Rows.Select(r => (r.On, r.Amount.ToString("N0"))).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int year, List<int>? on, List<string?>? amount, CancellationToken ct)
    {
        Y = year;
        if (!await LoadAsync(ct)) return NotFound();
        var flags = Board.Rows.Select((_, i) => on?.Contains(i) == true).ToList();
        var amounts = amount ?? [];
        var (e, bad) = await stages.SaveAsync(year, flags, amounts, ct);
        if (e == StageError.Disabled) return NotFound();
        if (e == StageError.None) return RedirectToPage(new { slug = tenant.Current.Slug, Y = year, Ok = "saved" });
        Error = FamilyStageService.Message(e);
        Bad = bad;
        Form = Board.Rows.Select((r, i) => (flags[i], i < amounts.Count ? amounts[i] ?? "" : "")).ToList();
        return Page();
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        Board = await stages.GetAsync(Y, ct);
        return Board.Enabled;
    }
}
