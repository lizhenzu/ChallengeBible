using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>퀴즈 출제 · 말씀 입력 · AI 프롬프트(ADR 0098)는 플랫폼 관리자(superadmin)만. 부서 관리자는 화면도 버튼도 없다(2026-11-15 사용자).</summary>
public partial class WordToolsAccessTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();

    public WordToolsAccessTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));
        var kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        foreach (var (who, name) in new[] { ("op", "운영자"), ("kidsadmin", "부서관리") })
        {
            var a = InMemoryDb.SeedAccount(_db, who + "@example.com", name);
            using var raw = InMemoryDb.Raw(_db);
            var u = raw.Users.Single(x => x.Id == a.Id);
            u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
            raw.SaveChanges();
            if (who == "op") InMemoryDb.AddRole(_db, a.Id, IdentityRoles.SuperAdmin);
            else InMemoryDb.SeedMembership(_db, kids.Id, a.UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        }
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Login(string email)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value, ["Email"] = email, ["Password"] = "1234",
        }));
        return c;
    }

    private static readonly string[] Pages =
        ["/platform/content/quiz/check", "/platform/content/word/check", "/platform/content/word/month", "/platform/content/word/clips", "/platform/quiz-ai-prompt"];

    [Fact]
    public async Task 부서_관리자는_퀴즈_출제_말씀_입력_AI_프롬프트를_열_수_없다()
    {
        var admin = await Login("kidsadmin@example.com");
        foreach (var p in Pages)
            Assert.NotEqual(HttpStatusCode.OK, (await admin.GetAsync(p)).StatusCode);
        Assert.DoesNotContain("quiz-ai-prompt", await admin.GetStringAsync("/kids/admin"));
    }

    [Fact]
    public async Task 플랫폼_관리자는_연다()
    {
        var op = await Login("op@example.com");
        foreach (var p in Pages)
            Assert.Equal(HttpStatusCode.OK, (await op.GetAsync(p)).StatusCode);
    }
}
