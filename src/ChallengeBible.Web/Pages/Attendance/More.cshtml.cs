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
        // 유년부 더보기와 같은 항목 · 순서(2026-11-13). 전체반 현황 · 월별 통계 · 주일 명단은 유년부처럼 다른 화면에서 들어간다.
        var thisSunday = AttendanceService.SundayOf(attendance.Today);
        List<Item> all =
        [
            new("주일 출석 보고서", "반별 · 학생별 출석 (PC 화면)", $"/{slug}/attendance/report?sunday={thisSunday:yyyy-MM-dd}", true),
            new("최근 전체 출석현황(3주)", "최근 3주 반별 출석 추이", $"/{slug}/attendance/trend?date={thisSunday:yyyy-MM-dd}", true),
            new("아이별 출석 등수", "올해 누적 아이별 출석 등수", $"/{slug}/attendance/rank", true),
            new("아이별 주별 출석표", "아이 × 주일 표 · 1월 ~ 이번 달", $"/{slug}/attendance/matrix", true),
            new("교사 · 반 배정", "누가 어느 반 담임인지 정하기", $"/{slug}/admin/classes", true),
        ];
        Items = all.Where(i => tenant.IsAdmin || !i.AdminOnly).ToList();
        return Page();
    }
}
