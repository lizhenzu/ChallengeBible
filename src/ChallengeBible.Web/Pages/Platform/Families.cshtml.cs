using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/families?q=` 온가족 목록(확정 시안: 플랫폼-온가족 "목록", 0065 · R-175 · R-176). 맨 위 신청 → 묶기 · 거절, 아래 온가족 · 찾기.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class FamiliesModel(FamilyAdminService families) : PageModel, ITitled
{
    public string Title => "온가족";
    private int Me => User.AccountId()!.Value;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    public List<FamilyRequestRow> Requests { get; private set; } = [];
    public List<FamilyBoardView> Families { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Requests = await families.RequestsAsync(Me, ct);
        Families = await families.FamiliesAsync(Me, Q, ct);
    }

    public async Task<IActionResult> OnPostRejectAsync(int requestId, CancellationToken ct)
    {
        await families.RejectAsync(Me, requestId, ct);
        return RedirectToPage(new { Ok = "rejected" });
    }

    /// <summary>"유년부 · 초등부" — 구성원들의 부서(교회 이름 빼고, 겹치면 하나).</summary>
    public static string Depts(FamilyBoardView f) =>
        string.Join(" · ", f.People.SelectMany(p => p.Depts).Select(d => JoinModel.Split(d).Dept).Distinct());
}
