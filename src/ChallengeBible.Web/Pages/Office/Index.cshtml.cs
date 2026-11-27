using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Office;

/// <summary>행정업무 화면 한 줄. Badge 가 있으면 이름 옆 딱지.</summary>
public sealed record OfficeItem(string No, string Href, string Label, string Desc, string Color, string Soft, string Icon, string? Badge);

/// <summary>
/// `/{slug}/office` 행정업무 메인(2026-11-25 사용자 시안 「행정업무 HTML」): 도전!!! 성경말씀과 따로, 교사 · 관리자가 쓰는 일
/// (오늘 할 일 · 출석 · 교적부 · 인명 검색 · 생일자 관리 · 명부 관리)을 한곳에. 이 사람이 볼 수 있는 것만 보인다. 모든 부서가 같다.
/// </summary>
public class IndexModel(TenantContext tenant, TodoService todo, MemberAdminService members, TimeProvider time) : PageModel, ITitled
{
    public string Title => "행정업무";
    public string Slug => tenant.Current.Slug;
    public TenantContext Tenant => tenant;
    /// <summary>로그인했지만 교사 · 관리자가 아니라 볼 수 없다(2026-11-26 사용자 — 「찾을 수 없음」 대신 「권한이 없어요」 안내).</summary>
    public bool Denied { get; private set; }
    public IReadOnlyList<OfficeItem> Items { get; private set; } = [];
    public int TodoCount { get; private set; }
    public bool HasTodo { get; private set; }
    public DateOnly Today { get; private set; }
    public string Initial => User.DisplayName() is { Length: > 0 } n ? n[..1] : "·";

    /// <summary>행정업무를 볼 수 있는 사람 — 교사 · 관리자 · 부장 · 부감 · 서기(인명 검색 · 교적부와 같은 기준).</summary>
    public static bool CanUse(TenantContext t) => !t.IsLobby && t.IsApproved && (t.IsStaff || t.IsAdmin || UserRole.KindOf(t.RoleCode) is UserRoleKind.Teacher or UserRoleKind.Officer);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        if (!CanUse(tenant)) { Denied = true; Response.StatusCode = StatusCodes.Status403Forbidden; return Page(); }
        var s = Slug;
        Today = TenantClock.ToTenantDate(time.GetUtcNow(), tenant.Current.TimeZoneId);

        HasTodo = tenant.IsStaff || tenant.IsAdmin;
        if (HasTodo) TodoCount = (await todo.BoardAsync(ct)).Items.Count;
        var pending = tenant.IsAdmin ? (await members.CountsAsync(ct)).GetValueOrDefault(MembershipStatus.Requested) : 0;
        var teacher = UserRole.KindOf(tenant.RoleCode) == UserRoleKind.Teacher;

        var list = new List<(string Href, string Label, string Desc, string C, string S, string I, string? Badge)>();

        // 순서(2026-11-27 새 퍼블리시본): 오늘 할 일 · 생일자 관리 · 출석 관리 · 주보 관리 · 교적부 · 인명 검색 · 명부 관리
        if (HasTodo) list.Add(($"/{s}/todo", "오늘 할 일", "", "#1f9d55", "#e8f6ee", "", TodoCount > 0 ? $"{TodoCount}건" : null));
        list.Add(($"/{s}/birthdays", "생일자 관리", "", "#7b57d6", "#f0ebfc", "", null));
        if (tenant.IsStaff) list.Add(($"/{s}/attendance", "출석 관리", "", "#2f6fe4", "#eaf1fd", "", null));
        if (tenant.IsStaff) list.Add(($"/{s}/bulletin", "주보 관리", "", "#c78a00", "#fcf4de", "", null));
        if (ClassBook.IndexModel.CanUse(tenant)) list.Add(($"/{s}/classbook", "교적부", "", "#ea6a1c", "#fdf0e6", "", null));
        if (People.IndexModel.CanUse(tenant)) list.Add(($"/{s}/people", "인명 검색", "", "#dc3f8a", "#fcebf3", "", null));
        if (tenant.IsAdmin || teacher) list.Add(($"/{s}/admin/members", "명부 관리", "", "#0d8f8a", "#e3f5f4", "", pending > 0 ? $"승인 {pending}" : null));

        Items = list.Select((x, i) => new OfficeItem($"{i + 1:00}", x.Href, x.Label, x.Desc, x.C, x.S, x.I, x.Badge)).ToList();
        return Page();
    }
}
