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
        // 새친구반 담당 · 관리자는 새친구를 첫 방문 주일부터 고칠 수 있다 — 4주 관리가 끝난 새친구도(기존 출석 변경, 2026-11-12)
        var first = await ctx.People.Where(p => p.Id == personId).Select(p => p.NewcomerFirstVisit).SingleAsync(ct);
        var newcomer = first is { } f && sunday >= SundayOf(f);
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

    /// <summary>4주 관리가 끝난 날: 4번째 출석 주일과 첫 방문 뒤 29일째 중 늦은 날. 아직이면 null.</summary>
    public static DateOnly? CareOverOn(DateOnly firstVisit, IReadOnlyList<DateOnly> visitSundays)
    {
        var v = visitSundays.Where(s => s >= SundayOf(firstVisit)).OrderBy(s => s).ToList();
        if (v.Count < CareVisits) return null;
        var d = v[CareVisits - 1] > firstVisit.AddDays(CareDays + 1) ? v[CareVisits - 1] : firstVisit.AddDays(CareDays + 1);
        return d;
    }

    // ── 돌봄(연락할 아이) — 유년부 출석부 "돌봄"(LongAbsent)과 같은 기준 ──

    public sealed record CareRow(int PersonId, string Name, string ClassName, DateOnly? LastAttend, int DaysAbsent, int AttendCount, string? Phone);
    public sealed record CareList(int AbsentDays, int WeeksCount, List<CareRow> Rows);
    public static readonly int[] CareFilters = [21, 42, 90, 180];

    /// <summary>
    /// 학생(학생 · 새친구 역할) 중 출석 기록이 없거나 마지막 출석이 오늘부터 absentDays 일보다 전인 아이.
    /// 기록 없는 아이 먼저, 그다음 오래 빠진 순(유년부와 같다). 올해 출석 수 · 올해 주일 수, 연락처는 연락처 1 없으면 2.
    /// </summary>
    public async Task<CareList> CareAsync(int absentDays, CancellationToken ct = default)
    {
        if (absentDays < 1) absentDays = 21;
        var today = Today;
        var thisSunday = SundayOf(today);
        var jan1 = new DateOnly(thisSunday.Year, 1, 1);
        var firstSunday = jan1.AddDays(((int)DayOfWeek.Sunday - (int)jan1.DayOfWeek + 7) % 7);
        var weeks = Math.Max(1, firstSunday > thisSunday ? 0 : (thisSunday.DayNumber - firstSunday.DayNumber) / 7 + 1);
        var cutoff = today.AddDays(-absentDays);

        var roster = (await classes.RosterAsync(ct)).Where(ClassService.IsStudent).ToList();
        var names = (await classes.ClassesAsync(ct)).ToDictionary(c => c.Id, c => c.Name);
        await using var ctx = db.Create();
        var ids = roster.Select(r => r.PersonId).ToList();
        var checks = await ctx.AttendanceChecks.AsNoTracking().Where(a => ids.Contains(a.PersonId)).Select(a => new { a.PersonId, a.Sunday }).ToListAsync(ct);
        var cls = await ctx.ClassMembers.AsNoTracking().Where(m => ids.Contains(m.PersonId) && !m.IsTeacher).ToDictionaryAsync(m => m.PersonId, m => m.ClassId, ct);
        var rows = new List<CareRow>();
        foreach (var r in roster)
        {
            var mine = checks.Where(c => c.PersonId == r.PersonId).Select(c => c.Sunday).ToList();
            DateOnly? last = mine.Count == 0 ? null : mine.Max();
            if (last is { } l && l >= cutoff) continue;
            var cn = cls.TryGetValue(r.PersonId, out var c) && names.TryGetValue(c, out var n) ? n : ClassService.UnassignedName;
            rows.Add(new CareRow(r.PersonId, r.Name, cn, last, last is { } d ? today.DayNumber - d.DayNumber : 0,
                mine.Count(s => s >= firstSunday && s <= thisSunday), string.IsNullOrWhiteSpace(r.Phone1) ? r.Phone2 : r.Phone1));
        }
        var ko = StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false);
        return new CareList(absentDays, weeks, rows.OrderByDescending(x => x.LastAttend is null ? int.MaxValue : x.DaysAbsent).ThenBy(x => x.Name, ko).ToList());
    }

    /// <summary>아래 탭 "돌봄" 위 숫자: 3주(21일) 기준 연락할 아이 수.</summary>
    public async Task<int> CareCountAsync(CancellationToken ct = default) => (await CareAsync(21, ct)).Rows.Count;

    // ── 주일 출결 보고서(PC) · 주일 출석 명단(휴대폰) — 유년부 SundayStudentsPC · Mobile 과 같은 내용 ──

    public sealed record RepKid(int PersonId, string Name, bool Present, int YearCount, bool Newcomer, int Visits);
    public sealed record RepClass(int Id, string Name, string Group, string Short, string Teachers, List<RepKid> Kids);
    public sealed record SundayReport(DateOnly Sunday, DateOnly ThisSunday, int WeeksCount, List<RepClass> Classes);

    /// <summary>반 이름 "1학년 2반" · "1학년2반" → 묶음 "1학년", 짧은 이름 "2반". 학년이 없으면 묶음 "반"(이름 그대로).</summary>
    public static (string Group, string Short) SplitClassName(string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(name.Trim(), @"^(\d+\s*학년)\s*(.+)$");
        return m.Success ? (m.Groups[1].Value.Replace(" ", ""), m.Groups[2].Value) : ("반", name);
    }

    /// <summary>
    /// 그 주일의 반별 명단: 정규 반(새친구반 제외) 학생과 미배정반(있을 때만, 끝에). 출석 여부 · 올해(그 주일까지) 출석 수 ·
    /// 그 주일에 관리 중인 새친구(R-237)면 첫 방문부터 그 주일까지 출석 수. 주일은 이번 주일보다 뒤로 못 간다.
    /// </summary>
    public async Task<SundayReport> SundayReportAsync(DateOnly? day, CancellationToken ct = default)
    {
        var thisSunday = SundayOf(Today);
        var sunday = SundayOf(day ?? thisSunday);
        if (sunday > thisSunday) sunday = thisSunday;
        var jan1 = new DateOnly(sunday.Year, 1, 1);
        var firstSunday = jan1.AddDays(((int)DayOfWeek.Sunday - (int)jan1.DayOfWeek + 7) % 7);
        var weeks = Math.Max(1, (sunday.DayNumber - firstSunday.DayNumber) / 7 + 1);

        var roster = (await classes.RosterAsync(ct)).Where(ClassService.IsStudent).ToDictionary(r => r.PersonId);
        var list = (await classes.ClassesAsync(ct)).Where(c => !c.IsNewcomer).ToList();
        await using var ctx = db.Create();
        var ids = roster.Keys.ToList();
        var members = await ctx.ClassMembers.AsNoTracking().Where(m => !m.IsTeacher && ids.Contains(m.PersonId)).Select(m => new { m.PersonId, m.ClassId }).ToListAsync(ct);
        var checks = await ctx.AttendanceChecks.AsNoTracking().Where(a => ids.Contains(a.PersonId) && a.Sunday <= sunday).Select(a => new { a.PersonId, a.Sunday }).ToListAsync(ct);
        var firsts = await ctx.People.AsNoTracking().Where(p => ids.Contains(p.Id) && p.NewcomerFirstVisit != null).ToDictionaryAsync(p => p.Id, p => p.NewcomerFirstVisit!.Value, ct);
        var ko = StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false);

        RepKid Kid(int pid)
        {
            var mine = checks.Where(c => c.PersonId == pid).Select(c => c.Sunday).ToList();
            var nc = false; var visits = 0;
            if (firsts.TryGetValue(pid, out var first) && SundayOf(first) <= sunday)
            {
                visits = mine.Count(s => s >= SundayOf(first));
                nc = !IsCareOver(first, visits, sunday);
            }
            return new RepKid(pid, roster[pid].Name, mine.Contains(sunday), mine.Count(s => s >= firstSunday), nc, visits);
        }

        var result = list.Select(c =>
        {
            var (g, s) = SplitClassName(c.Name);
            var kids = members.Where(m => m.ClassId == c.Id).Select(m => Kid(m.PersonId)).OrderBy(k => k.Name, ko).ToList();
            return new RepClass(c.Id, c.Name, g, s, string.Join(", ", c.Teachers), kids);
        }).ToList();
        var placed = members.Select(m => m.PersonId).ToHashSet();
        var loose = ids.Where(id => !placed.Contains(id)).Select(Kid).OrderBy(k => k.Name, ko).ToList();
        if (loose.Count > 0) result.Add(new RepClass(0, ClassService.UnassignedName, "기타", ClassService.UnassignedName, "", loose));
        return new SundayReport(sunday, thisSunday, weeks, result);
    }

    public sealed record Matrix(SundayReport Rep, List<DateOnly> Sundays, HashSet<(int PersonId, DateOnly Sunday)> Present);

    /// <summary>
    /// 아이별 주별 출석표(유년부 AdminStudentMatrixWeeks): 기준 주일까지 weeks 주일(1 ~ 52, 없으면 올해 주일 수 · 최소 4) ×
    /// R-243 명단 아이. 칸 = 그 주일 출석 여부.
    /// </summary>
    public async Task<Matrix> MatrixAsync(DateOnly? end, int? weeks, CancellationToken ct = default)
    {
        var rep = await SundayReportAsync(end, ct);
        var n = Math.Clamp(weeks ?? Math.Max(4, rep.WeeksCount), 1, 52);
        var sundays = Enumerable.Range(0, n).Select(i => rep.Sunday.AddDays(-7 * (n - 1 - i))).ToList();
        var ids = rep.Classes.SelectMany(c => c.Kids).Select(k => k.PersonId).ToList();
        await using var ctx = db.Create();
        var start = sundays[0];
        var rows = await ctx.AttendanceChecks.AsNoTracking().Where(a => ids.Contains(a.PersonId) && a.Sunday >= start && a.Sunday <= rep.Sunday)
            .Select(a => new { a.PersonId, a.Sunday }).ToListAsync(ct);
        return new Matrix(rep, sundays, rows.Select(a => (a.PersonId, a.Sunday)).ToHashSet());
    }

    // ── 월별 출석 통계 · 상세(유년부 Statistics · StatisticsDetail, 8/9) ──

    public sealed record MonthWeek(DateOnly Sunday, int Count, bool Future);
    public sealed record MonthClass(int Id, string Name, int Total, double Ratio);
    public sealed record MonthStats(int Year, int Month, DateOnly RefSunday, List<MonthWeek> Weeks, List<MonthClass> Classes)
    {
        public int Total => Weeks.Sum(w => w.Count);
        public int SundayCount => Weeks.Count;
        /// <summary>평균 출석(주별) = 총 출석 ÷ 주일 수, 소수 첫째 자리.</summary>
        public double Average => SundayCount == 0 ? 0 : Math.Round(Total * 10.0 / SundayCount) / 10;
        /// <summary>이번 달 출석 TOP 3 반(출석 0 인 반은 빼고).</summary>
        public IEnumerable<MonthClass> Top3 => Classes.Where(c => c.Total > 0).Take(3);
    }

    /// <summary>
    /// 그 달의 주일마다 전체 출석 수와 반별 출석 합계(R-248). 반 · 아이는 그 달 마지막 지난 주일(달이 아직 안 왔으면 이번 주일)의 R-243 명단.
    /// 비율 = 반 합계 ÷ 그 달 전체 합계. 반은 합계가 큰 순(같으면 이름 순). 오지 않은 주일은 0.
    /// </summary>
    public async Task<MonthStats> MonthStatsAsync(int year, int month, CancellationToken ct = default)
    {
        year = Math.Clamp(year, 2000, 2100);
        month = Math.Clamp(month, 1, 12);
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var sundays = Enumerable.Range(0, last.Day).Select(i => first.AddDays(i)).Where(d => d.DayOfWeek == DayOfWeek.Sunday).ToList();
        var thisSunday = SundayOf(Today);
        var past = sundays.Where(s => s <= thisSunday).ToList();
        var rep = await SundayReportAsync(past.Count > 0 ? past[^1] : thisSunday, ct);
        var ids = rep.Classes.SelectMany(c => c.Kids).Select(k => k.PersonId).ToList();
        await using var ctx = db.Create();
        var checks = await ctx.AttendanceChecks.AsNoTracking().Where(a => ids.Contains(a.PersonId) && a.Sunday >= first && a.Sunday <= last)
            .Select(a => new { a.PersonId, a.Sunday }).ToListAsync(ct);
        var weeks = sundays.Select(s => new MonthWeek(s, checks.Count(c => c.Sunday == s), s > thisSunday)).ToList();
        var all = checks.Count;
        var ko = StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false);
        var classes = rep.Classes.Select(c =>
            {
                var kids = c.Kids.Select(k => k.PersonId).ToHashSet();
                var t = checks.Count(x => kids.Contains(x.PersonId));
                return new MonthClass(c.Id, c.Name, t, all == 0 ? 0 : Math.Round(t * 1000.0 / all) / 10);
            })
            .OrderByDescending(c => c.Total).ThenBy(c => c.Name, ko).ToList();
        return new MonthStats(year, month, rep.Sunday, weeks, classes);
    }

    // ── 새친구 화면(확정 시안 K1 · K2 · K5 · K6 · K7, 2026-11-12) ──

    public sealed record NcWeek(DateOnly Sunday, bool Present);
    public sealed record NcActive(int PersonId, string Name, string ClassName, DateOnly FirstVisit, bool? Voluntary, string? Evangelist, int Visits, bool PresentThisSunday, bool PastDays, IReadOnlyList<bool> Recent);
    public sealed record NcDone(int PersonId, string Name, string ClassName, DateOnly FirstVisit, DateOnly DoneOn, int Visits, IReadOnlyList<NcWeek> Weeks);
    public sealed record NcHome(DateOnly ThisSunday, List<NcActive> Active, List<NcDone> Done);
    public sealed record NcSunday(DateOnly Sunday, bool Future, int Newcomers, int Present);
    public sealed record NcOn(int PersonId, string Name, bool Active, bool Present);

    /// <summary>새친구 홈: 4주 관리 중(먼저) · 4주 관리 완료(뒤, 끝난 날 최근 순). 완료 학생은 첫 방문부터 끝난 주일까지 주일별 출석.</summary>
    public async Task<NcHome> NewcomerHomeAsync(CancellationToken ct = default)
    {
        var today = Today;
        var thisSunday = SundayOf(today);
        var names = (await classes.ClassesAsync(ct)).ToDictionary(c => c.Id, c => c.Name);
        await using var ctx = db.Create();
        var people = await ctx.People.AsNoTracking().Where(p => p.NewcomerFirstVisit != null).ToListAsync(ct);
        var ids = people.Select(p => p.Id).ToList();
        var visits = (await ctx.AttendanceChecks.AsNoTracking().Where(a => ids.Contains(a.PersonId)).Select(a => new { a.PersonId, a.Sunday }).ToListAsync(ct))
            .GroupBy(a => a.PersonId).ToDictionary(g => g.Key, g => g.Select(x => x.Sunday).ToHashSet());
        var cls = await ctx.ClassMembers.AsNoTracking().Where(m => ids.Contains(m.PersonId) && !m.IsTeacher).ToDictionaryAsync(m => m.PersonId, m => m.ClassId, ct);
        string ClassOf(int pid) => cls.TryGetValue(pid, out var c) && names.TryGetValue(c, out var n) ? n : ClassService.UnassignedName;
        var active = new List<NcActive>();
        var done = new List<NcDone>();
        foreach (var p in people)
        {
            var first = p.NewcomerFirstVisit!.Value;
            var mine = visits.GetValueOrDefault(p.Id) ?? [];
            var count = mine.Count(s => s >= SundayOf(first));
            if (!IsCareOver(first, count, today))
            {
                var recent = Enumerable.Range(1, RecentWeeks).Reverse().Select(i => mine.Contains(thisSunday.AddDays(-7 * i))).ToList();
                active.Add(new NcActive(p.Id, p.Name, ClassOf(p.Id), first, p.NewcomerVoluntary, p.NewcomerEvangelist, count, mine.Contains(thisSunday), first.AddDays(CareDays) < today, recent));
                continue;
            }
            var doneOn = CareOverOn(first, [.. mine]) ?? today;
            var weeks = new List<NcWeek>();
            for (var s = SundayOf(first); s <= SundayOf(doneOn) && s <= thisSunday; s = s.AddDays(7)) weeks.Add(new NcWeek(s, mine.Contains(s)));
            done.Add(new NcDone(p.Id, p.Name, ClassOf(p.Id), first, doneOn, count, weeks));
        }
        return new NcHome(thisSunday,
            active.OrderByDescending(a => a.FirstVisit).ThenBy(a => a.Name).ToList(),
            done.OrderByDescending(d => d.DoneOn).ThenBy(d => d.Name).ToList());
    }

    /// <summary>기존 출석 변경 · 주일 고르기: 그 달의 주일마다 새친구 수(그 주일까지 첫 방문) · 출석 수. 오지 않은 주일은 Future.</summary>
    public async Task<List<NcSunday>> NewcomerSundaysAsync(int year, int month, CancellationToken ct = default)
    {
        var thisSunday = SundayOf(Today);
        var d = new DateOnly(year, month, 1);
        var sundays = new List<DateOnly>();
        for (var s = d.AddDays(((int)DayOfWeek.Sunday - (int)d.DayOfWeek + 7) % 7); s.Month == month; s = s.AddDays(7)) sundays.Add(s);
        await using var ctx = db.Create();
        var people = await ctx.People.AsNoTracking().Where(p => p.NewcomerFirstVisit != null).Select(p => new { p.Id, First = p.NewcomerFirstVisit!.Value }).ToListAsync(ct);
        var ids = people.Select(p => p.Id).ToList();
        var from = sundays.First(); var to = sundays.Last();
        var checks = (await ctx.AttendanceChecks.AsNoTracking().Where(a => ids.Contains(a.PersonId) && a.Sunday >= from && a.Sunday <= to).Select(a => new { a.PersonId, a.Sunday }).ToListAsync(ct))
            .Select(a => (a.PersonId, a.Sunday)).ToHashSet();
        return sundays.Select(s =>
        {
            var on = people.Where(p => SundayOf(p.First) <= s).ToList();
            return new NcSunday(s, s > thisSunday, on.Count, on.Count(p => checks.Contains((p.Id, s))));
        }).ToList();
    }

    /// <summary>기존 출석 변경 · 그 주일: 그 주일까지 첫 방문한 새친구 모두(관리 중 · 완료), 이름 순.</summary>
    public async Task<List<NcOn>> NewcomersOnAsync(DateOnly sunday, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var active = await ActiveNewcomersAsync(ctx, ct);
        var people = (await ctx.People.AsNoTracking().Where(p => p.NewcomerFirstVisit != null).Select(p => new { p.Id, p.Name, First = p.NewcomerFirstVisit!.Value }).ToListAsync(ct))
            .Where(p => SundayOf(p.First) <= sunday).ToList();
        var ids = people.Select(p => p.Id).ToList();
        var present = (await ctx.AttendanceChecks.AsNoTracking().Where(a => ids.Contains(a.PersonId) && a.Sunday == sunday).Select(a => a.PersonId).ToListAsync(ct)).ToHashSet();
        var ko = StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false);
        return people.OrderBy(p => p.Name, ko).Select(p => new NcOn(p.Id, p.Name, active.ContainsKey(p.Id), present.Contains(p.Id))).ToList();
    }

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
