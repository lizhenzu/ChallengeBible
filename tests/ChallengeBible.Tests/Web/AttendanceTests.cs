using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>출석(ADR 0093 출석): R-234 주일 · 출석/취소, R-235 담임 · 관리자 범위 · 미배정 기록 안 함, R-236 새친구 등록, R-237 새친구 관리 끝.</summary>
public class AttendanceTests
{
    private static readonly DateOnly Sun = new(2027, 1, 3);   // FixedTime 2027-01-05(화)의 이번 주일
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids, _elem;
    private readonly AppUser _teacher;
    private readonly int _c1, _c2, _kidA, _kidB, _loose, _teacherPid;

    public AttendanceTests()
    {
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        InMemoryDb.SeedMembership(_db, _kids.Id, InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜").UserName!, MembershipStatus.Approved, Roles.TenantAdmin);
        InMemoryDb.SeedMembership(_db, _elem.Id, InMemoryDb.SeedAccount(_db, "minsu@example.com", "박민수").UserName!, MembershipStatus.Approved, Roles.TenantAdmin);
        _teacher = InMemoryDb.SeedAccount(_db, "jeong@example.com", "정교사");
        InMemoryDb.SeedMembership(_db, _kids.Id, _teacher.UserName!, MembershipStatus.Approved, Roles.Teacher);
        var cls = Classes(_kids);
        _c1 = cls.CreateAsync("1학년1반").Result.Id;
        _c2 = cls.CreateAsync("1학년2반").Result.Id;
        _kidA = Person("김철수", "T1001");
        _kidB = Person("박하린", "T1001");
        _loose = Person("정하늘", "T1001");
        _teacherPid = People(_kids).RosterAsync(null, null).Result.Single(r => r.UserName == "jeong").PersonId;
        cls.AddAsync(_c1, [_kidA], teacher: false).Wait();
        cls.AddAsync(_c2, [_kidB], teacher: false).Wait();
        cls.AddAsync(_c1, [_teacherPid], teacher: true).Wait();
    }

    private (InMemoryDb Db, TenantContext Ctx) In(Tenant t, string? role = null, bool admin = false)
    {
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        var ctx = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db)) ctx.Set(raw.Tenants.Single(x => x.Id == t.Id), new DeptRoleUserAssoc { RoleCode = role ?? "T1002" }, admin);
        return (new InMemoryDb(_db, scope), ctx);
    }

    private ClassService Classes(Tenant t) { var (d, c) = In(t, admin: true); return new ClassService(d, new PeopleService(d, c, _time), c, _time); }
    private PeopleService People(Tenant t) { var (d, c) = In(t, admin: true); return new PeopleService(d, c, _time); }
    private AttendanceService Att(Tenant t, bool admin) { var (d, c) = In(t, admin: admin); return new AttendanceService(d, new ClassService(d, new PeopleService(d, c, _time), c, _time), c, _time); }
    private int Person(string name, string? role) => People(_kids).AddAsync("eunhye", new PersonInput(name, role, null, null, null, null)).Result.Id;

    [Fact]
    public async Task R234_주일만_이번_주일까지_누르면_출석_다시_누르면_취소()
    {
        var a = Att(_kids, admin: true);
        var scope = await a.ScopeAsync("eunhye");
        Assert.Equal(AttendError.BadDay, await a.SetAsync(scope, "eunhye", _kidA, Sun.AddDays(1), true));    // 월요일
        Assert.Equal(AttendError.BadDay, await a.SetAsync(scope, "eunhye", _kidA, Sun.AddDays(7), true));    // 다음 주일
        Assert.Equal(AttendError.None, await a.SetAsync(scope, "eunhye", _kidA, Sun, true));
        Assert.Equal(AttendError.None, await a.SetAsync(scope, "eunhye", _kidA, Sun, true));                 // 두 번 눌러도 한 줄
        Assert.Equal(AttendError.None, await a.SetAsync(scope, "eunhye", _kidA, Sun.AddDays(-7), true));     // 지난 주일도 고친다
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(2, raw.AttendanceChecks.Count(x => x.PersonId == _kidA));
        Assert.Equal(AttendError.None, await a.SetAsync(scope, "eunhye", _kidA, Sun, false));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Single(raw.AttendanceChecks.Where(x => x.PersonId == _kidA));
        Assert.Equal(new DateOnly(2027, 1, 3), AttendanceService.SundayOf(new DateOnly(2027, 1, 9)));
    }

    [Fact]
    public async Task R235_담임은_자기_반만_관리자는_모든_반_미배정반은_기록하지_않는다()
    {
        var t = Att(_kids, admin: false);
        var ts = await t.ScopeAsync("jeong");
        Assert.Equal([_c1], ts.Classes.ToArray());
        var board = await t.BoardAsync(ts, null);
        Assert.Equal(_kidA, Assert.Single(Assert.Single(board.Groups).Kids).PersonId);
        Assert.True(board.Groups[0].Mine);
        Assert.Equal(AttendError.None, await t.SetAsync(ts, "jeong", _kidA, Sun, true));
        Assert.Equal(AttendError.Forbidden, await t.SetAsync(ts, "jeong", _kidB, Sun, true));     // 다른 반

        var a = Att(_kids, admin: true);
        var asc = await a.ScopeAsync("eunhye");
        Assert.Equal(2, (await a.BoardAsync(asc, null)).Groups.Count);
        Assert.Equal(AttendError.None, await a.SetAsync(asc, "eunhye", _kidB, Sun, true));
        Assert.Equal(AttendError.Forbidden, await a.SetAsync(asc, "eunhye", _loose, Sun, true));  // 미배정반
        Assert.Equal(AttendError.NotFound, await Att(_elem, admin: true).SetAsync(asc, "minsu", _kidA, Sun, true));   // 다른 부서
    }

    [Fact]
    public async Task R234_지난_4주_점과_3주_연속_결석()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = _kidA, Sunday = Sun.AddDays(-28) });
            raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = _kidB, Sunday = Sun.AddDays(-7) });
            raw.SaveChanges();
        }
        var a = Att(_kids, admin: true);
        var kids = (await a.BoardAsync(await a.ScopeAsync("eunhye"), null)).Groups.SelectMany(g => g.Kids).ToDictionary(k => k.PersonId);
        Assert.Equal([true, false, false, false], kids[_kidA].Recent);
        Assert.Equal(3, kids[_kidA].AbsentStreak);
        Assert.Equal(0, kids[_kidB].AbsentStreak);
    }

    [Fact]
    public async Task R236_새친구를_등록하면_학생으로_올라가고_첫_방문_출석이_체크된다()
    {
        var a = Att(_kids, admin: true);
        Assert.Equal(AttendError.BadDate, (await a.RegisterAsync("eunhye", new NewcomerInput("오다은", new DateOnly(2027, 1, 6), null, null, null, null, null, true, null))).Error);
        var (e, pid) = await a.RegisterAsync("eunhye", new NewcomerInput("오다은", new DateOnly(2027, 1, 3), 5, 2, null, "010-2222-3333", null, false, "김철수"));
        Assert.Equal(AttendError.None, e);
        var (e2, pid2) = await a.RegisterAsync("eunhye", new NewcomerInput("한겨울", new DateOnly(2027, 1, 3), null, null, null, null, _c1, true, "무시"));
        Assert.Equal(AttendError.None, e2);

        using var raw = InMemoryDb.Raw(_db);
        var p = raw.People.Single(x => x.Id == pid);
        Assert.Equal((UserRole.StudentCode, (bool?)false, "김철수", "010-2222-3333"), (p.RoleCode, p.NewcomerVoluntary, p.NewcomerEvangelist, p.Phone2));
        Assert.Null(raw.People.Single(x => x.Id == pid2).NewcomerEvangelist);                           // 스스로 왔으면 전도자 없음
        Assert.Equal(Sun, raw.AttendanceChecks.Single(x => x.PersonId == pid).Sunday);
        Assert.Equal(_c1, raw.ClassMembers.Single(x => x.PersonId == pid2).ClassId);
        Assert.Contains(await Classes(_kids).UnassignedAsync(), u => u.PersonId == pid);                // 반을 안 고르면 미배정반
    }

    [Fact]
    public async Task R237_새친구는_새친구_화면에서_체크하고_28일과_4번이_지나면_빠진다()
    {
        var a = Att(_kids, admin: true);
        var (_, pid) = await a.RegisterAsync("eunhye", new NewcomerInput("오다은", new DateOnly(2026, 12, 6), null, null, null, null, null, true, null));
        // 새친구반 담당(임원) — 반 출석부는 없고 새친구만 체크한다
        var officerAcc = InMemoryDb.SeedAccount(_db, "boo@example.com", "최부장");
        InMemoryDb.SeedMembership(_db, _kids.Id, officerAcc.UserName!, MembershipStatus.Approved, Roles.Teacher);
        using (var raw = InMemoryDb.Raw(_db)) { raw.DeptRoles.Single(d => d.UserId == officerAcc.Id).RoleCode = "T1005"; raw.SaveChanges(); }
        var nc = (await Classes(_kids).ClassesAsync()).Single(c => c.IsNewcomer).Id;
        var officerPid = (await People(_kids).RosterAsync(null, null)).Single(r => r.UserName == "boo").PersonId;
        Assert.Equal(ClassError.None, await Classes(_kids).AddAsync(nc, [officerPid], teacher: true));
        var o = Att(_kids, admin: false);
        var os = await o.ScopeAsync("boo");
        Assert.True(os.Newcomers && os.Classes.Count == 0);
        Assert.Equal(AttendError.None, await o.SetAsync(os, "boo", pid, Sun.AddDays(-14), true));
        Assert.Equal(AttendError.Forbidden, await o.SetAsync(os, "boo", _kidA, Sun, true));

        var row = Assert.Single(await a.NewcomersAsync(Sun));
        Assert.Equal((2, true, ClassService.UnassignedName), (row.Visits, row.PastDays, row.ClassName));   // 28일 지났어도 2번이라 계속
        await o.SetAsync(os, "boo", pid, Sun.AddDays(-7), true);
        await o.SetAsync(os, "boo", pid, Sun, true);
        Assert.Empty(await a.NewcomersAsync(Sun));                                                    // 4번 · 28일 지남 → 끝
        Assert.False(AttendanceService.IsCareOver(new DateOnly(2027, 1, 1), 5, new DateOnly(2027, 1, 5)));   // 28일 전이면 계속
    }

    [Fact]
    public async Task R240_새친구_홈은_관리_중을_먼저_완료를_뒤에_완료_학생도_기록이_남는다()
    {
        var a = Att(_kids, admin: true);
        var (_, done) = await a.RegisterAsync("eunhye", new NewcomerInput("박서윤", new DateOnly(2026, 11, 1), null, null, null, null, null, true, null));
        var (_, on) = await a.RegisterAsync("eunhye", new NewcomerInput("오다은", new DateOnly(2026, 12, 20), null, null, null, null, null, true, null));
        var s = await a.ScopeAsync("eunhye");
        foreach (var d in new[] { 8, 22 }) await a.SetAsync(s, "eunhye", done, new DateOnly(2026, 11, d), true);
        await a.SetAsync(s, "eunhye", done, new DateOnly(2026, 12, 6), true);   // 11/1 · 11/8 · 11/22 · 12/6 = 4번, 28일 지남

        var h = await a.NewcomerHomeAsync();
        Assert.Equal(on, Assert.Single(h.Active).PersonId);
        var d0 = Assert.Single(h.Done);
        Assert.Equal((done, 4, new DateOnly(2026, 12, 6)), (d0.PersonId, d0.Visits, d0.DoneOn));
        Assert.Equal(6, d0.Weeks.Count);                                   // 11/1 ~ 12/6 주일 6번
        Assert.Equal([true, true, false, true, false, true], d0.Weeks.Select(w => w.Present));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(4, raw.AttendanceChecks.Count(x => x.PersonId == done));   // 지우지 않는다
        Assert.Equal(new DateOnly(2026, 12, 2), AttendanceService.CareOverOn(new DateOnly(2026, 11, 3), [new(2026, 11, 8), new(2026, 11, 15), new(2026, 11, 22), new(2026, 11, 29)]));
    }

    [Fact]
    public async Task R241_기존_출석_변경은_달의_실제_주일_완료_새친구도_한_명씩_바꾼다()
    {
        var a = Att(_kids, admin: true);
        var (_, done) = await a.RegisterAsync("eunhye", new NewcomerInput("박서윤", new DateOnly(2026, 11, 1), null, null, null, null, null, true, null));
        var sundays = await a.NewcomerSundaysAsync(2026, 11);
        Assert.Equal([1, 8, 15, 22, 29], sundays.Select(x => x.Sunday.Day));     // 5번 있는 달
        Assert.Equal(4, (await a.NewcomerSundaysAsync(2026, 12)).Count);
        Assert.Equal((1, 1), (sundays[0].Newcomers, sundays[0].Present));
        Assert.All(sundays, x => Assert.False(x.Future));                         // 오늘(2027-01-05) 앞

        // 새친구반 담당(임원): 관리가 끝난 새친구도 지난 주일을 고친다, 첫 방문 전 주일은 못 고친다
        var off = InMemoryDb.SeedAccount(_db, "boo@example.com", "최부장");
        InMemoryDb.SeedMembership(_db, _kids.Id, off.UserName!, MembershipStatus.Approved, Roles.Teacher);
        using (var raw = InMemoryDb.Raw(_db)) { raw.DeptRoles.Single(d => d.UserId == off.Id).RoleCode = "T1005"; raw.SaveChanges(); }
        var nc = (await Classes(_kids).ClassesAsync()).Single(c => c.IsNewcomer).Id;
        await Classes(_kids).AddAsync(nc, [(await People(_kids).RosterAsync(null, null)).Single(r => r.UserName == "boo").PersonId], teacher: true);
        var o = Att(_kids, admin: false);
        var os = await o.ScopeAsync("boo");
        foreach (var d in new[] { 8, 15, 22 }) Assert.Equal(AttendError.None, await o.SetAsync(os, "boo", done, new DateOnly(2026, 11, d), true));
        Assert.Empty((await o.NewcomerHomeAsync()).Active);                       // 이제 완료
        Assert.Equal(AttendError.None, await o.SetAsync(os, "boo", done, new DateOnly(2026, 11, 15), false));   // 완료 학생도 고친다
        Assert.Equal(AttendError.Forbidden, await o.SetAsync(os, "boo", done, new DateOnly(2026, 10, 25), true));
        var on15 = Assert.Single(await o.NewcomersOnAsync(new DateOnly(2026, 11, 15)));
        Assert.False(on15.Present);
        Assert.Empty(await o.NewcomersOnAsync(new DateOnly(2026, 10, 25)));        // 첫 방문 전 주일에는 안 나온다
    }

    [Fact]
    public async Task R242_돌봄은_기록_없는_학생_먼저_그다음_오래_빠진_순_기준일_안에_온_아이는_빠진다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = _kidA, Sunday = Sun });
            raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = _kidB, Sunday = new DateOnly(2026, 12, 6) });   // 30일 전
            raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = _teacherPid, Sunday = new DateOnly(2026, 6, 7) });
            raw.SaveChanges();
        }
        var a = Att(_kids, admin: true);
        var c = await a.CareAsync(21);
        Assert.Equal(1, c.WeeksCount);                                           // 2027년 첫 주일 1월 3일 하나
        Assert.Equal([_loose, _kidB], c.Rows.Select(r => r.PersonId));         // 교사 · 이번 주 온 아이는 없다
        Assert.Null(c.Rows[0].LastAttend);
        Assert.Equal((30, "1학년2반", 0), (c.Rows[1].DaysAbsent, c.Rows[1].ClassName, c.Rows[1].AttendCount));
        Assert.Equal(ClassService.UnassignedName, c.Rows[0].ClassName);
        Assert.Equal([_loose], (await a.CareAsync(42)).Rows.Select(r => r.PersonId));
        Assert.Equal(2, await a.CareCountAsync());
    }

    [Fact]
    public async Task R243_주일_보고서는_반별_출석_올해_출석수_미배정반은_끝에_다음_주일은_못_본다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = _kidA, Sunday = Sun });
            raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = _kidA, Sunday = Sun.AddDays(-7) });   // 작년
            raw.SaveChanges();
        }
        var a = Att(_kids, admin: true);
        var r = await a.SundayReportAsync(Sun.AddDays(14));                       // 앞으로는 못 간다
        Assert.Equal(Sun, r.Sunday);
        Assert.Equal(1, r.WeeksCount);
        Assert.Equal(["1학년1반", "1학년2반", ClassService.UnassignedName], r.Classes.Select(c => c.Name));
        var kidA = Assert.Single(r.Classes[0].Kids);
        Assert.Equal((true, 1), (kidA.Present, kidA.YearCount));
        Assert.False(Assert.Single(r.Classes[1].Kids).Present);
        Assert.Equal(_loose, Assert.Single(r.Classes[2].Kids).PersonId);
        Assert.Equal(("1학년", "1반"), (r.Classes[0].Group, r.Classes[0].Short));
        Assert.Equal(("반", "르우벤"), AttendanceService.SplitClassName("르우벤"));
        Assert.True(Assert.Single((await a.SundayReportAsync(Sun.AddDays(-7))).Classes[0].Kids).Present);
    }

    [Fact]
    public async Task R245_출석_등수는_올해_출석_수로_공동_등수를_매긴다()
    {
        var kidC = Person("가나다", "T1001");
        await Classes(_kids).AddAsync(_c2, [kidC], teacher: false);
        using (var raw = InMemoryDb.Raw(_db))
        {
            foreach (var p in new[] { _kidA, kidC }) raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = p, Sunday = Sun });
            raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = _kidB, Sunday = Sun.AddDays(-7) });   // 작년은 안 센다
            raw.SaveChanges();
        }
        var rows = ChallengeBible.Web.Pages.Attendance.RankModel.Ranked(await Att(_kids, admin: true).SundayReportAsync(Sun));
        Assert.Equal([("가나다", 1), ("김철수", 1), ("박하린", 2), ("정하늘", 2)], rows.Select(r => (r.Name, r.Rank)));
    }

    [Fact]
    public async Task R248_월별_통계는_그_달_주일의_출석을_주별_반별로_센다()
    {
        var kidC = Person("가나다", "T1001");
        await Classes(_kids).AddAsync(_c2, [kidC], teacher: false);
        using (var raw = InMemoryDb.Raw(_db))
        {
            foreach (var p in new[] { _kidA, _kidB, kidC }) raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = p, Sunday = Sun });
            raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = _kidA, Sunday = Sun.AddDays(-7) });   // 지난달은 안 센다
            raw.SaveChanges();
        }
        var s = await Att(_kids, admin: true).MonthStatsAsync(2027, 1);
        Assert.Equal([3, 10, 17, 24, 31], s.Weeks.Select(w => w.Sunday.Day));     // 2027년 1월 주일 5번
        Assert.Equal([3, 0, 0, 0, 0], s.Weeks.Select(w => w.Count));
        Assert.Equal([false, true, true, true, true], s.Weeks.Select(w => w.Future));   // 오늘(1/5) 뒤 주일은 아직 안 옴
        Assert.Equal(3, s.Total);
        Assert.Equal(0.6, s.Average);
        Assert.Equal(3, s.Classes.Sum(c => c.Total));
        Assert.Equal(100, Math.Round(s.Classes.Sum(c => c.Ratio)));
        Assert.True(s.Classes.SequenceEqual(s.Classes.OrderByDescending(c => c.Total)));   // 많은 반이 위
        Assert.Equal(1, (await Att(_kids, admin: true).MonthStatsAsync(2026, 12)).Weeks.Single(w => w.Sunday == Sun.AddDays(-7)).Count);
    }

    [Fact]
    public async Task R249_새친구_상세는_주일별_출석을_보이고_지우기는_새친구만_계정_연결은_못_지운다()
    {
        var a = Att(_kids, admin: true);
        var (e, pid) = await a.RegisterAsync("admin", new NewcomerInput("오다은", Sun, null, null, null, null, _c1, false, "철수"));
        Assert.Equal(AttendError.None, e);
        var d = (await a.NewcomerDetailAsync(pid))!;
        Assert.Equal(("오다은", "1학년1반", false, "철수", 1, false), (d.Name, d.ClassName, d.Voluntary, d.Evangelist, d.Visits, d.Done));
        Assert.Equal([(Sun, true)], d.Weeks.Select(w => (w.Sunday, w.Present)));       // 첫 방문 주일부터 이번 주일까지
        Assert.Null(await a.NewcomerDetailAsync(_kidA));                                // 새친구가 아닌 학생은 상세가 없다
        Assert.Equal(AttendError.NotFound, await a.DeleteNewcomerAsync(_kidA));         // 새친구가 아니면 지우지 않는다
        Assert.Equal(AttendError.None, await a.DeleteNewcomerAsync(pid));
        using (var raw = InMemoryDb.Raw(_db))
        {
            Assert.False(raw.People.Any(p => p.Id == pid));
            Assert.False(raw.AttendanceChecks.Any(c => c.PersonId == pid));
            Assert.False(raw.ClassMembers.Any(m => m.PersonId == pid));
        }
        Assert.Equal(AttendError.NotFound, await a.DeleteNewcomerAsync(pid));
        var (_, linked) = await a.RegisterAsync("admin", new NewcomerInput("연결됨", Sun, null, null, null, null, null, true, null));
        var acc = InMemoryDb.SeedAccount(_db, "linked@example.com", "연결됨");
        using (var raw = InMemoryDb.Raw(_db)) { raw.People.Single(p => p.Id == linked).UserId = acc.Id; raw.SaveChanges(); }
        Assert.Equal(AttendError.Linked, await a.DeleteNewcomerAsync(linked));
    }

    [Fact]
    public async Task R251_새친구_정보_고치기는_이름_생일_연락처_데려온_사람만_바꾸고_출석은_그대로다()
    {
        var a = Att(_kids, admin: true);
        var (_, pid) = await a.RegisterAsync("admin", new NewcomerInput("오다은", Sun, null, null, null, null, _c1, true, null));
        Assert.Equal(AttendError.None, await a.UpdateNewcomerAsync(pid, new NewcomerEdit(" 오다인 ", 3, 9, "010-1234-5678", null, false, "김철수")));
        var e = (await a.NewcomerEditAsync(pid))!;
        Assert.Equal(("오다인", 3, 9, "010-1234-5678", false, "김철수"), (e.Name, e.BirthMonth, e.BirthDay, e.Phone1, e.Voluntary, e.Evangelist));
        Assert.Equal(AttendError.None, await a.UpdateNewcomerAsync(pid, new NewcomerEdit("오다인", null, null, null, null, true, "무시됨")));   // 스스로 오면 데려온 사람은 비운다
        Assert.Null((await a.NewcomerEditAsync(pid))!.Evangelist);
        Assert.Equal(AttendError.NoName, await a.UpdateNewcomerAsync(pid, new NewcomerEdit(" ", null, null, null, null, true, null)));
        Assert.Equal(AttendError.BadBirthday, await a.UpdateNewcomerAsync(pid, new NewcomerEdit("오다인", 3, null, null, null, true, null)));
        Assert.Equal(AttendError.BadPhone, await a.UpdateNewcomerAsync(pid, new NewcomerEdit("오다인", null, null, "abc", null, true, null)));
        Assert.Equal(AttendError.NotFound, await a.UpdateNewcomerAsync(_kidA, new NewcomerEdit("바꿈", null, null, null, null, true, null)));   // 새친구가 아니면 못 고친다
        var d = (await a.NewcomerDetailAsync(pid))!;
        Assert.Equal(("오다인", "1학년1반", Sun, 1), (d.Name, d.ClassName, d.FirstVisit, d.Visits));   // 첫 방문일 · 반 · 출석은 그대로
    }

    [Fact]
    public async Task R246_주별_출석표는_기준_주일까지_주일_칸마다_출석을_표시한다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = _kidA, Sunday = Sun.AddDays(-7) });
            raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = _kidA, Sunday = Sun.AddDays(-70) });   // 4주 밖
            raw.SaveChanges();
        }
        var a = Att(_kids, admin: true);
        var m = await a.MatrixAsync(Sun, null);                                    // 올해 1주 → 최소 4주
        Assert.Equal([Sun.AddDays(-21), Sun.AddDays(-14), Sun.AddDays(-7), Sun], m.Sundays);
        Assert.Equal([(_kidA, Sun.AddDays(-7))], m.Present.ToList());
        Assert.Equal(52, (await a.MatrixAsync(Sun, 99)).Sundays.Count);
    }

    [Fact]
    public async Task R258_주별_출석표는_올해_1월부터_오늘이_든_달까지_앞으로_올_주일은_세지_않는다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = _kidA, Sunday = Sun });               // 이번 주일(2027-01-03)
            raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = _kidA, Sunday = Sun.AddDays(7) });    // 아직 안 온 주일: 센 값이 아니다
            raw.SaveChanges();
        }
        var a = Att(_kids, admin: true);
        var m = await a.MatrixMonthAsync(null);                                    // 오늘 = 2027-01-05
        Assert.Equal([3, 10, 17, 24, 31], m.Sundays.Select(s => s.Day));          // 올해 첫 주일 ~ 1월 마지막 주일
        Assert.Equal(Sun, m.Rep.ThisSunday);
        Assert.Equal([(_kidA, Sun)], m.Present.ToList());                          // 이번 주일 뒤는 담지 않는다
        var later = await a.MatrixMonthAsync(new DateOnly(2028, 1, 1));            // 오늘보다 뒤 기준일은 오늘로
        Assert.Equal(2027, later.Sundays[0].Year);
    }

    [Fact]
    public async Task R230_진급하면_출석_기록도_새_부서로_옮겨진다()
    {
        var acc = InMemoryDb.SeedAccount(_db, "cheolsu@example.com", "김철수");
        InMemoryDb.SeedMembership(_db, _kids.Id, acc.UserName!, MembershipStatus.Approved, Roles.Participant);
        var pid = (await People(_kids).RosterAsync(null, null)).Single(r => r.UserName == "cheolsu").PersonId;
        int req;
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = pid, Sunday = Sun });
            var r = new PromotionRequest { UserName = "cheolsu", SourceTenantId = _kids.Id, TargetTenantId = _elem.Id, RequestedByUserName = "t" };
            raw.PromotionRequests.Add(r);
            raw.SaveChanges();
            req = r.Id;
        }
        var (d, c) = In(_elem, admin: true);
        Assert.Equal(PromotionError.None, await new PromotionApprover(d, new InMemorySystemDb(_db), c, _time).ApproveAsync("minsu", req, "T1001"));
        using var after = InMemoryDb.Raw(_db);
        var moved = Assert.Single(after.AttendanceChecks.Where(x => x.Sunday == Sun && x.PersonId != _kidA));
        Assert.Equal(_elem.Id, moved.TenantId);
        Assert.Equal(acc.Id, after.People.Single(p => p.Id == moved.PersonId && p.TenantId == _elem.Id).UserId);
    }
}

/// <summary>출석 화면: 교사(반 없음)는 안내, 학생은 못 들어온다, 저장은 JSON.</summary>
public partial class AttendanceFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();

    public AttendanceFlowTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));
        InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Member(string login, string role)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var tok = TokenRx().Match(await c.GetStringAsync("/account/signup")).Groups[1].Value;
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tok, ["DisplayName"] = login, ["Email"] = login + "@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        string userName;
        int tenantId;
        using (var raw = InMemoryDb.Raw(_db)) { userName = raw.Users.Single(a => a.Email == login + "@example.com").UserName!; tenantId = raw.Tenants.Single().Id; }
        InMemoryDb.SeedMembership(_db, tenantId, userName, MembershipStatus.Approved, role);
        return c;
    }

    [Fact]
    public async Task 관리자는_새친구를_등록하고_출석부에서_저장하고_학생은_들어오지_못한다()
    {
        var a = await Member("admin", Roles.TenantAdmin);
        Assert.Contains("href=\"/jamsil-kids/attendance\"", await a.GetStringAsync("/jamsil-kids"));
        var form = await a.GetStringAsync("/jamsil-kids/attendance/newcomers/new");
        var r = await a.PostAsync("/jamsil-kids/attendance/newcomers/new", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(form).Groups[1].Value, ["Name"] = "오다은", ["FirstVisit"] = DateTime.Today.ToString("yyyy-MM-dd"), ["Voluntary"] = "true",
        }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var home = await a.GetStringAsync("/jamsil-kids/attendance/newcomers");   // K1 새친구 홈: 메뉴 · 관리 중 목록, 주일 이동 없음
        Assert.Contains("오다은", home);
        Assert.Contains("href=\"/jamsil-kids/attendance/newcomers/today\"", home);
        Assert.Contains("href=\"/jamsil-kids/attendance/newcomers/past\"", home);
        Assert.DoesNotContain("aria-label=\"지난 주일\"", home);
        var list = await a.GetStringAsync("/jamsil-kids/attendance/newcomers/today");   // K2
        Assert.Contains("오다은", list);
        Assert.Contains("기존 출석 변경", await a.GetStringAsync("/jamsil-kids/attendance/newcomers/past"));
        int pid;
        using (var raw = InMemoryDb.Raw(_db)) pid = raw.People.Single(p => p.Name == "오다은").Id;
        Assert.Contains($"href=\"/jamsil-kids/attendance/newcomers/detail?id={pid}\"", home);   // R-249 새친구 상세 · 지우기
        var det = await a.GetStringAsync($"/jamsil-kids/attendance/newcomers/detail?id={pid}");
        Assert.Contains("4주 관리 중", det);
        Assert.Contains("신상정보 수정", det);                                  // 정보 고치기 → 신상정보 수정
        Assert.DoesNotContain("잘못 등록했어요", det);                           // 삭제 버튼은 신상정보 수정 화면으로 옮겼다
        Assert.DoesNotContain("handler=Delete", det);
        // R-256 주일별 출석을 눌러서 고친다: 주일마다 한 줄(스위치), 저장은 출석부와 같은 주소
        Assert.Contains("class=\"wrow", det);
        Assert.Contains("id=\"board\"", det);
        var wkSunday = Regex.Match(det, "class=\"wrow[^\"]*\" data-id=\"\\d+\" data-name=\"[^\"]*\" data-sunday=\"([0-9-]+)\"").Groups[1].Value;
        Assert.NotEmpty(wkSunday);
        async Task SetWeek(bool on)
        {
            var rq = new HttpRequestMessage(HttpMethod.Post, "/jamsil-kids/attendance?handler=Set")
            {
                Content = new StringContent($"{{\"personId\":{pid},\"sunday\":\"{wkSunday}\",\"present\":{(on ? "true" : "false")}}}", System.Text.Encoding.UTF8, "application/json"),
            };
            rq.Headers.Add("RequestVerificationToken", TokenRx().Match(det).Groups[1].Value);
            Assert.Equal(HttpStatusCode.OK, (await a.SendAsync(rq)).StatusCode);
        }
        await SetWeek(true);
        Assert.Contains("<span id=\"ncN\">1</span>", await a.GetStringAsync($"/jamsil-kids/attendance/newcomers/detail?id={pid}"));
        await SetWeek(false);
        Assert.Contains("<span id=\"ncN\">0</span>", await a.GetStringAsync($"/jamsil-kids/attendance/newcomers/detail?id={pid}"));
        Assert.Contains($"href=\"/jamsil-kids/attendance/newcomers/edit?id={pid}\"", det);   // R-251 정보 고치기
        var editForm = await a.GetStringAsync($"/jamsil-kids/attendance/newcomers/edit?id={pid}");
        Assert.Contains("value=\"오다은\"", editForm);
        var saved = await a.PostAsync($"/jamsil-kids/attendance/newcomers/edit?id={pid}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(editForm).Groups[1].Value, ["Name"] = "오다인", ["Voluntary"] = "true",
        }));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Contains("오다인", await a.GetStringAsync($"/jamsil-kids/attendance/newcomers/detail?id={pid}&Ok=edit"));
        using (var raw = InMemoryDb.Raw(_db)) { var x = raw.People.Single(p => p.Id == pid); Assert.Equal("오다인", x.Name); x.Name = "오다은"; raw.SaveChanges(); }   // 아래 흐름은 옛 이름으로 이어 간다
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync("/jamsil-kids/attendance/newcomers/detail?id=999999")).StatusCode);
        var editForm2 = await a.GetStringAsync($"/jamsil-kids/attendance/newcomers/edit?id={pid}");
        Assert.Matches(">저장</button>\\s*<button[^>]*formaction=[^>]*>삭제</button>\\s*<a[^>]*>취소</a>", editForm2);   // 버튼 순서: 저장 · 삭제 · 취소(삭제는 부서 관리자만)
        var del = await a.PostAsync($"/jamsil-kids/attendance/newcomers/edit?handler=Delete&id={pid}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(editForm2).Groups[1].Value,
        }));
        Assert.Equal(HttpStatusCode.Redirect, del.StatusCode);
        using (var raw = InMemoryDb.Raw(_db)) { Assert.False(raw.People.Any(p => p.Id == pid)); Assert.False(raw.AttendanceChecks.Any(c => c.PersonId == pid)); }
        var again = await a.PostAsync("/jamsil-kids/attendance/newcomers/new", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(form).Groups[1].Value, ["Name"] = "오다은", ["FirstVisit"] = DateTime.Today.ToString("yyyy-MM-dd"), ["Voluntary"] = "true",
        }));
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        using (var raw = InMemoryDb.Raw(_db)) pid = raw.People.Single(p => p.Name == "오다은").Id;
        var sunday = Regex.Match(list, "data-sunday=\"([0-9-]+)\"").Groups[1].Value;
        var req = new HttpRequestMessage(HttpMethod.Post, "/jamsil-kids/attendance?handler=Set")
        {
            Content = new StringContent($"{{\"personId\":{pid},\"sunday\":\"{sunday}\",\"present\":false}}", System.Text.Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("RequestVerificationToken", TokenRx().Match(list).Groups[1].Value);
        var set = await a.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        var kid = await Member("kid", Roles.Participant);
        var denied = await kid.GetAsync("/jamsil-kids/attendance");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("/account/denied", denied.Headers.Location!.PathAndQuery);

        var t = await Member("teacher", Roles.Teacher);
        Assert.Contains("아직 맡은 반이 없어요", await t.GetStringAsync("/jamsil-kids/attendance"));

        // 더보기(유년부 "더보기"와 같은 모양): 관리자에게만 관리자 메뉴, 맡은 반 없는 교사는 못 들어온다
        var more = await a.GetStringAsync("/jamsil-kids/attendance/more");
        Assert.Contains("교사 · 반 배정", more);
        Assert.Contains("href=\"/jamsil-kids/attendance/more\"", await a.GetStringAsync("/jamsil-kids/attendance"));
        Assert.Equal(HttpStatusCode.Redirect, (await t.GetAsync("/jamsil-kids/attendance/more")).StatusCode);

        // 돌봄(유년부 "돌봄"과 같은 모양): 관리자만, 탭에 돌봄, 기준 고르기
        var care = await a.GetStringAsync("/jamsil-kids/attendance/care?absentDays=42");
        Assert.Contains("연락할 아이", care);
        Assert.Contains("6주 이상 결석", care);
        Assert.Contains("href=\"/jamsil-kids/attendance/care\"", await a.GetStringAsync("/jamsil-kids/attendance"));
        Assert.DoesNotContain("attendance/care", await t.GetStringAsync("/jamsil-kids/attendance"));
        Assert.Equal(HttpStatusCode.Redirect, (await t.GetAsync("/jamsil-kids/attendance/care")).StatusCode);

        // 주일 출결 보고서(PC) · 주일 출석 명단(휴대폰): 관리자만, 더보기에 있다
        Assert.Contains("href=\"/jamsil-kids/attendance/report?sunday=", more);   // 유년부 더보기와 같은 항목 · 순서
        Assert.True(more.IndexOf("주일 출석 보고서") < more.IndexOf("최근 전체 출석현황(4주)") && more.IndexOf("아이별 주별 출석표(52주)") < more.IndexOf("교사 · 반 배정"));
        Assert.DoesNotContain("attendance/dashboard/all", more);
        Assert.DoesNotContain("attendance/stats", more);
        var rep = await a.GetStringAsync("/jamsil-kids/attendance/report");
        Assert.Contains("주일 출결 보고서", rep);
        // 제목과 종이만 남긴다(사용자 확정 2026-11-13): 위 이동 줄 · 날짜 · 숫자 · 버튼 · 아래 탭이 없다
        Assert.Contains("class=\"paper\"", rep);
        Assert.DoesNotContain("attendance/trend?date=", rep);
        Assert.DoesNotContain("명단 (모바일)", rep);
        Assert.DoesNotContain("인쇄하기", rep);
        Assert.DoesNotContain("class=\"attTabs\"", rep);
        Assert.DoesNotContain("aria-label=\"주일 고르기\"", rep);
        Assert.Contains("새친구 1~4회째", rep);
        Assert.Contains("name=\"viewport\" content=\"width=1120\"", rep);   // 데스크탑 전용: 휴대폰에서도 PC 모양 그대로 줄여 보인다
        Assert.Contains("width=device-width", await a.GetStringAsync("/jamsil-kids/attendance/sunday"));
        Assert.Contains("출석 명단", await a.GetStringAsync("/jamsil-kids/attendance/sunday?sunday=2020-01-05"));
        Assert.Equal(HttpStatusCode.Redirect, (await t.GetAsync("/jamsil-kids/attendance/report")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await t.GetAsync("/jamsil-kids/attendance/sunday")).StatusCode);

        // 최근 전체 출석현황(3주) · 주일 반별 출석
        Assert.Contains("href=\"/jamsil-kids/attendance/trend?date=", more);
        var trend = await a.GetStringAsync("/jamsil-kids/attendance/trend");
        Assert.Equal(4, Regex.Matches(trend, "<article class=\"w o").Count);          // 주마다 카드 하나(확정 시안 2026-11-13)
        Assert.Equal(4, Regex.Matches(trend, "class=\"main\" href=\"/jamsil-kids/attendance/sunday\\?sunday=").Count);   // R-269 카드를 누르면 그 주일 반별 명단
        Assert.Equal(4, Regex.Matches(trend, ">주일 출결 보고서 \\(PC\\)</a>").Count);
        Assert.DoesNotContain("명단 보기", trend);                                      // 큰 버튼 3개는 없앴다
        Assert.DoesNotContain("attendance/byclass", trend);
        var oldByClass = await a.GetAsync("/jamsil-kids/attendance/byclass?sunday=2020-01-05");   // 반별 출석은 명단과 합쳤다: 예전 주소는 명단으로
        Assert.Equal(HttpStatusCode.Redirect, oldByClass.StatusCode);
        Assert.Equal("/jamsil-kids/attendance/sunday?sunday=2020-01-05", oldByClass.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, (await t.GetAsync("/jamsil-kids/attendance/trend")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await t.GetAsync("/jamsil-kids/attendance/byclass")).StatusCode);

        // 아이별 출석 등수
        Assert.Contains("href=\"/jamsil-kids/attendance/rank\"", more);
        Assert.Contains("전체 등수", await a.GetStringAsync("/jamsil-kids/attendance/rank"));
        Assert.Equal(HttpStatusCode.Redirect, (await t.GetAsync("/jamsil-kids/attendance/rank")).StatusCode);

        // 아이별 주별 출석표
        Assert.Contains("href=\"/jamsil-kids/attendance/matrix\"", more);
        var mx = await a.GetStringAsync("/jamsil-kids/attendance/matrix");
        Assert.Contains("name=\"viewport\" content=\"width=1120\"", mx);                // 데스크탑 전용
        Assert.Contains("class=\"dk-chart\"", mx);                                       // 위에 막대 그래프
        Assert.InRange(Regex.Matches(mx, "<th class=\"dh ").Count, 4, 53);              // 올해 1월 ~ 이번 달 주일 칸이 한 화면에
        Assert.DoesNotContain("최근 4주", mx);                                           // 4 · 13 · 26 · 52주 고르기는 없다
        Assert.Contains("연속 결석(3주 이상)", mx);                                       // 연속 결석 기준이 눈에 보인다(진한 빨강 칸, 확정 시안 C)
        Assert.Equal(HttpStatusCode.Redirect, (await t.GetAsync("/jamsil-kids/attendance/matrix")).StatusCode);

        // 출석 대시보드(반 하나 / 전체 반)
        var today = await a.GetStringAsync("/jamsil-kids/attendance");
        Assert.Contains("attendance/dashboard/all", today);   // 유년부처럼 출석 체크 화면에서 들어간다
        Assert.Contains("class=\"tdTile\" href=\"/jamsil-kids/attendance/report?sunday=", today);   // 유년부처럼 보고서 띠(관리자만)
        Assert.Contains("미출석/미체크 아이", today);
        // 주일 이동은 전주 · 보는 주 · 차주 3칸(확정 시안 2026-11-13): 이번 주일에서는 차주를 누를 수 없고, 지난 주일에서는 차주가 링크
        Assert.Contains("class=\"nvB\"", today);
        Assert.Contains("<small>전주</small>", today);
        Assert.Contains("<small>이번 주일</small>", today);
        Assert.Contains("<small>차주 · 아직</small>", today);
        Assert.DoesNotContain("aria-label=\"다음 주일\"", today);
        var pastDay = await a.GetStringAsync("/jamsil-kids/attendance?d=2020-01-05");
        Assert.Contains("<small>보는 주</small>", pastDay);
        Assert.Contains("지난 주일 · 고치는 중", pastDay);
        Assert.Contains("href=\"?D=2020-01-12\"", pastDay);                              // 차주 칸은 누르면 그 주일로
        Assert.Contains("href=\"?D=2019-12-29\"", pastDay);                              // 전주 칸
        Assert.Contains("id=\"qClear\"", today);
        Assert.Contains("class=\"tdTip\"", today);
        Assert.Contains("class=\"att gut attw\"", today);                                  // R-270 데스크톱은 유년부처럼 넓게(760px)
        Assert.Contains("id=\"qGo\"", today);
        Assert.DoesNotContain("tdTiles", await t.GetStringAsync("/jamsil-kids/attendance"));
        var dashAll = await a.GetStringAsync("/jamsil-kids/attendance/dashboard/all");
        Assert.Contains("전체반 출석현황", dashAll);
        Assert.Contains("class=\"dbTop flow\"", dashAll);                                  // 위 덩어리는 화면에 고정하지 않는다(사용자 2026-11-13)
        Assert.Contains("class=\"daSum\"", dashAll);                                       // R-261 시안 B: 파란 요약 카드 · 정렬 3칸 · 반 한 줄 목록
        Assert.Contains("미체크 먼저", dashAll);
        Assert.Contains("차주 · 아직", dashAll);                                            // R-262 오늘 화면과 같은 주일 이동 3칸
        Assert.Contains("href=\"/jamsil-kids/attendance/trend?date=", dashAll);
        Assert.Contains("반을 선택해 주세요", await a.GetStringAsync("/jamsil-kids/attendance/dashboard"));
        Assert.Equal(HttpStatusCode.Redirect, (await t.GetAsync("/jamsil-kids/attendance/dashboard/all")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await t.GetAsync("/jamsil-kids/attendance/dashboard")).StatusCode);

        // 월별 출석 통계 · 상세(R-248): 월 카드 12개, 상세는 그 달 주일마다 점 하나
        Assert.Contains("href=\"/jamsil-kids/attendance/stats\"", await a.GetStringAsync("/jamsil-kids/attendance/sunday?sunday=2020-01-05"));   // 반별 출석을 합친 명단 아래 "월별 출석 통계"(R-269)
        var stats = await a.GetStringAsync("/jamsil-kids/attendance/stats?year=2020&month=1");
        Assert.Equal(12, Regex.Matches(stats, "/jamsil-kids/attendance/stats\\?year=2020&(amp;)?month=\\d+\"").Count);
        Assert.Contains("이번 달 출석 요약", stats);
        Assert.Matches("/jamsil-kids/attendance/stats/detail\\?year=2020&(amp;)?month=1\"", stats);
        var detail = await a.GetStringAsync("/jamsil-kids/attendance/stats/detail?year=2020&month=1");
        Assert.Equal(4, Regex.Matches(detail, "<circle ").Count);                  // 2020년 1월 주일 4번
        Assert.Contains("반별 출석 합계", detail);
        Assert.Equal(HttpStatusCode.Redirect, (await t.GetAsync("/jamsil-kids/attendance/stats")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await t.GetAsync("/jamsil-kids/attendance/stats/detail")).StatusCode);

        // 반 하나만 크게 보는 출석 체크(사용자 확정 2026-11-13): [출석 고치기]가 전체 출석부 대신 이 화면을 연다
        int cid;
        using (var raw = InMemoryDb.Raw(_db))
        {
            var tid = raw.Tenants.Single().Id;
            var term = raw.ClassTerms.First(x => x.TenantId == tid);
            var c1 = new SchoolClass { TenantId = tid, TermId = term.Id, Name = "1학년 1반", CreatedUtc = DateTime.UtcNow };
            var c2 = new SchoolClass { TenantId = tid, TermId = term.Id, Name = "1학년 2반", CreatedUtc = DateTime.UtcNow };
            raw.Classes.AddRange(c1, c2);
            var p = new Person { TenantId = tid, Name = "김집중", RoleCode = "T1001", CreatedUtc = DateTime.UtcNow };
            var p2 = new Person { TenantId = tid, Name = "이다음", RoleCode = "T1001", CreatedUtc = DateTime.UtcNow };
            raw.People.AddRange(p, p2);
            raw.SaveChanges();
            raw.ClassMembers.Add(new ClassMember { TenantId = tid, TermId = term.Id, ClassId = c1.Id, PersonId = p.Id, IsTeacher = false, CreatedUtc = DateTime.UtcNow });
            raw.ClassMembers.Add(new ClassMember { TenantId = tid, TermId = term.Id, ClassId = c2.Id, PersonId = p2.Id, IsTeacher = false, CreatedUtc = DateTime.UtcNow });
            raw.SaveChanges();
            cid = c1.Id;
        }
        var one = await a.GetStringAsync($"/jamsil-kids/attendance/class/{cid}");
        Assert.Contains("김집중", one);
        Assert.Contains("1학년 1반", one);
        Assert.Contains("다음 반 ›<b>1학년 2반</b>", one);                      // 이전 · 다음 반으로 차례로 넘긴다
        Assert.Contains("class=\"off\">‹ 이전 반<b>첫 반</b>", one);
        Assert.Contains("id=\"board\"", one);                                  // 저장 JS 가 이 요소를 쓴다
        Assert.DoesNotContain("id=\"q\"", one);                                // 검색창 · 다른 반 카드 없음
        Assert.Contains($"/jamsil-kids/attendance/class/{cid}?d=", await a.GetStringAsync("/jamsil-kids/attendance/sunday"));
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync("/jamsil-kids/attendance/class/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await t.GetAsync($"/jamsil-kids/attendance/class/{cid}")).StatusCode);   // 맡은 반 없는 교사
    }
}
