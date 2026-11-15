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

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int? Open { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string Slug { get; set; } = "";
    [BindProperty] public string TimeZoneId { get; set; } = "Asia/Seoul";
    [BindProperty] public string ThemeColor { get; set; } = "blue";
    [BindProperty] public bool Mission { get; set; } = true;
    [BindProperty] public bool Reading { get; set; } = true;
    [BindProperty] public string? AdminEmail { get; set; }

    /// <summary>여러 부서 한 번에 만들기(확정 시안 플랫폼-부서-여러개만들기): 같은 이름의 칸을 줄 수만큼 보낸다(줄을 빼도 번호를 다시 매길 필요 없음).</summary>
    [BindProperty] public List<string?> BulkName { get; set; } = [];
    [BindProperty] public List<string?> BulkSlug { get; set; } = [];
    [BindProperty] public List<string?> BulkColor { get; set; } = [];
    public sealed record BulkLine(string Name, string Slug, string Color, string? Error);
    public List<BulkLine> BulkLines { get; private set; } = [];
    public bool ShowBulk { get; private set; }

    /// <summary>처음 채워 둘 부서(2026-11-15 사용자: 영아부 · 유아부 · 유치부). 이미 있는 주소는 빼고, 다 있으면 빈 줄 하나.</summary>
    public static readonly IReadOnlyList<(string Name, string Slug, string Color)> Suggested =
    [
        ("잠실새내교회 영아부", "jamsil-infant", "green"), ("잠실새내교회 유아부", "jamsil-toddler", "red"), ("잠실새내교회 유치부", "jamsil-kindergarten", "violet"),
    ];

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
        "bulk" => $"{Made}곳을 만들었어요 · {MadeNames}. 참여 코드를 선생님께 알려 주세요.",
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

    [BindProperty(SupportsGet = true)] public int Made { get; set; }
    [BindProperty(SupportsGet = true)] public string? MadeNames { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostBulkAsync(CancellationToken ct)
    {
        var lines = Enumerable.Range(0, Math.Max(BulkName.Count, BulkSlug.Count))
            .Select(i => new BulkLine((BulkName.ElementAtOrDefault(i) ?? "").Trim(), (BulkSlug.ElementAtOrDefault(i) ?? "").Trim(), BulkColor.ElementAtOrDefault(i) ?? "blue", null))
            .Where(l => l.Name.Length > 0 || l.Slug.Length > 0).ToList();   // 다 빈 줄은 건너뛴다
        var (made, errors) = await platform.CreateManyAsync(Me, lines.Select(l => new NewTenant(l.Name, l.Slug, "Asia/Seoul", l.Color, true, true, null)).ToList(), ct);
        if (errors.Count == 0)
            return RedirectToPage(new { Ok = "bulk", Made = made.Count, MadeNames = string.Join(" · ", made.Select(t => t.Name)) });
        BulkLines = lines.Select((l, i) => l with { Error = errors.GetValueOrDefault(i) }).ToList();
        RowError = errors.GetValueOrDefault(-1);
        ShowBulk = true;
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        var (t, errors) = await platform.CreateAsync(Me, new NewTenant(Name, Slug, TimeZoneId, ThemeColor, Mission, Reading, AdminEmail), ct);
        if (t is null)
        {
            CreateErrors = errors;
            ShowCreate = true;
            await LoadAsync(ct);
            return Page();
        }
        return RedirectToPage(new { Open = t.Id, Ok = "created" });
    }

    public async Task<IActionResult> OnPostInfoAsync(int id, string name, string timeZoneId, string themeColor, CancellationToken ct)
    {
        var errors = await platform.UpdateInfoAsync(Me, id, name, timeZoneId, themeColor, ct);
        return errors.Ok ? RedirectToPage(new { Open = id, Ok = "info" }) : await RowFail(id, string.Join(" ", errors.Values), ct);
    }

    public async Task<IActionResult> OnPostProgramsAsync(int id, bool mission, bool reading, CancellationToken ct)
    {
        var err = await platform.SetProgramsAsync(Me, id, mission, reading, ct);
        return err is null ? RedirectToPage(new { Open = id, Ok = "saved" }) : await RowFail(id, err, ct);
    }

    public async Task<IActionResult> OnPostCodeAsync(int id, CancellationToken ct)
    {
        await platform.RegenerateCodeAsync(Me, id, ct);
        return RedirectToPage(new { Open = id, Ok = "code" });
    }

    public async Task<IActionResult> OnPostAddAdminAsync(int id, string email, CancellationToken ct)
    {
        var err = await platform.AddAdminAsync(Me, id, email, ct);
        return err is null ? RedirectToPage(new { Open = id, Ok = "admin" }) : await RowFail(id, err, ct);
    }

    public async Task<IActionResult> OnPostRemoveAdminAsync(int id, string userName, CancellationToken ct)
    {
        await platform.RemoveAdminAsync(Me, id, userName, ct);
        return RedirectToPage(new { Open = id, Ok = "admin" });
    }

    public async Task<IActionResult> OnPostListedAsync(int id, bool listed, CancellationToken ct)
    {
        await platform.SetJoinListedAsync(Me, id, listed, ct);
        return RedirectToPage(new { Open = id, Ok = listed ? "listed" : "unlisted" });
    }

    public async Task<IActionResult> OnPostStatusAsync(int id, string status, CancellationToken ct)
    {
        await platform.SetStatusAsync(Me, id, status, ct);
        return RedirectToPage(new { Open = id, Ok = status == TenantStatus.Suspended ? "suspended" : "resumed" });
    }

    private async Task<IActionResult> RowFail(int id, string error, CancellationToken ct)
    {
        Open = id;
        RowError = error;
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var all = await platform.ListAsync(Me, ct);
        Rows = string.IsNullOrWhiteSpace(Q) ? all : all.Where(r => r.Name.Contains(Q.Trim()) || r.Slug.Contains(Q.Trim().ToLowerInvariant())).ToList();
        if (BulkLines.Count == 0)
        {
            BulkLines = Suggested.Where(x => all.All(r => r.Slug != x.Slug)).Select(x => new BulkLine(x.Name, x.Slug, x.Color, null)).ToList();
            if (BulkLines.Count == 0) BulkLines = [new BulkLine("", "", "blue", null)];
        }
    }
}
