using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>
/// 부서 대표 학생 로그인(R-367, 확정 시안 첫화면-대표학생, 2026-11-22 사용자): 첫 화면 부서 관리자 상자 아래 "○○부 학생" 버튼,
/// 누르면 비밀번호만 넣고 그 부서의 대표 가짜 학생으로 들어간다. 버튼에는 아이 이름을 쓰지 않는다(명부가 진짜 아이와 이어져 있다).
/// 대표 = 그 부서 가짜 학생 계정(이메일 @fake-seed.test, 역할 학생, 관리자 아님) 중 ① 생일이 30일 안(가까울수록) ② 올해 미션 점수 ③ 기록(팀 · 통독 · 출석) 순.
/// 누를 때마다 다시 고른다(가짜 데이터를 지우고 다시 넣어도 맞게).
/// </summary>
public sealed class DeptStudentLoginService(ISystemDb systemDb, TenantClock clock)
{
    public const int BirthdayDays = 30;

    public sealed record Pick(DeptAdminButton Dept, string UserName);

    /// <summary>생일까지 남은 날(오늘이면 0). 2월 29일생은 평년 2월 28일. 생일이 없으면 null.</summary>
    public static int? DaysToBirthday(DateOnly today, byte? month, byte? day)
    {
        if (month is not (>= 1 and <= 12) || day is not >= 1) return null;
        DateOnly On(int y) => new(y, month.Value, Math.Min(day.Value, DateTime.DaysInMonth(y, month.Value)));
        var next = On(today.Year);
        if (next < today) next = On(today.Year + 1);
        return next.DayNumber - today.DayNumber;
    }

    public async Task<IReadOnlyList<Pick>> ListAsync(CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create("landing");   // 로그인 전
        var tenants = await ctx.Tenants.AsNoTracking().Where(t => t.Status == TenantStatus.Active && !t.IsLobby)
            .Select(t => new { t.Id, t.Slug, t.Name, t.TimeZoneId }).ToListAsync(ct);
        tenants = tenants.Where(t => DeptOrder.Key(t.Name) <= DeptAdminLoginService.LastAge).ToList();
        if (tenants.Count == 0) return [];
        var ids = tenants.Select(t => t.Id).ToList();

        var admins = await (from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id
                            where r.Name == IdentityRoles.Admin || r.Name == IdentityRoles.SuperAdmin select ur.UserId).ToListAsync(ct);
        var kids = await (from d in ctx.DeptRoles.AsNoTracking()
                          join u in ctx.Users.AsNoTracking() on d.UserId equals u.Id
                          where ids.Contains(d.TenantId) && d.RoleCode == UserRole.StudentCode
                                && u.Email != null && u.Email.EndsWith(TestToolsService.EmailDomain) && !admins.Contains(u.Id)
                          select new { d.TenantId, u.Id, u.UserName }).ToListAsync(ct);
        if (kids.Count == 0) return [];
        var userIds = kids.Select(k => k.Id).ToList();
        var names = kids.Select(k => k.UserName!).ToList();

        var people = await ctx.People.AsNoTracking().Where(p => p.UserId != null && userIds.Contains(p.UserId))
            .Select(p => new { p.TenantId, p.Id, p.UserId, p.BirthMonth, p.BirthDay }).ToListAsync(ct);
        var year = clock.Today("Asia/Seoul").Year;
        var from = new DateOnly(year, 1, 1);
        var points = (await ctx.MissionActivities.AsNoTracking().Where(a => names.Contains(a.UserName) && a.MissionDate >= from)
            .GroupBy(a => a.UserName).Select(g => new { g.Key, P = g.Sum(x => x.Points) }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.P);
        var teams = (await ctx.FamilyMembers.AsNoTracking().Where(m => names.Contains(m.UserName)).Select(m => m.UserName).ToListAsync(ct)).ToHashSet();
        var reading = (await ctx.ReadingRounds.AsNoTracking().Where(r => names.Contains(r.UserName)).GroupBy(r => r.UserName)
            .Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.N);
        var personIds = people.Select(p => p.Id).ToList();
        var attend = (await ctx.AttendanceChecks.AsNoTracking().Where(a => personIds.Contains(a.PersonId)).GroupBy(a => new { a.TenantId, a.PersonId })
            .Select(g => new { g.Key.TenantId, g.Key.PersonId, N = g.Count() }).ToListAsync(ct)).ToDictionary(x => (x.TenantId, x.PersonId), x => x.N);

        var list = new List<Pick>();
        foreach (var t in tenants.OrderBy(t => DeptOrder.Key(t.Name)).ThenBy(t => t.Id))
        {
            var today = clock.Today(t.TimeZoneId);
            var best = kids.Where(k => k.TenantId == t.Id).Select(k =>
                {
                    var p = people.FirstOrDefault(x => x.TenantId == t.Id && x.UserId == k.Id);
                    var days = p is null ? null : DaysToBirthday(today, p.BirthMonth, p.BirthDay);
                    var soon = days is int d && d <= BirthdayDays;
                    var rich = (teams.Contains(k.UserName!) ? 1 : 0) + reading.GetValueOrDefault(k.UserName!) + (p is null ? 0 : Math.Min(attend.GetValueOrDefault((t.Id, p.Id)), 10) / 5);
                    return new { k.UserName, Soon = soon, Days = days ?? 999, Points = points.GetValueOrDefault(k.UserName!), Rich = rich };
                })
                .OrderByDescending(x => x.Soon).ThenByDescending(x => x.Points).ThenByDescending(x => x.Rich).ThenBy(x => x.Days).ThenBy(x => x.UserName)
                .FirstOrDefault();
            if (best is not null) list.Add(new Pick(new DeptAdminButton(t.Slug, DeptAdminLoginService.ShortName(t.Name), DeptOrder.Key(t.Name)), best.UserName!));
        }
        return list;
    }

    public async Task<Pick?> FindAsync(string? slug, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(slug) ? null
            : (await ListAsync(ct)).FirstOrDefault(p => string.Equals(p.Dept.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
