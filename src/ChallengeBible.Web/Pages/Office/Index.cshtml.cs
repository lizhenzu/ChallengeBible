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

/// <summary>행정업무 칩(위쪽 요약).</summary>
public sealed record OfficeChip(string Label, string Count, string Color, string Soft, string Dark);

/// <summary>
/// `/{slug}/office` 행정업무 메인(2026-11-25 사용자 시안 「행정업무 HTML」): 도전!!! 성경말씀과 따로, 교사 · 관리자가 쓰는 일
/// (오늘 할 일 · 출석 · 교적부 · 인명 검색 · 생일자 관리 · 명부 관리)을 한곳에. 이 사람이 볼 수 있는 것만 보인다. 모든 부서가 같다.
/// </summary>
public class IndexModel(TenantContext tenant, TodoService todo, MemberAdminService members, TimeProvider time) : PageModel, ITitled
{
    public string Title => "행정업무";
    public string Slug => tenant.Current.Slug;
    public TenantContext Tenant => tenant;
    public IReadOnlyList<OfficeItem> Items { get; private set; } = [];
    public IReadOnlyList<OfficeChip> Chips { get; private set; } = [];
    public int TodoCount { get; private set; }
    public bool HasTodo { get; private set; }
    public DateOnly Today { get; private set; }
    public string Initial => User.DisplayName() is { Length: > 0 } n ? n[..1] : "·";

    /// <summary>행정업무를 볼 수 있는 사람 — 교사 · 관리자 · 부장 · 부감 · 서기(인명 검색 · 교적부와 같은 기준).</summary>
    public static bool CanUse(TenantContext t) => !t.IsLobby && t.IsApproved && (t.IsStaff || t.IsAdmin || UserRole.KindOf(t.RoleCode) is UserRoleKind.Teacher or UserRoleKind.Officer);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!CanUse(tenant)) return NotFound();
        var s = Slug;
        Today = TenantClock.ToTenantDate(time.GetUtcNow(), tenant.Current.TimeZoneId);

        HasTodo = tenant.IsStaff || tenant.IsAdmin;
        if (HasTodo) TodoCount = (await todo.BoardAsync(ct)).Items.Count;
        var pending = tenant.IsAdmin ? (await members.CountsAsync(ct)).GetValueOrDefault(MembershipStatus.Requested) : 0;
        var teacher = UserRole.KindOf(tenant.RoleCode) == UserRoleKind.Teacher;

        var list = new List<(string Href, string Label, string Desc, string C, string S, string I, string? Badge)>();
        const string ICO_TODO = "<path d=\"M6.5 4.5h11a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1h-11a1 1 0 0 1-1-1v-14a1 1 0 0 1 1-1z\"/><path d=\"M9 3v3M15 3v3\"/><path d=\"M8.7 12l2.2 2.2 4.1-4.3\"/>";
        const string ICO_ATT = "<path d=\"M4.5 6h15v14h-15z\"/><path d=\"M4.5 10.5h15\"/><path d=\"M9 3.5v4M15 3.5v4\"/><path d=\"M8.4 14.8l1.9 1.9 3.4-3.6\"/>";
        const string ICO_BOOK = "<path d=\"M4 7.2A1.4 1.4 0 0 1 5.4 5.8h4l1.8 2.2h7.4A1.4 1.4 0 0 1 20 9.4v8.4a1.4 1.4 0 0 1-1.4 1.4H5.4A1.4 1.4 0 0 1 4 17.8z\"/><path d=\"M8.5 13.5h7M8.5 16.5h4\"/>";
        const string ICO_FIND = "<path d=\"M11 18.2a7.2 7.2 0 1 0 0-14.4 7.2 7.2 0 0 0 0 14.4z\"/><path d=\"M16.3 16.3L21 21\"/>";
        const string ICO_BIRTH = "<path d=\"M4.5 12.5h15V20h-15z\"/><path d=\"M4.5 16.4c1.6 0 1.6 1.6 3.2 1.6s1.6-1.6 3.2-1.6 1.6 1.6 3.2 1.6 1.6-1.6 3.2-1.6\"/><path d=\"M12 9.6V7.2\"/><path d=\"M12 4.4v.8\"/>";
        const string ICO_ROSTER = "<path d=\"M9.2 11.4a3.2 3.2 0 1 0 0-6.4 3.2 3.2 0 0 0 0 6.4z\"/><path d=\"M3.6 20c.9-3.2 3-4.8 5.6-4.8s4.7 1.6 5.6 4.8\"/><path d=\"M16.2 5.7a3 3 0 0 1 0 5.6\"/><path d=\"M17.6 15.7c1.5.7 2.4 2.2 2.8 4.3\"/>";

        if (HasTodo) list.Add(($"/{s}/todo", "오늘 할 일", "오늘 처리할 일을 모아 봐요", "#1f9d55", "#e8f6ee", ICO_TODO, TodoCount > 0 ? $"{TodoCount}건" : null));
        if (tenant.IsStaff) list.Add(($"/{s}/attendance", "출석", "반별 출석을 확인하고 체크해요", "#2f6fe4", "#eaf1fd", ICO_ATT, null));
        if (ClassBook.IndexModel.CanUse(tenant)) list.Add(($"/{s}/classbook", "교적부", "반 · 학생 정보를 확인해요", "#ea6a1c", "#fdf0e6", ICO_BOOK, null));
        if (People.IndexModel.CanUse(tenant)) list.Add(($"/{s}/people", "인명 검색", "이름으로 사람을 찾아요", "#dc3f8a", "#fcebf3", ICO_FIND, null));
        list.Add(($"/{s}/birthdays", "생일자 관리", "생일자와 연락처를 확인해요", "#7b57d6", "#f0ebfc", ICO_BIRTH, null));
        if (tenant.IsAdmin || teacher) list.Add(($"/{s}/admin/members", "명부 관리", "등록 · 승인 · 반 배정을 처리해요", "#0d8f8a", "#e3f5f4", ICO_ROSTER, pending > 0 ? $"승인 {pending}" : null));

        Items = list.Select((x, i) => new OfficeItem($"{i + 1:00}", x.Href, x.Label, x.Desc, x.C, x.S, x.I, x.Badge)).ToList();
        var chips = new List<OfficeChip>();
        if (HasTodo) chips.Add(new("오늘 할 일", $"{TodoCount}건", "#1f9d55", "#e8f6ee", "#14663a"));
        if (pending > 0) chips.Add(new("명부 승인", pending.ToString(), "#0d8f8a", "#e3f5f4", "#0a645f"));
        Chips = chips;
        return Page();
    }
}
