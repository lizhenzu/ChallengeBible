using ChallengeBible.Domain.Time;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages;

/// <summary>
/// `/` 공통 첫 화면(0045, 확정 시안 로그인전-첫화면-v2 · 0061). 2026-11-23 사용자: 로그인한 사람도 이 화면에 머문다 —
/// 머리말에 "000님"을 보여 주고, 부서가 하나면 그 부서 홈 바로가기를 둔다(자동으로 넘기지 않는다).
/// 플랫폼 관리자이면서 소속이 없을 때만 플랫폼 홈으로 보낸다.
/// </summary>
public class IndexModel(AccountService accounts, TenantClock clock, LandingService landing, DeptAdminLoginService deptAdmins, DeptStudentLoginService deptStudents) : PageModel, ITitled
{
    public string Title => "";
    /// <summary>[관리자 로그인] 아래 부서 관리자 버튼(R-364).</summary>
    public IReadOnlyList<DeptAdminButton> DeptAdmins { get; private set; } = [];
    /// <summary>그 아래 부서 대표 학생 버튼(R-367). 버튼에는 부서만, 아이 이름은 쓰지 않는다.</summary>
    public IReadOnlyList<DeptAdminButton> DeptStudents { get; private set; } = [];
    /// <summary>맨 아래 © 연도: 부서가 없는 화면이라 한국 시간 기준 올해(하드코딩 안 함).</summary>
    public int Year => clock.Today("Asia/Seoul").Year;
    public LandingView Data { get; private set; } = null!;
    /// <summary>로그인한 사람의 머리말 정보(이름 · 승인된 부서 수 · 하나뿐인 부서 slug). 로그인 전에는 null.</summary>
    public LandingMe? Me { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Data = await landing.GetAsync(ct);
        DeptAdmins = await deptAdmins.ListAsync(ct);
        DeptStudents = (await deptStudents.ListAsync(ct)).Select(p => p.Dept).ToList();
        if (User.UserName() is { } id)
        {
            var go = await accounts.WhereToGoAsync(id, ct);
            if (go.ApprovedCount == 0 && User.IsPlatformAdmin()) return Redirect("/platform");
            Me = new(User.DisplayName(), go.ApprovedCount, go.OnlyTenantSlug);
        }
        return Page();
    }
}

/// <summary>첫 화면 머리말에 쓰는 로그인 정보(이름 · 부서 수 · 하나뿐인 부서 slug).</summary>
public sealed record LandingMe(string Name, int DeptCount, string? Slug);
