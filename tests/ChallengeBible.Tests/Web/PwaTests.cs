using System.Net;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>PWA(확정 시안: 오프라인): 서비스 워커 · 오프라인 화면은 로그인 없이 열리고, 화면마다 등록 스크립트가 붙는다.</summary>
public class PwaTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();

    public PwaTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));
        InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
    }

    [Fact]
    public async Task 서비스_워커와_오프라인_화면은_로그인_없이_열린다()
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var sw = await c.GetAsync("/sw.js");
        Assert.Equal(HttpStatusCode.OK, sw.StatusCode);
        var js = await sw.Content.ReadAsStringAsync();
        Assert.Contains("/offline.html", js);
        Assert.Contains("req.mode === 'navigate'", js);   // 화면은 저장하지 않고 연결이 없을 때만 오프라인 화면

        var off = await c.GetAsync("/offline.html");
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.Contains("인터넷 연결이 끊겼어요", await off.Content.ReadAsStringAsync());

        var manifest = await c.GetAsync("/jamsil-kids/manifest.webmanifest");
        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
    }

    [Fact]
    public async Task 화면마다_등록_스크립트가_붙는다()
    {
        var c = _app.CreateClient();
        Assert.Contains("/js/pwa", await c.GetStringAsync("/account/login"));
    }
}
