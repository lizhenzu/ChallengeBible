using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Pages;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>앱 설치 안내(ADR 0115, 확정 시안 설치안내): R-330 돌아갈 주소 · R-331 보내지 않는 화면 · R-332 설치 앱은 하나.</summary>
public partial class InstallTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();

    public InstallTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));
        InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
    }

    [Theory]   // R-330
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/jamsil-kids/word-mission?d=1", "/jamsil-kids/word-mission?d=1")]
    [InlineData("https://evil.example.com/", "/")]
    [InlineData("//evil.example.com/", "/")]
    [InlineData("/\\evil.example.com", "/")]
    [InlineData("/install?back=/x", "/")]
    public void 돌아갈_주소는_이_사이트_안만(string? back, string expected) => Assert.Equal(expected, InstallModel.SafeBack(back));

    [Fact]
    public async Task 설치_안내는_로그인_없이_열리고_기기별_안내가_다_들어_있다()
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var r = await c.GetAsync("/install?back=" + Uri.EscapeDataString("https://evil.example.com"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var html = await r.Content.ReadAsStringAsync();
        Assert.Contains("data-back=\"/\"", html);                          // 바깥 주소는 첫 화면으로(R-330)
        Assert.Contains("data-install=\"off\"", html);                     // 안내 자신은 다시 보내지 않는다(R-331)
        foreach (var v in new[] { "data-for=\"android desktop\"", "data-for=\"ios\"", "data-for=\"inapp\"", "data-for=\"desktop-other\"", "data-for=\"done\"" })
            Assert.Contains(v, html);
        Assert.Contains("바로가기 설치하기", html);                         // 안드로이드 · PC 버튼 문구를 하나로(2026-11-26 사용자)
        Assert.Contains("바로가기 설치해요", html);
        Assert.DoesNotContain("PC에 설치하세요", html);
        Assert.Contains("홈 화면에 추가", html);
        Assert.Contains("크롬으로 열기", html);
        Assert.Contains("이미 설치했어요", html);
        Assert.Contains("나중에 할게요", html);
        Assert.Contains("src=\"/js/install.", html);   // 파일 이름에 버전이 붙는다
    }

    [Fact]
    public async Task 설치_앱은_하나_이름은_도전_성경말씀_시작은_첫_화면()   // R-332
    {
        var c = _app.CreateClient();
        var m = await c.GetAsync("/manifest.webmanifest");
        Assert.Equal("application/manifest+json", m.Content.Headers.ContentType!.MediaType);
        var j = JsonDocument.Parse(await m.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("/", j.GetProperty("id").GetString());
        Assert.Equal("/", j.GetProperty("start_url").GetString());
        Assert.Equal("/", j.GetProperty("scope").GetString());
        Assert.Equal(BrandingService.DefaultName, j.GetProperty("name").GetString());
        Assert.Equal("도전!!!성경말씀", j.GetProperty("short_name").GetString());
        Assert.Equal("standalone", j.GetProperty("display").GetString());
        Assert.Contains(j.GetProperty("icons").EnumerateArray(), x => x.GetProperty("purpose").GetString() == "maskable");
        Assert.Equal("webapp", j.GetProperty("related_applications")[0].GetProperty("platform").GetString());

        foreach (var url in new[] { "/", "/account/login" })   // 부서 밖 화면도 같은 앱, 설치 안내로 보낼 수 있음(R-331)
        {
            var html = await c.GetStringAsync(url);
            Assert.Contains("rel=\"manifest\" href=\"/manifest.webmanifest\"", html);
            Assert.Contains("src=\"/js/install-gate.", html);
            Assert.DoesNotContain("data-install=\"off\"", html);
        }
    }

    [Fact]
    public async Task 플랫폼_관리_화면은_설치_안내로_보내지_않는다()   // R-331
    {
        var a = InMemoryDb.SeedAccount(_db, "op@example.com", "운영자");
        using (var raw = InMemoryDb.Raw(_db))
        {
            var row = raw.Users.Single(x => x.UserName == a.UserName!);
            row.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<AppUser>().HashPassword(row, "1234");
            InMemoryDb.AddRole(_db, row.Id, IdentityRoles.SuperAdmin);
            raw.SaveChanges();
        }
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var h = await c.GetStringAsync("/account/login");
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(h).Groups[1].Value, ["Email"] = "op@example.com", ["Password"] = "1234",
        }));
        var page = await c.GetStringAsync("/platform/todo");
        Assert.Contains("data-install=\"off\"", page);
    }

    [Fact]
    public async Task 안내_스크립트는_설치된_앱_나중에_자동_프로그램을_거른다()   // R-331 (브라우저 동작은 스크립트로)
    {
        var c = _app.CreateClient();
        var gate = await c.GetStringAsync("/js/install-gate.js");
        Assert.Contains("display-mode: standalone", gate);
        Assert.Contains("navigator.standalone", gate);
        Assert.Contains("'done'", gate);
        Assert.Contains("'later'", gate);
        Assert.Contains("navigator.webdriver", gate);
        Assert.Contains("getInstalledRelatedApps", gate);
        Assert.Contains("cb-install-seen", gate);
        var js = await c.GetStringAsync("/js/install.js");
        Assert.Contains("7 * 24 * 60 * 60 * 1000", js);                 // [나중에] 7일
        Assert.Contains("beforeinstallprompt", js);
        Assert.Contains("kakaotalk://web/openExternal", js);
        Assert.Contains("package=com.android.chrome", js);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();
}
