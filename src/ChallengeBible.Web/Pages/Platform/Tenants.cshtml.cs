using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/tenants` 부서 관리(확정 시안: 플랫폼-부서관리).</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class TenantsModel(PlatformTenantService platform) : PageModel, ITitled
{
    public string Title => "부서 · 플랫폼";

    [BindProperty(SupportsGet = true)] public int? Open { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string Slug { get; set; } = "";
    [BindProperty] public string TimeZoneId { get; set; } = "Asia/Seoul";
    [BindProperty] public string ThemeColor { get; set; } = "blue";
    [BindProperty] public bool Mission { get; set; } = true;
    [BindProperty] public bool Reading { get; set; } = true;
    /// <summary>도전!!!성경필사(ADR 0099): 기본 켬.</summary>
    [BindProperty] public bool Copy { get; set; } = true;
    [BindProperty] public string? AdminEmail { get; set; }

    public List<TenantRow> Rows { get; private set; } = [];
    public FieldErrors CreateErrors { get; private set; } = new();
    public bool ShowCreate { get; private set; }
    public string? RowError { get; private set; }

    private string Me => User.UserName()!;

    public static string ColorVar(string c) => $"var(--c-{c})";
    public static string ColorLabel(string c) => c switch { "blue" => "파랑", "green" => "초록", "red" => "주황", "violet" => "보라", _ => c };

    public string OkMessage => Ok switch
    {
        "created" => "부서를 만들었어요. 참여 코드를 선생님께 알려 주세요.",
        "saved" => "저장했어요.",
        "info" => "기본 정보를 저장했어요.",
        "code" => "새 참여 코드를 만들었어요. 예전 코드로는 신청할 수 없어요.",
        "admin" => "부서 관리자를 바꿨어요.",
        "suspended" => "부서를 정지했어요. 데이터는 지우지 않아요.",
        "resumed" => "부서를 다시 열었어요.",
        "listed" => "참여 신청 화면의 부서 목록에 보여요.",
        "unlisted" => "참여 신청 화면의 부서 목록에서 숨겼어요. 부서 주소 · 코드로는 신청할 수 있어요.",
        _ => "",
    };

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Open is int id) return Redirect(TenantModel.Url(id, null, Ok));   // 예전 주소(?Open=) → 부서 화면
        await LoadAsync(ct);
        return Page();
    }

    /// <summary>저장한 뒤 그 부서 화면의 알맞은 탭으로(2026-11-18 확정 시안 플랫폼부서-화면분리).</summary>
    private RedirectResult Back(int id, string ok) => Redirect(TenantModel.Url(id, TenantModel.TabOf(ok), ok));

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        var (t, errors) = await platform.CreateAsync(Me, new NewTenant(Name, Slug, TimeZoneId, ThemeColor, Mission, Reading, AdminEmail, Copy), ct);
        if (t is null)
        {
            CreateErrors = errors;
            ShowCreate = true;
            await LoadAsync(ct);
            return Page();
        }
        return Back(t.Id, "created");
    }

    public async Task<IActionResult> OnPostInfoAsync(int id, string name, string timeZoneId, string themeColor, CancellationToken ct)
    {
        var errors = await platform.UpdateInfoAsync(Me, id, name, timeZoneId, themeColor, ct);
        return errors.Ok ? Back(id, "info") : await RowFail(id, string.Join(" ", errors.Values), ct);
    }

    public async Task<IActionResult> OnPostProgramsAsync(int id, bool mission, bool reading, bool copy, CancellationToken ct)
    {
        var err = await platform.SetProgramsAsync(Me, id, mission, reading, ct, copy);
        return err is null ? Back(id, "saved") : await RowFail(id, err, ct);
    }

    public async Task<IActionResult> OnPostCodeAsync(int id, CancellationToken ct)
    {
        await platform.RegenerateCodeAsync(Me, id, ct);
        return Back(id, "code");
    }

    public async Task<IActionResult> OnPostAddAdminAsync(int id, string email, CancellationToken ct)
    {
        var err = await platform.AddAdminAsync(Me, id, email, ct);
        return err is null ? Back(id, "admin") : await RowFail(id, err, ct);
    }

    public async Task<IActionResult> OnPostRemoveAdminAsync(int id, string userName, CancellationToken ct)
    {
        await platform.RemoveAdminAsync(Me, id, userName, ct);
        return Back(id, "admin");
    }

    public async Task<IActionResult> OnPostListedAsync(int id, bool listed, CancellationToken ct)
    {
        await platform.SetJoinListedAsync(Me, id, listed, ct);
        return Back(id, listed ? "listed" : "unlisted");
    }

    public async Task<IActionResult> OnPostStatusAsync(int id, string status, CancellationToken ct)
    {
        await platform.SetStatusAsync(Me, id, status, ct);
        return Back(id, status == TenantStatus.Suspended ? "suspended" : "resumed");
    }

    private Task<IActionResult> RowFail(int id, string error, CancellationToken ct)
    {
        TempData[TenantModel.ErrKey] = error;   // 부서 화면에서 한 번 보인다
        return Task.FromResult<IActionResult>(Redirect(TenantModel.Url(id, Request.Form["tab"].FirstOrDefault(), null)));
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Rows = await platform.ListAsync(Me, ct);
    }
}
