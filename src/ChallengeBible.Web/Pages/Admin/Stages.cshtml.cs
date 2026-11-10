using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>
/// `/{slug}/admin/teams/stages` 부서 시상 기준(R-219, ADR 0091, 확정 시안 관리-시상기준). 영역 관리자만.
/// 기본 규칙 "가족팀" 스위치가 꺼지면 404. JS 없이 폼마다 서버가 다시 그린다.
/// </summary>
[TenantRoles(Roles.TenantAdmin)]
public class StagesModel(FamilyStageService stages, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "시상 기준";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public int? Y { get; set; }
    [BindProperty(SupportsGet = true)] public int? Edit { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    // 단계 더하기 칸(오류가 나면 쓴 값을 그대로 다시 보여 준다)
    [BindProperty] public int? APoints { get; set; }
    [BindProperty] public string? AType { get; set; }
    [BindProperty] public string? ATitle { get; set; }
    [BindProperty] public string? AEmoji { get; set; }

    public StageBoard Board { get; private set; } = null!;
    public string? Error { get; private set; }
    /// <summary>오류가 난 곳: 0 = 위쪽 띠, -1 = 더하기 칸, 그 밖 = 단계 Id.</summary>
    public int? ErrorFor { get; private set; }

    public string OkMessage => Ok switch
    {
        "own" => "기본값을 복사했어요. 이제 마음대로 고칠 수 있어요",
        "reset" => "기본값으로 되돌렸어요",
        "added" => "단계를 더했어요",
        "saved" => "저장했어요",
        "on" => "단계를 켰어요",
        "off" => "단계를 껐어요",
        "deleted" => "단계를 지웠어요",
        _ => "",
    };

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) => await LoadAsync(ct) ? Page() : NotFound();

    public Task<IActionResult> OnPostOwnAsync(int year, CancellationToken ct) => Act(year, null, 0, "own", stages.OwnAsync(year, ct), ct);
    public Task<IActionResult> OnPostResetAsync(int year, CancellationToken ct) => Act(year, null, 0, "reset", stages.ResetAsync(year, ct), ct);
    public Task<IActionResult> OnPostAddAsync(int year, CancellationToken ct) =>
        Act(year, null, -1, "added", stages.AddAsync(year, APoints ?? 0, AType, ATitle, AEmoji, ct), ct);
    public Task<IActionResult> OnPostSaveAsync(int year, int id, int points, string? type, string? title, string? emoji, CancellationToken ct) =>
        Act(year, id, id, "saved", stages.UpdateAsync(year, id, points, type, title, emoji, ct), ct, keepEdit: true);
    public Task<IActionResult> OnPostToggleAsync(int year, int id, bool on, CancellationToken ct) => Act(year, null, id, on ? "on" : "off", stages.ToggleAsync(year, id, on, ct), ct);
    public Task<IActionResult> OnPostDeleteAsync(int year, int id, CancellationToken ct) => Act(year, null, id, "deleted", stages.DeleteAsync(year, id, ct), ct);

    private async Task<IActionResult> Act(int year, int? edit, int errorFor, string ok, Task<StageError> work, CancellationToken ct, bool keepEdit = false)
    {
        var e = await work;
        Y = year;
        if (e == StageError.None) return RedirectToPage(new { slug = tenant.Current.Slug, Y = year, Ok = ok });
        if (!await LoadAsync(ct)) return NotFound();
        Error = FamilyStageService.Message(e);
        ErrorFor = errorFor;
        if (keepEdit) Edit = edit;
        return Page();
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        Board = await stages.GetAsync(Y, ct);
        if (!Board.Enabled) return false;
        if (Board.Year != Board.ThisYear && Board.Year != Board.ThisYear + 1) { Y = Board.ThisYear; Board = await stages.GetAsync(null, ct); }
        return true;
    }
}
