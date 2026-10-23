using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>플랫폼 화면: 플랫폼 관리자만, 부서를 만들면 목록 · 참여 코드가 보인다.</summary>
public partial class PlatformFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();

    public PlatformFlowTests(WebApplicationFactory<Program> app) =>
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> LoggedIn(string loginId, bool platformAdmin)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await c.GetStringAsync("/account/signup");
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(html).Groups[1].Value,
            ["DisplayName"] = loginId, ["LoginId"] = loginId, ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        if (!platformAdmin) return c;
        using (var raw = InMemoryDb.Raw(_db)) { raw.Accounts.Single(a => a.LoginId == loginId).IsPlatformAdmin = true; raw.SaveChanges(); }
        // 플랫폼 관리자 표시는 로그인할 때 쿠키에 들어간다 → 다시 로그인
        html = await c.GetStringAsync("/account/login");
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(html).Groups[1].Value, ["LoginId"] = loginId, ["Password"] = "1234",
        }));
        return c;
    }

    [Fact]
    public async Task 플랫폼_관리자가_아니면_부서_관리를_못_본다()
    {
        var c = await LoggedIn("cheolsu", platformAdmin: false);
        var r = await c.GetAsync("/platform/tenants");
        Assert.StartsWith("/account/denied", r.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task 플랫폼_관리자는_부서를_만들고_첫_화면은_플랫폼으로()
    {
        var c = await LoggedIn("operator", platformAdmin: true);
        Assert.Equal("/platform/tenants", (await c.GetAsync("/")).Headers.Location!.OriginalString);

        var page = await c.GetStringAsync("/platform/tenants");
        var created = await c.PostAsync("/platform/tenants?handler=Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value,
            ["Name"] = "잠실새내교회 유년부", ["Slug"] = "jamsil-kids", ["TimeZoneId"] = "Asia/Seoul", ["ThemeColor"] = "blue",
            ["Mission"] = "true", ["Reading"] = "true",
        }));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        var list = await c.GetStringAsync(created.Headers.Location!.OriginalString);
        Assert.Contains("잠실새내교회 유년부", list);
        Assert.Contains("부서를 만들었어요", list);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Contains(raw.Tenants.Single().JoinCode.Trim(), list);
    }
}
