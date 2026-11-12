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
    }
}
