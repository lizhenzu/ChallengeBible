using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Attendance;

/// <summary>
/// `/{slug}/attendance/more` 출석 더보기 — 유년부 출석부 "더보기"(Views/Attendance/More)와 같은 모양(2026-11-13 사용자 "그대로").
/// 가끔 쓰는 메뉴를 모은다. 부서 관리자 메뉴(보고서 · 통계 · 반 배정)는 관리자에게만 보인다. 만든 화면만 메뉴에 나온다.
/// </summary>
[TenantRoles(Roles.TenantAdmin, Roles.Teacher)]
public class MoreModel(AttendanceService attendance, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "더보기";
    public TenantContext Tenant => tenant;

    public sealed record Item(string Label, string Desc, string Url, bool AdminOnly);

    public bool Newcomers { get; private set; }
    public List<Item> Items { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        var scope = await attendance.ScopeAsync(User.UserName()!, ct);
        if (!scope.Any) return Forbid();
        Newcomers = scope.Newcomers;
        var slug = tenant.Current.Slug;
        List<Item> all =
        [
            new("최근 전체 출석현황 (3주)", "주일 3번 출석률 · 반별 출석", $"/{slug}/attendance/trend", true),
            new("주일 출결 보고서 (PC)", "학년별 요약 · 반별 출결 · 인쇄", $"/{slug}/attendance/report", true),
            new("주일 출석 명단 (휴대폰)", "반별로 누가 왔는지", $"/{slug}/attendance/sunday", true),
            new("교사 · 반 배정", "누가 어느 반 담임인지 정하기", $"/{slug}/admin/classes", true),
        ];
        Items = all.Where(i => tenant.IsAdmin || !i.AdminOnly).ToList();
        return Page();
    }
}
