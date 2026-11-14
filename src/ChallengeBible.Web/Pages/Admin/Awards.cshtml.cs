using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Admin;

/// <summary>
/// `/{slug}/admin/teams/awards` 가족팀 시상 기록(R-220, ADR 0092, 확정 시안 관리-시상기록). 영역 관리자만.
/// 기본 규칙 "가족팀" 스위치가 꺼지면 404. 확인은 스크립트 확인 창, 스크립트가 없어도 폼은 그대로 동작한다.
/// </summary>
[TenantRoles(Roles.TenantAdmin)]
public class AwardsModel(FamilyAwardService awards, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "시상 기록";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }
    [BindProperty(SupportsGet = true)] public string? Who { get; set; }
    [BindProperty(SupportsGet = true)] public int? Stage { get; set; }

    public AwardBoard Board { get; private set; } = null!;
    public string? Error { get; private set; }

    public string OkMessage => Ok switch
    {
        "given" => $"{Who}에 {Stage:N0}점 선물을 기록했어요",
        "undone" => "기록을 지웠어요",
        _ => "",
    };

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) => await LoadAsync(ct) ? Page() : NotFound();

    // [선물 줬어요]는 선물 처리(관리 › 선물)의 [줬어요]로 하나로 합침(ADR 0094 F, 사용자 2026-11-14 질문 2 → 추천)

    public async Task<IActionResult> OnPostUndoAsync(int award, CancellationToken ct)
    {
        var e = await awards.UndoAsync(award, User.UserName()!, ct);
        if (e == AwardError.None) return RedirectToPage(new { slug = tenant.Current.Slug, Ok = "undone" });
        return await Fail(e, ct);
    }

    private async Task<IActionResult> Fail(AwardError e, CancellationToken ct)
    {
        if (!await LoadAsync(ct)) return NotFound();
        Error = FamilyAwardService.Message(e);
        Ok = null;
        return Page();
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        Board = await awards.GetAsync(ct);
        return Board.Enabled;
    }
}
