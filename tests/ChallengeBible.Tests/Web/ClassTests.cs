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

/// <summary>조직 · 반(ADR 0093): R-231 해 · 새해 복사, R-232 한 해 한 반 · 선생님 자격, R-233 관리자만.</summary>
public class ClassTests
{
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();   // 2027-01-05
    private readonly Tenant _kids, _elem;

    public ClassTests()
    {
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        var admin = InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜");
        InMemoryDb.SeedMembership(_db, _kids.Id, admin.UserName!, MembershipStatus.Approved, Roles.TenantAdmin);
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
    public async Task R232_학생은_한_해에_한_반에만_들고_선생님은_일반_반은_교사만()
    {
        var c1 = (await Classes(_kids).CreateAsync(null, "1학년 1반", false)).Id;
        var c2 = (await Classes(_kids).CreateAsync((await Classes(_kids).TermsAsync()).Single().Id, "1학년 2반", false)).Id;
        var kid = await Person("김철수", "T1001");
        var newbie = await Person("한겨울", ClassService.NewcomerCode);
        var teacher = await Person("정교사", "T1002");
        var officer = await Person("최부장", "T1005");
        var parent = await Person("김영희", null);

        Assert.Equal(ClassError.None, await Classes(_kids).AddAsync(c1, [kid, newbie], teacher: false));
        Assert.Equal(ClassError.InOtherClass, await Classes(_kids).AddAsync(c2, [kid], teacher: false));
        Assert.Equal(ClassError.NotEligible, await Classes(_kids).AddAsync(c2, [parent], teacher: false));
        Assert.Equal(ClassError.NotEligible, await Classes(_kids).AddAsync(c1, [officer], teacher: true));     // 일반 반은 교사만
        Assert.Equal(ClassError.None, await Classes(_kids).AddAsync(c1, [teacher], teacher: true));
        Assert.Equal(ClassError.InOtherClass, await Classes(_kids).AddAsync(c2, [teacher], teacher: true));   // 선생님도 한 반

        var rows = await Classes(_kids).ClassesAsync((await Classes(_kids).TermsAsync()).Single().Id);
        var r1 = rows.Single(r => r.Id == c1);
        Assert.Equal((2, "정교사"), (r1.Students, Assert.Single(r1.Teachers)));
        Assert.DoesNotContain(await Classes(_kids).UnassignedAsync(rows.Count > 0 ? (await Classes(_kids).TermsAsync()).Single().Id : null), p => p.PersonId == kid);
        Assert.Equal(ClassError.NotFound, await Classes(_elem).AddAsync(c1, [kid], teacher: false));          // 다른 부서 반은 보이지 않는다
    }

    [Fact]
    public async Task R232_새친구반은_하나이고_담당은_교사_임원_관리자_학생은_넣지_않는다()
    {
        var nc = (await Classes(_kids).CreateAsync(null, "새친구반", true)).Id;
        var term = (await Classes(_kids).TermsAsync()).Single().Id;
        Assert.Equal(ClassError.OneNewcomer, (await Classes(_kids).CreateAsync(term, "새친구 2", true)).Error);
        var officer = await Person("최부장", "T1005");
        var kid = await Person("김철수", "T1001");
        var adminRow = (await People(_kids).RosterAsync(null, null)).Single(r => r.IsAdmin).PersonId;

        Assert.Equal(ClassError.None, await Classes(_kids).AddAsync(nc, [officer, adminRow], teacher: true));
        Assert.Equal(ClassError.NotEligible, await Classes(_kids).AddAsync(nc, [kid], teacher: false));
        Assert.Equal(2, Assert.Single(await Classes(_kids).ClassesAsync(term)).Teachers.Count);
    }

    [Fact]
    public async Task R231_새해_반은_복사하고_시작일부터_지금_해가_된다()
    {
        var c1 = (await Classes(_kids).CreateAsync(null, "1학년 1반", false)).Id;
        var kid = await Person("김철수", "T1001");
        var gone = await Person("박하린", "T1001");
        await Classes(_kids).AddAsync(c1, [kid, gone], teacher: false);
        var y2027 = (await Classes(_kids).TermsAsync()).Single();
        Assert.Equal((2027, new DateOnly(2027, 1, 5), true), (y2027.Year, y2027.StartDate, y2027.IsCurrent));
        Assert.Equal(PeopleError.None, await People(_kids).DeleteAsync(gone));   // 명부에서 빠지면 반에서도 빠진다

        Assert.Equal(ClassError.BadDate, (await Classes(_kids).CopyAsync(y2027.Id, new DateOnly(2027, 1, 1))).Error);
        var (e, next) = await Classes(_kids).CopyAsync(y2027.Id, new DateOnly(2028, 1, 2));
        Assert.Equal(ClassError.None, e);
        Assert.Equal(ClassError.YearExists, (await Classes(_kids).CopyAsync(y2027.Id, new DateOnly(2028, 1, 2))).Error);

        var terms = await Classes(_kids).TermsAsync();
        Assert.True(terms.Single(t => t.Year == 2027).IsCurrent);                // 시작일 전까지는 지난해 반
        Assert.True(terms.Single(t => t.Year == 2028).IsFuture);
        var copied = Assert.Single(await Classes(_kids).ClassesAsync(next));
        Assert.Equal(("1학년 1반", 1), (copied.Name, copied.Students));
        Assert.Single(await Classes(_kids).ClassesAsync(y2027.Id));              // 지난해 기록은 그대로
        Assert.Equal(ClassError.BadDate, await Classes(_kids).SetStartAsync(next, new DateOnly(2027, 1, 5)));
        Assert.Equal(ClassError.None, await Classes(_kids).SetStartAsync(next, new DateOnly(2027, 12, 26)));

        var rows = ClassService.ToRows([new ClassTerm { Id = 1, Year = 2027, StartDate = new(2027, 1, 5) }, new ClassTerm { Id = 2, Year = 2028, StartDate = new(2027, 12, 26) }], new DateOnly(2027, 12, 26));
        Assert.True(rows.Single(r => r.Id == 2).IsCurrent);
    }

    [Fact]
    public async Task R232_진급하면_옛_부서_반에서_빠진다()
    {
        var kidAcc = InMemoryDb.SeedAccount(_db, "cheolsu@example.com", "김철수");
        InMemoryDb.SeedMembership(_db, _kids.Id, kidAcc.UserName!, MembershipStatus.Approved, Roles.Participant);
        var pid = (await People(_kids).RosterAsync(null, null)).Single(r => r.UserName == "cheolsu").PersonId;
        var c1 = (await Classes(_kids).CreateAsync(null, "1학년 1반", false)).Id;
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

        using var after = InMemoryDb.Raw(_db);
        Assert.Empty(after.ClassMembers);
    }

    [Fact]
    public void R231_새해_처음_값은_1월_첫_주일() =>
        Assert.Equal(new DateOnly(2028, 1, 2), ChallengeBible.Web.Pages.Admin.ClassYearModel.FirstSunday(2028));
}

/// <summary>R-233: 반 화면은 부서 관리자만(교사도 못 들어온다).</summary>
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
    public async Task R233_관리자는_반을_만들고_교사는_들어오지_못한다()
    {
        var a = await Member("admin", Roles.TenantAdmin);
        var page = await a.GetStringAsync("/jamsil-kids/admin/classes");
        Assert.Contains("+ 반 만들기", page);
        var r = await a.PostAsync("/jamsil-kids/admin/classes?handler=Add", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value, ["name"] = "1학년 1반",
        }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        int id;
        using (var raw = InMemoryDb.Raw(_db)) id = raw.Classes.Single().Id;
        Assert.Contains("반 없는 사람에서 넣기", await a.GetStringAsync($"/jamsil-kids/admin/classes/{id}"));
        Assert.Contains("복사해서 만들기", await a.GetStringAsync($"/jamsil-kids/admin/classes/new-year?From={RawTerm()}"));

        var t = await Member("teacher", Roles.Teacher);
        var denied = await t.GetAsync("/jamsil-kids/admin/classes");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("/account/denied", denied.Headers.Location!.PathAndQuery);
    }

    private int RawTerm() { using var raw = InMemoryDb.Raw(_db); return raw.ClassTerms.Single().Id; }
}
