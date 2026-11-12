using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Attendance;

/// <summary>
/// `/{slug}/attendance/care?absentDays=21` 돌봄(연락할 아이) — 유년부 출석부 "돌봄"(Views/Attendance/LongAbsent)과 같은 모양 · 기준(2/9).
/// 부서 관리자만. 3주 · 6주 · 3개월 · 6개월 이상, 기록 없는 아이 먼저 · 오래 빠진 순, [전화].
/// </summary>
[TenantRoles(Roles.TenantAdmin)]
public class CareModel(AttendanceService attendance, TenantContext tenant) : PageModel, ITitled
{
    public string Title => "돌봄";
    public TenantContext Tenant => tenant;

    [BindProperty(SupportsGet = true)] public int AbsentDays { get; set; } = 21;
    public AttendanceService.CareList Care { get; private set; } = null!;

    public static readonly (int Days, string Name)[] Filters = [(21, "3주 이상"), (42, "6주 이상"), (90, "3개월 이상"), (180, "6개월 이상")];
    public static string FilterName(int d) => Filters.FirstOrDefault(f => f.Days == d).Name ?? $"{d}일 이상";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (tenant.IsLobby) return NotFound();
        Care = await attendance.CareAsync(AbsentDays, ct);
        return Page();
    }

    public static string Tel(string? p) => new((p ?? "").Where(char.IsDigit).ToArray());
}
