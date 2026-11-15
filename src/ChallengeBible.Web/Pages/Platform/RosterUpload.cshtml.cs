using System.Text.Json;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>
/// `/platform/roster-upload` 명단 올리기(ADR 0097, 확정 시안 명단올리기): ① 부서별 템플릿 받기 ② 파일 하나 → 미리 보기 ③ "지우는 것을 알아요" 체크 → 넣기.
/// 미리 보기 내용은 서명한 숨은 칸(Plan)으로 넣기에 넘긴다 — 파일을 다시 올리지 않고, 고쳐 보낼 수 없다. 플랫폼 관리자만.
/// </summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
[RequestSizeLimit(5_000_000)]
public class RosterUploadModel(RosterUploadService roster, IDataProtectionProvider protection) : PageModel, ITitled
{
    public string Title => "명단 올리기";
    public IReadOnlyList<RosterDept> Depts { get; private set; } = [];
    public RosterPlan? Plan { get; private set; }
    public string? PlanToken { get; private set; }
    public string? Error { get; private set; }
    public RosterResult? Result { get; private set; }
    public string? ResultDept { get; private set; }

    private IDataProtector Protector => protection.CreateProtector("RosterUpload.Plan.v1");
    private string Me => User.UserName()!;

    public async Task OnGetAsync(CancellationToken ct) => Depts = await roster.DeptsAsync(Me, ct);

    public async Task<IActionResult> OnGetTemplateAsync(int t, CancellationToken ct)
    {
        var (name, bytes) = await roster.TemplateAsync(Me, t, ct);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    public async Task<IActionResult> OnPostPreviewAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) Error = "올릴 엑셀 파일을 골라 주세요.";
        else
        {
            await using var s = file.OpenReadStream();
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms, ct);
            ms.Position = 0;
            (Plan, Error) = await roster.PreviewAsync(Me, file.FileName, ms, ct);
            if (Plan is not null) PlanToken = Protector.Protect(JsonSerializer.Serialize(Plan));
        }
        Depts = await roster.DeptsAsync(Me, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostApplyAsync(string? plan, bool agree, CancellationToken ct)
    {
        RosterPlan? p = null;
        try { if (!string.IsNullOrEmpty(plan)) p = JsonSerializer.Deserialize<RosterPlan>(Protector.Unprotect(plan)); }
        catch (System.Security.Cryptography.CryptographicException) { }
        if (p is null) Error = "미리 보기가 끝났어요. 파일을 다시 골라 주세요.";
        else if (!agree) { Plan = p; PlanToken = plan; Error = $"\"{RosterUploadService.ShortName(p.TenantName)}의 지금 명단을 지우는 것을 알아요\"에 체크해야 넣어요."; }
        else if (p.Students.Count + p.Teachers.Count == 0) { Plan = p; PlanToken = plan; Error = "넣을 사람이 없어요. 엑셀을 확인해 주세요."; }
        else { Result = await roster.ApplyAsync(Me, p, ct); ResultDept = p.TenantName; }
        Depts = await roster.DeptsAsync(Me, ct);
        return Page();
    }
}
