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

/// <summary>임시 시험 도구 /test-tools(ADR 0095): 설정으로 숨김, 넣기 · 지우기는 플랫폼 관리자만, 상황별 로그인은 가짜 계정(비밀번호 1234)만.</summary>
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
        var tok = TokenRx().Match(await c.GetStringAsync("/test-tools")).Groups[1].Value;
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
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/test-tools")).StatusCode);
        Assert.DoesNotContain("/test-tools", await c.GetStringAsync("/platform"));
    }

    [Fact]
    public async Task 플랫폼_관리자는_넣기_지우기와_상황별_로그인_버튼을_보고_메뉴에도_있다()
    {
        var t = InMemoryDb.SeedTenant(_db, "yb", "YB0001");
        Seed("superadmin", "boss@example.com", platformAdmin: true);
        Seed("yb-s01", "yb-s01" + TestToolsService.EmailDomain, tenantId: t.Id);
        var c = Client(App(enabled: true));
        await Login(c, "superadmin");
        Assert.Contains("/test-tools", await c.GetStringAsync("/platform"));
        var page = await c.GetStringAsync("/test-tools");
        Assert.Contains("가짜 데이터 넣기", page);
        Assert.Contains("value=\"yb-s01\"", page);
        Assert.Contains("3,100점", page);   // s01 상황 이름
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

        var r = await Post(c, "/test-tools?handler=Login", new() { ["user"] = "yb-s01" });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/", r.Headers.Location!.OriginalString);

        // 이제 가짜 계정: 화면은 보이지만 데이터 칸은 없고, 다른 가짜 계정으로 바꿀 수 있다
        var page = await c.GetStringAsync("/test-tools");
        Assert.DoesNotContain("가짜 데이터 넣기", page);
        Assert.Contains("value=\"yb-s05\"", page);
        var run = await Post(c, "/test-tools?handler=Run", new() { ["kind"] = "create", ["mode"] = "test" });
        Assert.NotEqual(HttpStatusCode.OK, run.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(c, "/test-tools?handler=Login", new() { ["user"] = "yb-s05" })).StatusCode);
    }

    [Fact]
    public async Task 가짜가_아닌_계정으로는_로그인_버튼이_통하지_않는다()
    {
        Seed("superadmin", "boss@example.com", platformAdmin: true);
        Seed("real", "real@example.com");
        var c = Client(App(enabled: true));
        await Login(c, "superadmin");
        Assert.Equal(HttpStatusCode.NotFound, (await Post(c, "/test-tools?handler=Login", new() { ["user"] = "real" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(c, "/test-tools?handler=Login", new() { ["user"] = "superadmin" })).StatusCode);
    }

    [Fact]
    public async Task 일반_계정에게는_화면이_없고_로그인_안_하면_로그인_화면으로_간다()
    {
        Seed("real", "real@example.com");
        var app = App(enabled: true);
        var anon = await Client(app).GetAsync("/test-tools");
        Assert.Equal(HttpStatusCode.Redirect, anon.StatusCode);
        Assert.Contains("/account/login", anon.Headers.Location!.OriginalString);
        var c = Client(app);
        await Login(c, "real");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/test-tools")).StatusCode);
    }

    [Fact]
    public async Task 진짜_실행은_확인_글자가_있어야_하고_메모리_DB_에서는_SQL_을_돌리지_않는다()
    {
        Seed("superadmin", "boss@example.com", platformAdmin: true);
        var c = Client(App(enabled: true));
        await Login(c, "superadmin");
        var noWord = await (await Post(c, "/test-tools?handler=Run", new() { ["kind"] = "create", ["mode"] = "real", ["confirm"] = "" })).Content.ReadAsStringAsync();
        Assert.Contains("FAKE 를 써야 해요", noWord);
        var test = await (await Post(c, "/test-tools?handler=Run", new() { ["kind"] = "delete", ["mode"] = "test" })).Content.ReadAsStringAsync();
        Assert.Contains("메모리 DB 에서는 SQL 을 실행하지 않아요", test);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.AuditLogs.Where(a => a.Action == TestToolsService.CreateAction || a.Action == TestToolsService.DeleteAction));
    }

    [Fact]
    public void 앱에_넣은_SQL_은_매개변수로_저장_여부와_날짜를_받고_SQL_Studio_에서_막히는_문장이_없다()
    {
        foreach (var file in new[] { "fake-create.sql", "fake-delete.sql" })
        {
            var sql = TestToolsService.Script(file);
            Assert.Contains("DECLARE @Commit bit = @pCommit;", sql);
            Assert.Contains("@pToday", sql);
            Assert.DoesNotContain("sp_set_session_context", sql);   // 시스템 연결이 이미 켠다(읽기 전용이라 다시 못 바꿈)
            Assert.DoesNotContain("SECURITY POLICY", sql);
            Assert.All(sql, ch => Assert.True(ch < 128, "ASCII 만"));
        }
        Assert.Contains("DELETE FROM [Identity].[User] WHERE Id NOT IN (SELECT UserId FROM #Keep);", TestToolsService.Script("fake-create.sql"));
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
