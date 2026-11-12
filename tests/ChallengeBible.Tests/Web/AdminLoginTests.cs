using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>첫 화면 "관리자 로그인"(2026-11-12): 비밀번호만, 아이디는 화면에 없고, 플랫폼 관리자만, 틀린 횟수 제한.</summary>
public partial class AdminLoginTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();

    public AdminLoginTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private HttpClient Client() => _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>가입 화면으로 계정을 만들고(비밀번호 1234) 아이디를 superadmin 으로 바꾼다.</summary>
    private async Task SeedSuperAdmin(bool platformAdmin)
    {
        var c = Client();
        var tok = TokenRx().Match(await c.GetStringAsync("/account/signup")).Groups[1].Value;
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tok, ["DisplayName"] = "관리자", ["Email"] = "boss@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        using var raw = InMemoryDb.Raw(_db);
        var u = raw.Users.Single(a => a.Email == "boss@example.com");
        (u.UserName, u.NormalizedUserName) = ("superadmin", AppUser.Normalize("superadmin"));
        raw.SaveChanges();
        if (platformAdmin) InMemoryDb.MakePlatformAdmin(_db, u);
    }

    private async Task<HttpResponseMessage> Login(HttpClient c, string password)
    {
        var tok = TokenRx().Match(await c.GetStringAsync("/account/admin-login")).Groups[1].Value;
        return await c.PostAsync("/account/admin-login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tok, ["Password"] = password,
        }));
    }

    [Fact]
    public async Task 첫_화면에서_비밀번호만으로_플랫폼_관리자가_들어가고_아이디는_보이지_않는다()
    {
        await SeedSuperAdmin(platformAdmin: true);
        var c = Client();
        Assert.Contains("href=\"/account/admin-login\"", await c.GetStringAsync("/"));
        var page = await c.GetStringAsync("/account/admin-login");
        Assert.DoesNotContain("superadmin", page);

        Assert.Equal(HttpStatusCode.OK, (await Login(c, "nope")).StatusCode);   // 틀리면 화면 그대로
        var ok = await Login(c, "1234");
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Equal("/platform", ok.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task 플랫폼_관리자가_아니면_비밀번호가_맞아도_들어가지_못한다()
    {
        await SeedSuperAdmin(platformAdmin: false);
        Assert.Equal(HttpStatusCode.OK, (await Login(Client(), "1234")).StatusCode);
    }

    [Fact]
    public async Task 다섯_번_틀리면_맞는_비밀번호도_잠시_막는다()
    {
        await SeedSuperAdmin(platformAdmin: true);
        var c = Client();
        for (var i = 0; i < 5; i++) await Login(c, "wrong" + i);
        var r = await Login(c, "1234");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("15분 뒤", await r.Content.ReadAsStringAsync());
    }
}
