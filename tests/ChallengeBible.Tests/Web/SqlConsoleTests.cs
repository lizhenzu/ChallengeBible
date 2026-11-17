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

/// <summary>R-320 플랫폼 SQL 실행(ADR 0107): 플랫폼 관리자만, 기본 시험 실행, 저장은 확인 글자, 트랜잭션 문장 거절, 감사 기록.</summary>
public partial class SqlConsoleTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();

    public SqlConsoleTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        var kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        InMemoryDb.SeedAccount(_db, "admin@example.com", "admin");
        string adminName;
        using (var raw = InMemoryDb.Raw(_db)) adminName = raw.Users.Single(u => u.Email == "admin@example.com").UserName!;
        InMemoryDb.SeedMembership(_db, kids.Id, adminName, MembershipStatus.Approved, [Roles.Teacher, Roles.TenantAdmin]);
        var op = InMemoryDb.SeedAccount(_db, "operator@example.com", "운영자");
        InMemoryDb.AddRole(_db, op.Id, IdentityRoles.SuperAdmin);
        using var r2 = InMemoryDb.Raw(_db);
        foreach (var u in r2.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        r2.SaveChanges();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Login(string who)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value, ["Email"] = who + "@example.com", ["Password"] = "1234",
        }));
        return c;
    }

    private static async Task<string> Run(HttpClient c, string sql, string mode, string confirm = "")
    {
        var tok = TokenRx().Match(await c.GetStringAsync("/platform/sql")).Groups[1].Value;
        var r = await c.PostAsync("/platform/sql", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tok, ["query"] = sql, ["mode"] = mode, ["confirm"] = confirm,
        }));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task R320_플랫폼_관리자만_들어오고_메뉴에_있다()
    {
        var admin = await Login("admin");
        var r = await admin.GetAsync("/platform/sql");
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);

        var op = await Login("operator");
        await op.GetAsync("/platform");   // 첫 화면은 할 일로 갈 수 있음(R-309)
        var html = await op.GetStringAsync("/platform/sql");
        Assert.Contains("SQL 실행", html);
        Assert.Contains("시험 실행(되돌림)", html);
        Assert.Contains("data-sql-preset", html);
        Assert.Contains("href=\"/platform/sql\"", await op.GetStringAsync("/platform/ops"));
    }

    [Fact]
    public async Task R320_저장은_확인_글자가_있어야_하고_없으면_실행하지_않는다()
    {
        var op = await Login("operator");
        await op.GetAsync("/platform");
        var html = await Run(op, "UPDATE app.Tenant SET Name = Name;", "save", "");
        Assert.Contains("SAVE", html);
        Assert.Contains("실행하지 않았어요", html);
        using var raw = InMemoryDb.Raw(_db);
        Assert.DoesNotContain(raw.AuditLogs, a => a.Action == SqlConsoleService.Action);
    }

    [Fact]
    public async Task R320_트랜잭션_문장은_받지_않는다()
    {
        Assert.NotNull(SqlConsoleService.Reject("DELETE FROM x; COMMIT;"));
        Assert.NotNull(SqlConsoleService.Reject("begin tran; delete x"));
        Assert.NotNull(SqlConsoleService.Reject("ROLLBACK"));
        Assert.NotNull(SqlConsoleService.Reject("SET IMPLICIT_TRANSACTIONS ON"));
        Assert.Null(SqlConsoleService.Reject("SELECT CommitCount FROM t"));   // 단어 일부는 괜찮음
        Assert.Null(SqlConsoleService.Reject("SELECT 1"));

        var op = await Login("operator");
        await op.GetAsync("/platform");
        Assert.Contains("COMMIT · ROLLBACK · BEGIN TRAN", await Run(op, "DELETE FROM app.Tenant; COMMIT;", "save", "SAVE"));
    }

    [Fact]
    public async Task R320_메모리_DB_에서는_실행하지_않는다()
    {
        var op = await Login("operator");
        await op.GetAsync("/platform");
        Assert.Contains("메모리 DB", await Run(op, "SELECT 1", "test"));
    }

    [Fact]
    public async Task R320_서비스는_플랫폼_관리자가_아니면_거절한다()
    {
        using var scope = _app.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<SqlConsoleService>();
        string adminName;
        using (var raw = InMemoryDb.Raw(_db)) adminName = raw.Users.Single(u => u.Email == "admin@example.com").UserName!;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.RunAsync(adminName, "SELECT 1", false));
    }

    [Fact]
    public void 감사_로그는_400자_칸에_맞게_자른다_긴_SQL_도_실행_뒤_오류가_나지_않게()
    {
        var longSql = string.Concat(Enumerable.Repeat("SELECT N'김경결' AS 이름; ", 60));   // 1,000자 넘음(2026-11-18 오류 화면)
        var d = ChallengeBible.Web.Services.SqlConsoleService.AuditDetail("test", longSql);
        Assert.True(d.Length <= ChallengeBible.Web.Services.SqlConsoleService.AuditMax);
        Assert.StartsWith("test: SELECT", d);
        Assert.EndsWith("…", d);
        Assert.Equal("save: SELECT 1", ChallengeBible.Web.Services.SqlConsoleService.AuditDetail("save", "SELECT 1"));
    }
}
