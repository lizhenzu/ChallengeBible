using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>출석부에서 이 사람이 할 수 있는 일: 부서 관리자는 모든 반, 담임은 자기 반, 새친구반 담당은 새친구 화면.</summary>
public sealed record AttendScope(bool Admin, IReadOnlySet<int> Classes, bool NewcomerStaff)
{
    public bool Any => Admin || Classes.Count > 0 || NewcomerStaff;
    public bool Newcomers => Admin || NewcomerStaff;
}

public sealed record AttendKid(int PersonId, string Name, bool Present, IReadOnlyList<bool> Recent, int AbsentStreak, bool Newcomer);
public sealed record AttendGroup(int ClassId, string Name, string Teacher, bool Mine, List<AttendKid> Kids);
public sealed record AttendBoard(DateOnly Sunday, DateOnly ThisSunday, List<AttendGroup> Groups)
{
    public int Total => Groups.Sum(g => g.Kids.Count);
    public int Present => Groups.Sum(g => g.Kids.Count(k => k.Present));
}

public sealed record NewcomerRow(int PersonId, string Name, string ClassName, DateOnly FirstVisit, bool? Voluntary, string? Evangelist, int Visits, bool PresentToday, bool PastDays);

public sealed record NewcomerInput(string? Name, DateOnly? FirstVisit, int? BirthMonth, int? BirthDay, string? Phone1, string? Phone2, int? ClassId, bool Voluntary, string? Evangelist);

public enum AttendError { None, NotFound, Forbidden, BadDay, NoName, BadBirthday, BadPhone, BadDate }

/// <summary>
/// 주일 출석(ADR 0093 출석 1 ~ 8, R-234 ~ R-237). 출석 = 그 주일 · 그 사람의 한 줄, 취소 = 지운다(유년부와 같다).
/// 담임은 자기 반, 부서 관리자는 모든 반을 체크한다. 미배정반 아이는 반을 정하기 전까지 기록하지 않는다(새친구는 새친구 화면에서).
/// 새친구 관리는 첫 방문 뒤 28일이 지나고 출석이 4번 이상이면 끝난다.
/// </summary>
public sealed class AttendanceService(IAppDb db, ClassService classes, TenantContext tenant, TimeProvider time)
{
    public const int CareDays = 28, CareVisits = 4, RecentWeeks = 4, AbsentAlert = 3;

    public DateOnly Today => TenantClock.ToTenantDate(time.GetUtcNow(), tenant.Current.TimeZoneId);
    /// <summary>그 날짜가 든 주의 주일(그날이 일요일이면 그날).</summary>
    public static DateOnly SundayOf(DateOnly d) => d.AddDays(-(int)d.DayOfWeek);

    public async Task<AttendScope> ScopeAsync(string userName, CancellationToken ct = default)
    {
        await classes.ClassesAsync(ct);   // 새친구반 · 반 묶음이 없으면 만든다
        await using var ctx = db.Create();
        var me = await (from u in ctx.Users join p in ctx.People on u.Id equals p.UserId where u.UserName == userName select p.Id).FirstOrDefaultAsync(ct);
        var teach = await (from m in ctx.ClassMembers join c in ctx.Classes on m.ClassId equals c.Id
                           where m.PersonId == me && m.IsTeacher select new { c.Id, c.IsNewcomer }).ToListAsync(ct);
        return new AttendScope(tenant.IsAdmin, teach.Where(t => !t.IsNewcomer).Select(t => t.Id).ToHashSet(), teach.Any(t => t.IsNewcomer));
    }

    /// <summary>반 출석부(R-234 · R-235): 볼 수 있는 정규 반의 학생, 그 주일 출석 · 지난 4주 · 연속 결석.</summary>
    public async Task<AttendBoard> BoardAsync(AttendScope scope, DateOnly? day, CancellationToken ct = default)
    {
        var thisSunday = SundayOf(Today);
        var sunday = day is { } d ? SundayOf(d) : thisSunday;
        if (sunday > thisSunday) sunday = thisSunday;
        var list = await classes.ClassesAsync(ct);
        var roster = (await classes.RosterAsync(ct)).ToDictionary(r => r.PersonId);
        await using var ctx = db.Create();
        var members = await ctx.ClassMembers.AsNoTracking().Where(m => !m.IsTeacher).ToListAsync(ct);
        var from = sunday.AddDays(-7 * RecentWeeks);
        var checks = (await ctx.AttendanceChecks.AsNoTracking().Where(a => a.Sunday >= from && a.Sunday <= sunday).Select(a => new { a.PersonId, a.Sunday }).ToListAsync(ct))
            .Select(a => (a.PersonId, a.Sunday)).ToHashSet();
        var active = await ActiveNewcomersAsync(ctx, ct);
        var ko = StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false);
        var groups = new List<AttendGroup>();
        foreach (var c in list.Where(c => !c.IsNewcomer && (scope.Admin || scope.Classes.Contains(c.Id))))
        {
            var kids = members.Where(m => m.ClassId == c.Id && roster.ContainsKey(m.PersonId)).Select(m =>
            {
                var recent = Enumerable.Range(1, RecentWeeks).Reverse().Select(i => checks.Contains((m.PersonId, sunday.AddDays(-7 * i)))).ToList();
                var streak = 0;
                for (var i = recent.Count - 1; i >= 0 && !recent[i]; i--) streak++;
                return new AttendKid(m.PersonId, roster[m.PersonId].Name, checks.Contains((m.PersonId, sunday)), recent, streak, active.ContainsKey(m.PersonId));
            }).OrderBy(k => k.Name, ko).ToList();
            if (kids.Count > 0) groups.Add(new AttendGroup(c.Id, c.Name, string.Join(" · ", c.Teachers), scope.Classes.Contains(c.Id), kids));
        }
        return new AttendBoard(sunday, thisSunday, groups.OrderByDescending(g => g.Mine).ToList());
    }

    /// <summary>
    /// 출석 · 취소(R-234 · R-235). 주일만, 미래 주일은 안 된다. 담임은 자기 반 학생, 새친구반 담당은 관리 중인 새친구, 부서 관리자는 반이 있는 학생 · 새친구.
    /// 미배정반 아이(새친구 아님)는 기록하지 않는다.
    /// </summary>
    public async Task<AttendError> SetAsync(AttendScope scope, string actor, int personId, DateOnly sunday, bool present, CancellationToken ct = default)
    {
        if (sunday.DayOfWeek != DayOfWeek.Sunday || sunday > SundayOf(Today)) return AttendError.BadDay;
        await using var ctx = db.Create();
        if (!await ctx.People.AnyAsync(p => p.Id == personId, ct)) return AttendError.NotFound;
        var cls = await (from m in ctx.ClassMembers join c in ctx.Classes on m.ClassId equals c.Id
                         where m.PersonId == personId && !m.IsTeacher && !c.IsNewcomer select (int?)c.Id).FirstOrDefaultAsync(ct);
        var newcomer = (await ActiveNewcomersAsync(ctx, ct)).ContainsKey(personId);
        var allowed = (cls is int id && (scope.Admin || scope.Classes.Contains(id))) || (newcomer && scope.Newcomers);
        if (!allowed) return AttendError.Forbidden;
        var row = await ctx.AttendanceChecks.SingleOrDefaultAsync(a => a.PersonId == personId && a.Sunday == sunday, ct);
        if (present && row is null)
            ctx.AttendanceChecks.Add(new AttendanceCheck { TenantId = tenant.Current.Id, PersonId = personId, Sunday = sunday, CreatedUtc = time.GetUtcNow().UtcDateTime, CreatedByUserName = actor });
        else if (!present && row is not null) ctx.AttendanceChecks.Remove(row);
        await ctx.SaveChangesAsync(ct);
        return AttendError.None;
    }

    /// <summary>관리 중인 새친구(R-237): 첫 방문이 있고, 28일이 지나고 출석 4번 이상이 되기 전. 값 = 첫 방문 이후 출석 수.</summary>
    private Task<Dictionary<int, int>> ActiveNewcomersAsync(AppDbContext ctx, CancellationToken ct) => ActiveNewcomersAsync(ctx, Today, ct);

    public static async Task<Dictionary<int, int>> ActiveNewcomersAsync(AppDbContext ctx, DateOnly today, CancellationToken ct)
    {
        var ncs = await ctx.People.AsNoTracking().Where(p => p.NewcomerFirstVisit != null).Select(p => new { p.Id, First = p.NewcomerFirstVisit!.Value }).ToListAsync(ct);
        if (ncs.Count == 0) return [];
        var ids = ncs.Select(n => n.Id).ToList();
        var visits = await ctx.AttendanceChecks.AsNoTracking().Where(a => ids.Contains(a.PersonId)).Select(a => new { a.PersonId, a.Sunday }).ToListAsync(ct);
        var result = new Dictionary<int, int>();
        foreach (var n in ncs)
        {
            var count = visits.Count(v => v.PersonId == n.Id && v.Sunday >= SundayOf(n.First));
            if (!IsCareOver(n.First, count, today)) result[n.Id] = count;
        }
        return result;
    }

    public static bool IsCareOver(DateOnly firstVisit, int visits, DateOnly today) => firstVisit.AddDays(CareDays) < today && visits >= CareVisits;

    /// <summary>새친구 화면(R-237): 관리 중인 새친구, 반(없으면 미배정반) · 첫 방문 · 출석 수 · 그 주일 출석.</summary>
    public async Task<List<NewcomerRow>> NewcomersAsync(DateOnly sunday, CancellationToken ct = default)
    {
        var list = await classes.ClassesAsync(ct);
        var names = list.ToDictionary(c => c.Id, c => c.Name);
        await using var ctx = db.Create();
        var active = await ActiveNewcomersAsync(ctx, ct);
        var ids = active.Keys.ToList();
        var people = await ctx.People.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        var cls = await ctx.ClassMembers.AsNoTracking().Where(m => ids.Contains(m.PersonId) && !m.IsTeacher).ToDictionaryAsync(m => m.PersonId, m => m.ClassId, ct);
        var present = (await ctx.AttendanceChecks.AsNoTracking().Where(a => ids.Contains(a.PersonId) && a.Sunday == sunday).Select(a => a.PersonId).ToListAsync(ct)).ToHashSet();
        var today = Today;
        return people.OrderByDescending(p => p.NewcomerFirstVisit).ThenBy(p => p.Name)
            .Select(p => new NewcomerRow(p.Id, p.Name, cls.TryGetValue(p.Id, out var c) && names.TryGetValue(c, out var n) ? n : ClassService.UnassignedName,
                p.NewcomerFirstVisit!.Value, p.NewcomerVoluntary, p.NewcomerEvangelist, active[p.Id], present.Contains(p.Id),
                p.NewcomerFirstVisit!.Value.AddDays(CareDays) < today)).ToList();
    }

    /// <summary>새친구 등록(R-236): 명부에 학생으로 올리고, 반을 고르면 그 반에(아니면 미배정반), 첫 방문 주일에 출석을 체크한다.</summary>
    public async Task<(AttendError Error, int PersonId)> RegisterAsync(string actor, NewcomerInput input, CancellationToken ct = default)
    {
        var (e, name, bm, bd, p1, p2) = PeopleService.Check(new PersonInput(input.Name, null, input.BirthMonth, input.BirthDay, input.Phone1, input.Phone2), needName: true);
        if (e != PeopleError.None) return (e switch { PeopleError.NoName => AttendError.NoName, PeopleError.BadBirthday => AttendError.BadBirthday, _ => AttendError.BadPhone }, 0);
        if (input.FirstVisit is not { } first || first > Today) return (AttendError.BadDate, 0);
        var ev = (input.Evangelist ?? "").Trim();
        await using var ctx = db.Create();
        var now = time.GetUtcNow().UtcDateTime;
        var p = new Person
        {
            TenantId = tenant.Current.Id, Name = name!, RoleCode = UserRole.StudentCode, BirthMonth = bm, BirthDay = bd, Phone1 = p1, Phone2 = p2,
            NewcomerFirstVisit = first, NewcomerVoluntary = input.Voluntary, NewcomerEvangelist = input.Voluntary || ev.Length == 0 ? null : (ev.Length > 50 ? ev[..50] : ev),
            CreatedUtc = now, CreatedByUserName = actor,
        };
        ctx.People.Add(p);
        await ctx.SaveChangesAsync(ct);
        ctx.AttendanceChecks.Add(new AttendanceCheck { TenantId = tenant.Current.Id, PersonId = p.Id, Sunday = SundayOf(first), CreatedUtc = now, CreatedByUserName = actor });
        await ctx.SaveChangesAsync(ct);
        if (input.ClassId is int cid && cid > 0)
        {
            var err = await classes.AddAsync(cid, [p.Id], teacher: false, ct);
            if (err != ClassError.None) return (AttendError.NotFound, p.Id);
        }
        return (AttendError.None, p.Id);
    }

    public static string Message(AttendError e) => e switch
    {
        AttendError.NotFound => "찾을 수 없어요. 화면을 새로 고쳐 주세요.",
        AttendError.Forbidden => "이 아이는 체크할 수 없어요(내 반 · 새친구만).",
        AttendError.BadDay => "주일만, 이번 주일까지 체크할 수 있어요.",
        AttendError.NoName => "이름을 넣어 주세요.",
        AttendError.BadBirthday => "생일의 월 · 일을 둘 다 맞게 골라 주세요.",
        AttendError.BadPhone => "연락처는 숫자와 - 로 넣어 주세요(예: 010-1234-5678).",
        AttendError.BadDate => "첫 방문일을 오늘이나 그 전으로 넣어 주세요.",
        _ => "",
    };
}
