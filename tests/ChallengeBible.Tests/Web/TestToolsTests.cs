using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>임시 시험 도구(ADR 0095 · 0102): 설정으로 숨김, /test-tools/data 가짜 데이터 넣기는 플랫폼 관리자만, /test-tools/login 상황별 로그인은 가짜 계정(비밀번호 1234)만.</summary>
public partial class TestToolsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _db = InMemoryDb.NewName();

    public TestToolsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private WebApplicationFactory<Program> App(bool enabled) => _factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("TestTools:Enabled", enabled ? "true" : "false");
        b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        });
    });

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private static HttpClient Client(WebApplicationFactory<Program> app) => app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>비밀번호 1234 계정. 플랫폼 관리자면 SuperAdmin 역할.</summary>
    private AppUser Seed(string userName, string email, bool platformAdmin = false, int? tenantId = null, string? roleCode = "T1001")
    {
        var u = InMemoryDb.SeedAccount(_db, email, userName + "이름");
        using (var raw = InMemoryDb.Raw(_db))
        {
            var row = raw.Users.Single(x => x.Id == u.Id);
            (row.UserName, row.NormalizedUserName) = (userName, AppUser.Normalize(userName));
            row.PasswordHash = new PasswordHasher<AppUser>().HashPassword(row, "1234");
            if (tenantId is int t) raw.DeptRoles.Add(new DeptRoleUserAssoc { TenantId = t, UserId = row.Id, RoleCode = roleCode });
            raw.SaveChanges();
            u = row;
        }
        if (platformAdmin) InMemoryDb.MakePlatformAdmin(_db, u);
        return u;
    }

    private static async Task<HttpResponseMessage> Post(HttpClient c, string url, Dictionary<string, string> form)
    {
        var tok = TokenRx().Match(await c.GetStringAsync("/test-tools/login")).Groups[1].Value;
        form["__RequestVerificationToken"] = tok;
        return await c.PostAsync(url, new FormUrlEncodedContent(form));
    }

    private static async Task Login(HttpClient c, string login)
    {
        var tok = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value;
        var r = await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tok, ["Email"] = login, ["Password"] = "1234",
        }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
    }

    [Fact]
    public async Task 설정이_꺼지면_플랫폼_관리자에게도_화면이_없다()
    {
        Seed("superadmin", "boss@example.com", platformAdmin: true);
        var app = App(enabled: false);
        var c = Client(app);
        await Login(c, "superadmin");
        foreach (var url in new[] { "/test-tools", "/test-tools/data", "/test-tools/login" })
            Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(url)).StatusCode);
        await c.GetAsync("/platform");   // 로그인 직후 한 번은 오늘 할 일로 갈 수 있다(R-309)
        Assert.DoesNotContain("/test-tools", await c.GetStringAsync("/platform"));
    }

    [Fact]
    public async Task 플랫폼_관리자는_넣기와_상황별_로그인을_따로_보고_메뉴에도_둘_다_있다()
    {
        var t = InMemoryDb.SeedTenant(_db, "yb", "YB0001");
        Seed("superadmin", "boss@example.com", platformAdmin: true);
        Seed("yb-s01", "yb-s01" + TestToolsService.EmailDomain, tenantId: t.Id);
        var c = Client(App(enabled: true));
        await Login(c, "superadmin");
        await c.GetAsync("/platform");
        var menu = await c.GetStringAsync("/platform");
        Assert.Contains("href=\"/test-tools/data\"", menu);
        Assert.Contains("href=\"/test-tools/login\"", menu);
        var old = await c.GetAsync("/test-tools");   // 옛 주소는 넣기로
        Assert.Equal("/test-tools/data", old.Headers.Location!.OriginalString);

        var data = await c.GetStringAsync("/test-tools/data");
        Assert.Contains("가짜 데이터 넣기", data);
        Assert.Contains("href=\"/platform/reset\"", data);   // 지우기는 완전 초기화로
        Assert.DoesNotContain("지우기</button>", data);
        Assert.DoesNotContain("value=\"yb-s01\"", data);    // 로그인 버튼은 따로
        Assert.DoesNotContain("케이스별 시험", data);

        var login = await c.GetStringAsync("/test-tools/login");
        Assert.Contains("value=\"yb-s01\"", login);
        Assert.Contains("3,100점", login);   // s01 상황 이름
        Assert.DoesNotContain("넣기</button>", login);
        Assert.DoesNotContain("시험 관리자 만들기", login);   // 관리자는 지울 수 없어 만들지 않는다(2026-11-16)
    }

    [Fact]
    public async Task 상황별_로그인은_가짜_계정으로_들어가고_가짜_계정은_넣기_지우기를_못_한다()
    {
        var t = InMemoryDb.SeedTenant(_db, "yb", "YB0001");
        Seed("superadmin", "boss@example.com", platformAdmin: true);
        Seed("yb-s01", "yb-s01" + TestToolsService.EmailDomain, tenantId: t.Id);
        Seed("yb-s05", "yb-s05" + TestToolsService.EmailDomain, tenantId: t.Id);
        var c = Client(App(enabled: true));
        await Login(c, "superadmin");

        var r = await Post(c, "/test-tools/login", new() { ["user"] = "yb-s01" });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/", r.Headers.Location!.OriginalString);

        // 이제 가짜 계정: 로그인 화면은 보이지만 넣기 화면은 없고, 다른 가짜 계정으로 바꿀 수 있다
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/test-tools/data")).StatusCode);
        Assert.Equal("/test-tools/login", (await c.GetAsync("/test-tools")).Headers.Location!.OriginalString);
        var page = await c.GetStringAsync("/test-tools/login");
        Assert.Contains("value=\"yb-s05\"", page);
        var run = await Post(c, "/test-tools/data", new() { ["confirm"] = "FAKE" });
        Assert.NotEqual(HttpStatusCode.OK, run.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(c, "/test-tools/login", new() { ["user"] = "yb-s05" })).StatusCode);
    }

    [Fact]
    public async Task 비밀번호를_바꾼_가짜_관리자도_플랫폼_관리자는_버튼으로_들어가고_가짜_계정은_못_들어간다()
    {
        var t = InMemoryDb.SeedTenant(_db, "yb", "YB0001");
        Seed("superadmin", "boss@example.com", platformAdmin: true);
        var adm = Seed("yb-admin", "yb-admin" + TestToolsService.EmailDomain, tenantId: t.Id, roleCode: "T1005");
        Seed("yb-s01", "yb-s01" + TestToolsService.EmailDomain, tenantId: t.Id);
        using (var raw = InMemoryDb.Raw(_db))   // 2026-11-15: 부서 관리자 비밀번호를 SQL 로 바꿈(1234 가 아님)
        {
            var row = raw.Users.Single(x => x.Id == adm.Id);
            row.PasswordHash = new PasswordHasher<AppUser>().HashPassword(row, "다른비밀번호");
            raw.SaveChanges();
        }
        var c = Client(App(enabled: true));
        await Login(c, "superadmin");
        var r = await Post(c, "/test-tools/login", new() { ["user"] = "yb-admin" });   // 플랫폼 관리자: 가장이라 비밀번호를 보지 않는다
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("yb-admin이름", await c.GetStringAsync("/test-tools/login"));

        var kid = Client(App(enabled: true));                                                  // 가짜 계정이 누르면 비밀번호 1234 로 확인 → 못 들어간다
        await Login(kid, "yb-s01");
        Assert.Equal(HttpStatusCode.NotFound, (await Post(kid, "/test-tools/login", new() { ["user"] = "yb-admin" })).StatusCode);
    }

    [Fact]
    public async Task 가짜가_아닌_계정으로는_로그인_버튼이_통하지_않는다()
    {
        Seed("superadmin", "boss@example.com", platformAdmin: true);
        Seed("real", "real@example.com");
        var c = Client(App(enabled: true));
        await Login(c, "superadmin");
        Assert.Equal(HttpStatusCode.NotFound, (await Post(c, "/test-tools/login", new() { ["user"] = "real" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(c, "/test-tools/login", new() { ["user"] = "superadmin" })).StatusCode);
    }

    [Fact]
    public async Task 일반_계정에게는_화면이_없고_로그인_안_하면_로그인_화면으로_간다()
    {
        Seed("real", "real@example.com");
        var app = App(enabled: true);
        foreach (var url in new[] { "/test-tools", "/test-tools/data", "/test-tools/login" })
        {
            var anon = await Client(app).GetAsync(url);
            Assert.Equal(HttpStatusCode.Redirect, anon.StatusCode);
            Assert.Contains("/account/login", anon.Headers.Location!.OriginalString);
        }
        var c = Client(app);
        await Login(c, "real");
        foreach (var url in new[] { "/test-tools", "/test-tools/data", "/test-tools/login" })
            Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task 실행은_확인_글자가_있어야_하고_메모리_DB_에서는_SQL_을_돌리지_않는다()
    {
        Seed("superadmin", "boss@example.com", platformAdmin: true);
        var c = Client(App(enabled: true));
        await Login(c, "superadmin");
        var noWord = await (await Post(c, "/test-tools/data", new() { ["confirm"] = "" })).Content.ReadAsStringAsync();
        Assert.Contains("FAKE 를 써야 해요", noWord);
        var page = await (await Post(c, "/test-tools/data", new() { ["confirm"] = "FAKE" })).Content.ReadAsStringAsync();
        Assert.Contains("메모리 DB 에서는 SQL 을 실행하지 않아요", page);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.AuditLogs.Where(a => a.Action == TestToolsService.CreateAction));
    }

    [Fact]
    public void 앱에_넣은_SQL_은_매개변수로_저장_여부와_날짜를_받고_SQL_Studio_에서_막히는_문장이_없다()
    {
        foreach (var file in new[] { "fake-create.sql" })
        {
            var sql = TestToolsService.Script(file);
            Assert.Contains("DECLARE @Commit bit = @pCommit;", sql);
            Assert.Contains("@pToday", sql);
            Assert.DoesNotContain("sp_set_session_context", sql);   // 시스템 연결이 이미 켠다(읽기 전용이라 다시 못 바꿈)
            Assert.DoesNotContain("SECURITY POLICY", sql);
            Assert.All(sql, ch => Assert.True(ch < 128, "ASCII 만"));
        }
    }

    [Fact]
    public void 넣기는_아무것도_지우지_않고_이미_있는_반에_새_명부_줄과_반_배정을_만든다()
    {
        var create = TestToolsService.Script("fake-create.sql");
        Assert.DoesNotContain("DELETE FROM [Identity]", create);
        Assert.DoesNotContain("DELETE FROM app.", create);
        Assert.DoesNotContain("DELETE FROM dbo.", create);
        Assert.DoesNotContain("INSERT app.Class ", create);           // 반은 만들지 않는다(이미 있는 반에만)
        Assert.DoesNotContain("UPDATE x SET UserId", create);         // 이미 있는 명부 줄은 건드리지 않는다
        Assert.Contains("INSERT app.Person (TenantId, Name, RoleCode, BirthMonth, BirthDay, UserId, CreatedUtc, CreatedByUserName)", create);
        Assert.Contains("INSERT app.ClassMember (TenantId, TermId, ClassId, PersonId, IsTeacher, CreatedUtc)", create);
        Assert.Contains("c.IsNewcomer = 0", create);                  // 새친구반에는 넣지 않는다
        Assert.Contains("N'ADMIN', N'SUPERADMIN'", create);           // 이미 넣었는지 볼 때 관리자 계정은 뺀다
        Assert.Throws<InvalidOperationException>(() => TestToolsService.Script("fake-delete.sql"));   // 지우기는 완전 초기화(ADR 0102)
    }

    [Fact]
    public void 상황_코드는_틀에_있는_사람이다()
    {
        var codes = new HashSet<string> { "admin" };
        for (var i = 1; i <= 10; i++) { codes.Add($"t{i:00}"); codes.Add($"p{i:00}"); }
        for (var i = 1; i <= 40; i++) codes.Add($"s{i:00}");
        Assert.All(TestToolsService.Scenarios, s => Assert.Contains(s.Code, codes));
        Assert.Equal(TestToolsService.Scenarios.Count, TestToolsService.Scenarios.Select(s => s.Code + s.Label).Distinct().Count());
    }
}
