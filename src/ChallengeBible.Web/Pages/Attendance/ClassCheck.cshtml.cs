using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Attendance;

/// <summary>
/// `/{slug}/attendance/class/{id}?d=` 반 하나만 크게 보는 출석 체크(확정 시안, 사용자 2026-11-13).
/// 반별 출석 · 전체반 현황 · 주일 명단의 [출석 고치기]가 전체 출석부 대신 이 화면을 연다.
/// 저장은 출석부와 같은 주소(`attendance?handler=Set`)를 쓴다. 담임은 자기 반만, 부서 관리자는 모든 반.
/// </summary>
[TenantRoles(Roles.TenantAdmin, Roles.Teacher)]
public class ClassCheckModel(AttendanceService attendance, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "반 출석 체크";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? D { get; set; }
    public AttendBoard Board { get; private set; } = null!;
    public AttendGroup Group { get; private set; } = null!;
    public AttendGroup? Prev { get; private set; }
    public AttendGroup? Next { get; private set; }
    public bool Newcomers { get; private set; }

    /// <summary>"확인 끝" · 돌아가기: 부서 관리자는 반별 출석, 담임은 출석부.</summary>
    public string BackUrl(string slug, string date) => tenant.IsAdmin ? $"/{slug}/attendance/sunday?sunday={date}" : $"/{slug}/attendance?d={date}";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        var scope = await attendance.ScopeAsync(User.UserName()!, ct);
        if (!scope.Any) return Forbid();
        Newcomers = scope.Newcomers;
        Board = await attendance.BoardAsync(scope, D, ct);
        var i = Board.Groups.FindIndex(g => g.ClassId == Id);
        if (i < 0) return NotFound();   // 없는 반이거나 담임이 아닌 반
        Group = Board.Groups[i];
        Prev = i > 0 ? Board.Groups[i - 1] : null;
        Next = i < Board.Groups.Count - 1 ? Board.Groups[i + 1] : null;
        return Page();
    }
}
