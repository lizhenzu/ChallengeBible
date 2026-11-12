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

/// <summary>반(ADR 0093 반 관리 및 자동 생성 규칙): R-231 해 없음 · 기본 반 자동, R-232 한 반 · 선생님 자격 · 미배정반, R-233 관리자만.</summary>
public class ClassTests
{
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids, _elem;

    public ClassTests()
    {
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        InMemoryDb.SeedMembership(_db, _kids.Id, InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜").UserName!, MembershipStatus.Approved, Roles.TenantAdmin);
        InMemoryDb.SeedMembership(_db, _elem.Id, InMemoryDb.SeedAccount(_db, "minsu@example.com", "박민수").UserName!, MembershipStatus.Approved, Roles.TenantAdmin);
    }

    private (InMemoryDb Db, TenantContext Ctx) In(Tenant t)
    {
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        var ctx = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db)) ctx.Set(raw.Tenants.Single(x => x.Id == t.Id), null);
        return (new InMemoryDb(_db, scope), ctx);
    }

    private ClassService Classes(Tenant t) { var (d, c) = In(t); return new ClassService(d, new PeopleService(d, c, _time), c, _time); }
    private PeopleService People(Tenant t) { var (d, c) = In(t); return new PeopleService(d, c, _time); }

    private async Task<int> Person(string name, string? role, Tenant? t = null) =>
        (await People(t ?? _kids).AddAsync("eunhye", new PersonInput(name, role, null, null, null, null))).Id;

    [Fact]
    public async Task R231_반_화면을_열면_새친구반이_저절로_생기고_해는_하나뿐이다()
    {
        var nc = Assert.Single(await Classes(_kids).ClassesAsync());
        Assert.Equal((ClassService.NewcomerName, true), (nc.Name, nc.IsNewcomer));
        Assert.Single(await Classes(_kids).ClassesAsync());                       // 두 번 열어도 하나
        Assert.Equal(ClassError.Fixed, await Classes(_kids).DeleteAsync(nc.Id));
        Assert.Equal(ClassError.DupName, (await Classes(_kids).CreateAsync(ClassService.UnassignedName)).Error);
        Assert.Equal(ClassError.None, (await Classes(_kids).CreateAsync("1학년 1반")).Error);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Single(raw.ClassTerms.Where(t => t.TenantId == _kids.Id));
        Assert.Single(raw.Classes.Where(c => c.TenantId == _elem.Id || c.TenantId == _kids.Id), c => c.TenantId == _kids.Id && c.IsNewcomer);
    }

    [Fact]
    public async Task R232_학생은_한_반에만_들고_선생님은_일반_반은_교사만()
    {
        var c1 = (await Classes(_kids).CreateAsync("1학년 1반")).Id;
        var c2 = (await Classes(_kids).CreateAsync("1학년 2반")).Id;
        var kid = await Person("김철수", "T1001");
        var newbie = await Person("한겨울", ClassService.NewcomerCode);
        var teacher = await Person("정교사", "T1002");
        var officer = await Person("최부장", "T1005");
        var parent = await Person("김영희", null);

        Assert.Equal(ClassError.None, await Classes(_kids).AddAsync(c1, [kid, newbie], teacher: false));
        Assert.Equal(ClassError.InOtherClass, await Classes(_kids).AddAsync(c2, [kid], teacher: false));
        Assert.Equal(ClassError.NotEligible, await Classes(_kids).AddAsync(c2, [parent], teacher: false));
        Assert.Equal(ClassError.NotEligible, await Classes(_kids).AddAsync(c1, [officer], teacher: true));
        Assert.Equal(ClassError.None, await Classes(_kids).AddAsync(c1, [teacher], teacher: true));
        Assert.Equal(ClassError.InOtherClass, await Classes(_kids).AddAsync(c2, [teacher], teacher: true));

        var r1 = (await Classes(_kids).ClassesAsync()).Single(r => r.Id == c1);
        Assert.Equal((2, "정교사"), (r1.Students, Assert.Single(r1.Teachers)));
        Assert.Equal(ClassError.NotFound, await Classes(_elem).AddAsync(c1, [kid], teacher: false));   // 다른 부서 반은 보이지 않는다
    }

    [Fact]
    public async Task R232_새친구반_담당은_교사_임원_관리자_새친구는_정규_반에()
    {
        var nc = Assert.Single(await Classes(_kids).ClassesAsync()).Id;
        var officer = await Person("최부장", "T1005");
        var kid = await Person("김철수", "T1001");
        var adminRow = (await People(_kids).RosterAsync(null, null)).Single(r => r.IsAdmin).PersonId;

        Assert.Equal(ClassError.None, await Classes(_kids).AddAsync(nc, [officer, adminRow], teacher: true));
        Assert.Equal(ClassError.NotEligible, await Classes(_kids).AddAsync(nc, [kid], teacher: false));
        Assert.Equal(2, Assert.Single(await Classes(_kids).ClassesAsync()).Teachers.Count);
    }

    [Fact]
    public async Task R232_정규_반이_없는_학생은_모두_미배정반이고_한꺼번에_배정한다()
    {
        var c1 = (await Classes(_kids).CreateAsync("1학년 1반")).Id;
        var c2 = (await Classes(_kids).CreateAsync("1학년 2반")).Id;
        var a = await Person("김철수", "T1001");
        var b = await Person("한겨울", ClassService.NewcomerCode);
        var c = await Person("정하늘", "T1001");
        await Person("정교사", "T1002");                                              // 선생님은 미배정반에 없다

        Assert.Equal([a, b, c], (await Classes(_kids).UnassignedAsync()).Select(p => p.PersonId).Order());
        var (e, n) = await Classes(_kids).AssignAsync(new Dictionary<int, int> { [a] = c1, [b] = c2, [c] = 0 });
        Assert.Equal((ClassError.None, 2), (e, n));
        Assert.Equal(c, Assert.Single(await Classes(_kids).UnassignedAsync()).PersonId);

        Assert.Equal(ClassError.None, await Classes(_kids).DeleteAsync(c1));            // 반을 지우면 미배정반으로
        Assert.Contains(await Classes(_kids).UnassignedAsync(), p => p.PersonId == a);
    }

    [Fact]
    public async Task R232_다른_부서로_진급하면_받는_부서_미배정반에_들어간다()
    {
        var kidAcc = InMemoryDb.SeedAccount(_db, "cheolsu@example.com", "김철수");
        InMemoryDb.SeedMembership(_db, _kids.Id, kidAcc.UserName!, MembershipStatus.Approved, Roles.Participant);
        var pid = (await People(_kids).RosterAsync(null, null)).Single(r => r.UserName == "cheolsu").PersonId;
        var c1 = (await Classes(_kids).CreateAsync("1학년 1반")).Id;
        await Classes(_kids).AddAsync(c1, [pid], teacher: false);
        int req;
        using (var raw = InMemoryDb.Raw(_db))
        {
            var r = new PromotionRequest { UserName = "cheolsu", SourceTenantId = _kids.Id, TargetTenantId = _elem.Id, RequestedByUserName = "t" };
            raw.PromotionRequests.Add(r);
            raw.SaveChanges();
            req = r.Id;
        }
        var (d, c) = In(_elem);
        Assert.Equal(PromotionError.None, await new PromotionApprover(d, new InMemorySystemDb(_db), c, _time).ApproveAsync("minsu", req, "T1001"));

        using (var after = InMemoryDb.Raw(_db)) Assert.Empty(after.ClassMembers);           // 옛 부서 반에서 빠진다
        Assert.Contains(await Classes(_elem).UnassignedAsync(), p => p.Name == "김철수");
    }
}

/// <summary>R-233: 반 · 미배정반 화면은 부서 관리자만(교사도 못 들어온다).</summary>
public partial class ClassFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();

    public ClassFlowTests(WebApplicationFactory<Program> app)
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
    public async Task R233_관리자는_반을_만들고_미배정반에서_배정하고_교사는_들어오지_못한다()
    {
        var a = await Member("admin", Roles.TenantAdmin);
        var page = await a.GetStringAsync("/jamsil-kids/admin/classes");
        Assert.Contains("새친구반", page);
        Assert.Contains("미배정반", page);
        var r = await a.PostAsync("/jamsil-kids/admin/classes?handler=Add", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value, ["name"] = "1학년 1반",
        }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        int cls, kid;
        using (var raw = InMemoryDb.Raw(_db))
        {
            cls = raw.Classes.Single(c => !c.IsNewcomer).Id;
            var p = new Person { TenantId = raw.Tenants.Single().Id, Name = "김철수", RoleCode = "T1001" };
            raw.People.Add(p);
            raw.SaveChanges();
            kid = p.Id;
        }
        var un = await a.GetStringAsync("/jamsil-kids/admin/classes/unassigned");
        Assert.Contains("김철수", un);
        var post = await a.PostAsync("/jamsil-kids/admin/classes/unassigned", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(un).Groups[1].Value, [$"to[{kid}]"] = cls.ToString(),
        }));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.EndsWith("/admin/classes/unassigned/done", post.Headers.Location!.OriginalString);
        var asg = await a.GetStringAsync(post.Headers.Location!.OriginalString);   // 배정 완료: 반마다 아이 · [그 반으로 →]
        Assert.Contains("1명을 반에 배정했어요", asg);
        Assert.Contains("김철수", asg);
        Assert.Contains($"href=\"/jamsil-kids/admin/classes/{cls}\"", asg);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(cls, raw.ClassMembers.Single(m => m.PersonId == kid).ClassId);

        // 반 이동 마법사 → 완료 화면(반 목록 · 부서 홈 고르기)
        var conf = await a.GetStringAsync($"/jamsil-kids/admin/classes/move?S=4&F={cls}&P={kid}&D=Unassigned");
        var mv = await a.PostAsync("/jamsil-kids/admin/classes/move", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(conf).Groups[1].Value, ["F"] = cls.ToString(), ["P"] = kid.ToString(), ["D"] = "Unassigned",
        }));
        Assert.Equal(HttpStatusCode.Redirect, mv.StatusCode);
        Assert.EndsWith("/admin/classes/move/done", mv.Headers.Location!.OriginalString);
        var done = await a.GetStringAsync(mv.Headers.Location!.OriginalString);
        Assert.Contains("1명을 옮겼어요", done);
        Assert.Contains("href=\"/jamsil-kids/admin/classes\"", done);
        Assert.Contains("href=\"/jamsil-kids\"", done);
        using (var raw = InMemoryDb.Raw(_db)) Assert.DoesNotContain(raw.ClassMembers, m => m.PersonId == kid);

        var t = await Member("teacher", Roles.Teacher);
        foreach (var url in new[] { "/jamsil-kids/admin/classes", "/jamsil-kids/admin/classes/unassigned" })
        {
            var denied = await t.GetAsync(url);
            Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
            Assert.StartsWith("/account/denied", denied.Headers.Location!.PathAndQuery);
        }
    }
}
