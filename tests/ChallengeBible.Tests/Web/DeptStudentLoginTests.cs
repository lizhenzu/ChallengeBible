using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>R-367 부서 대표 학생 로그인: 가짜 학생이 있는 부서만 버튼, 생일 30일 안 → 점수 → 기록 순으로 고르고, 비밀번호만 넣고 들어간다. 오늘 = 2027-01-05(서울).</summary>
public partial class DeptStudentLoginTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;

    public DeptStudentLoginTests(WebApplicationFactory<Program> factory)
    {
        _app = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _kids = Dept("jamsil-kids", "KD0001", "잠실새내교회 유년부");
        Dept("jamsil-infant", "IN0001", "잠실새내교회 영아부");
        Dept("jamsil-adult", "AD0001", "잠실새내교회 장년부");
        // 유년부: 점수만 높은 아이 · 생일이 30일 안인 아이 · 진짜(가짜 아님) 아이
        Kid(_kids.Id, "jamsil-kids-s01", "kids-s01@fake-seed.test", bm: 6, bd: 1, points: 3000);
        Kid(_kids.Id, "jamsil-kids-s02", "kids-s02@fake-seed.test", bm: 1, bd: 20, points: 500);
        Kid(_kids.Id, "real-kid", "real@example.com", bm: 1, bd: 10, points: 9000);
        // 장년부는 버튼 없음(영아부 ~ 청년부만). 영아부는 가짜 학생이 없어 버튼 없음
        Kid(Tenants("jamsil-adult"), "jamsil-adult-s01", "adult-s01@fake-seed.test", bm: 1, bd: 10, points: 100);
    }

    private Tenant Dept(string slug, string code, string name)
    {
        var t = InMemoryDb.SeedTenant(_db, slug, code);
        using var raw = InMemoryDb.Raw(_db);
        raw.Tenants.Single(x => x.Id == t.Id).Name = name;
        raw.SaveChanges();
        return t;
    }

    private int Tenants(string slug) { using var raw = InMemoryDb.Raw(_db); return raw.Tenants.Single(t => t.Slug == slug).Id; }

    private void Kid(int tenantId, string userName, string email, byte bm, byte bd, int points)
    {
        var u = InMemoryDb.SeedAccount(_db, email, "김가나");
        using var raw = InMemoryDb.Raw(_db);
        var row = raw.Users.Single(x => x.Id == u.Id);
        (row.UserName, row.NormalizedUserName) = (userName, AppUser.Normalize(userName));
        row.PasswordHash = new PasswordHasher<AppUser>().HashPassword(row, "1234");
        raw.DeptRoles.Add(new DeptRoleUserAssoc { TenantId = tenantId, UserId = row.Id, RoleCode = UserRole.StudentCode });
        raw.People.Add(new Person { TenantId = tenantId, Name = "김가나", UserId = row.Id, BirthMonth = bm, BirthDay = bd, CreatedUtc = DateTime.UtcNow });
        raw.MissionActivities.Add(new MissionActivity { TenantId = tenantId, UserName = userName, MissionDate = new DateOnly(2027, 1, 3), MissionItem = "Word.Me", Points = points, PerformedAt = DateTime.UtcNow });
        raw.SaveChanges();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private HttpClient Client() => _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task R367_로그인_화면에_가짜_학생이_있는_부서만_학생_버튼이_있고_아이_이름은_없다()
    {
        Assert.DoesNotContain("/account/student-login/", await Client().GetStringAsync("/"));   // 2026-11-24 사용자: 첫 화면 맨 아래에서 로그인 화면으로 옮김
        var html = await Client().GetStringAsync("/account/login");
        Assert.Contains("부서 대표 학생 로그인", html);
        var links = Regex.Matches(html, "href=\"/account/student-login/([^\"]+)\"><i></i>([^<]+)</a>").Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToArray();
        Assert.Equal(new[] { ("jamsil-kids", "유년부 학생") }, links);
        Assert.DoesNotContain("jamsil-kids-s0", html);
    }

    [Fact]
    public async Task R367_생일이_30일_안인_아이를_점수보다_먼저_고른다()
    {
        using var scope = _app.Services.CreateScope();
        var pick = await scope.ServiceProvider.GetRequiredService<DeptStudentLoginService>().FindAsync("jamsil-kids");
        Assert.Equal("jamsil-kids-s02", pick!.UserName);   // 1월 20일 생일(15일 남음) > 3,000점이지만 생일 먼 s01, 진짜 아이는 빠짐
        Assert.Equal(15, DeptStudentLoginService.DaysToBirthday(new DateOnly(2027, 1, 5), 1, 20));
        Assert.Equal(0, DeptStudentLoginService.DaysToBirthday(new DateOnly(2027, 1, 5), 1, 5));
        Assert.Equal(361, DeptStudentLoginService.DaysToBirthday(new DateOnly(2027, 1, 5), 1, 1));
    }

    [Fact]
    public async Task R367_비밀번호만_넣으면_그_아이로_부서_홈에_가고_버튼_없는_부서는_404()
    {
        var c = Client();
        const string url = "/account/student-login/jamsil-kids";
        var page = await c.GetStringAsync(url);
        Assert.Contains("유년부 학생", page);
        Assert.DoesNotContain("jamsil-kids-s0", page);
        var tok = TokenRx().Match(page).Groups[1].Value;
        var ok = await c.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = tok, ["Password"] = "1234" }));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Equal("/", ok.Headers.Location!.OriginalString);   // 로그인한 뒤 = 첫 화면(2026-11-26 사용자)

        foreach (var slug in new[] { "jamsil-infant", "jamsil-adult", "nowhere" })
            Assert.Equal(HttpStatusCode.NotFound, (await Client().GetAsync("/account/student-login/" + slug)).StatusCode);
    }
}
