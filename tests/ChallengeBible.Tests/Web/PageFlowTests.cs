using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>화면 흐름: 첫 화면 → 가입 → 참여 신청 → 승인 기다림 (DB 는 메모리).</summary>
public partial class PageFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();

    public PageFlowTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));   // 가입 때 임시 부서 확인(0064)
        }));
    }

    private HttpClient Client() => _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private static async Task<string> Token(HttpClient c, string url) =>
        TokenRx().Match(await c.GetStringAsync(url)).Groups[1].Value;

    [Fact]
    public async Task 로그인_전_첫_화면에는_부서_이름이_없다_0045()
    {
        var html = await Client().GetStringAsync("/");
        Assert.Contains("로그인", html);
        Assert.DoesNotContain("유년부", html);
        var year = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul")).Year;
        Assert.Contains($"© {year} 잠실새내 · 주일학교", html);   // 연도는 시스템 날짜
    }

    [Fact]
    public async Task 로그인하지_않으면_참여_신청은_로그인으로()
    {
        var r = await Client().GetAsync("/join");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.StartsWith("/account/login", r.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task 가입하고_코드로_참여하면_승인_없이_바로_그_부서로_간다()
    {
        InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        var c = Client();

        var signup = await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await Token(c, "/account/signup"),
            ["DisplayName"] = "김철수", ["Email"] = "cheolsu@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        Assert.Equal(HttpStatusCode.Redirect, signup.StatusCode);
        Assert.Equal("/join?welcome=1", signup.Headers.Location!.OriginalString);

        var found = await c.PostAsync("/join?handler=Find", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await Token(c, "/join"),
            ["Code"] = "jskids",
        }));
        var foundHtml = await found.Content.ReadAsStringAsync();
        Assert.Matches(@"<small>잠실새내교회</small>\s*<b>유년부</b>", foundHtml);

        var applied = await c.PostAsync("/join?handler=Apply", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", await Token(c, "/join")),
            new("Code", "JSKIDS"),
            new("SelectedRoles", Roles.Participant),
        }));
        Assert.Equal(HttpStatusCode.Redirect, applied.StatusCode);
        Assert.Equal("/jamsil-kids", applied.Headers.Location!.OriginalString);   // 승인 없이 바로 소속(R-340, 2026-11-20)
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/jamsil-kids")).StatusCode);
    }
}
