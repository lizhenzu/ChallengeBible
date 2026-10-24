using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>부서 이름 · 색(0033 · 0038, 확정 시안: 관리-이름색): 저장 규칙, 바꾼 기록, 대표 색 적용 범위, 부서 매니페스트.</summary>
public partial class BrandingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids, _elem;

    public BrandingTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
    }

    private BrandingService In(Tenant t)
    {
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        var tc = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db)) tc.Set(raw.Tenants.Single(x => x.Id == t.Id), null);
        return new BrandingService(new InMemoryDb(_db, scope), tc, _time);
    }

    private int Admin(string email = "eunhye@example.com") => InMemoryDb.SeedAccount(_db, email, "이은혜").Id;

    [Fact]
    public async Task 저장하면_바뀐_것만_기록에_남고_빈칸은_기본값()
    {
        var me = Admin();
        var (changed, errors) = await In(_kids).SaveAsync(me, "  유년부 말씀 챌린지 ", "", "green");
        Assert.True(changed);
        Assert.Empty(errors);
        var v = await In(_kids).GetAsync();
        Assert.Equal(("유년부 말씀 챌린지", (string?)null, "green"), (v.AppName, v.AppShortName, v.ThemeColor));
        var log = Assert.Single(v.Log);
        Assert.Equal("이은혜", log.Actor);
        Assert.Equal("앱 이름 기본 → 유년부 말씀 챌린지, 대표 색 파랑 → 초록", log.Detail);

        var (again, _) = await In(_kids).SaveAsync(me, "유년부 말씀 챌린지", null, "green");   // 같은 값 → 기록 없음
        Assert.False(again);
        Assert.Single((await In(_kids).GetAsync()).Log);
    }

    [Fact]
    public async Task 글자_수와_정해_둔_색을_지킨다()
    {
        var (_, errors) = await In(_kids).SaveAsync(Admin(), new string('가', 31), new string('나', 13), "pink");
        Assert.Equal(["AppName", "AppShortName", "ThemeColor"], errors.Keys.Order().ToArray());
        using var raw = InMemoryDb.Raw(_db);
        Assert.Null(raw.Tenants.Single(t => t.Id == _kids.Id).AppName);
    }

    [Fact]
    public async Task 바꾼_기록은_다른_부서_것이_보이지_않는다()
    {
        var me = Admin();
        await In(_elem).SaveAsync(me, "초등부 챌린지", null, "red");
        Assert.Empty((await In(_kids).GetAsync()).Log);
        Assert.Single((await In(_elem).GetAsync()).Log);
    }

    [Fact]
    public void 아이콘_글자는_짧은_이름_앞_2자()
    {
        Assert.Equal("도전", BrandingService.IconText("도전성경"));
        Assert.Equal("유년", BrandingService.IconText("!유년부"));
        Assert.Equal("도전", BrandingService.IconText("!!!"));
    }

    // ---- 화면 흐름 ----

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Member(string email, params string[] roles)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/signup")).Groups[1].Value,
            ["DisplayName"] = "이은혜", ["Email"] = email + "@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        int id;
        using (var raw = InMemoryDb.Raw(_db)) id = raw.Accounts.Single(a => a.Email == email + "@example.com").Id;
        InMemoryDb.SeedMembership(_db, _kids.Id, id, MembershipStatus.Approved, roles);
        return c;
    }

    [Fact]
    public async Task 관리자가_이름_색을_바꾸면_부서_화면_강조색과_제목이_바뀌고_통독은_파랑()
    {
        var admin = await Member("eunhye", Roles.TenantAdmin);
        var page = await admin.GetStringAsync("/jamsil-kids/admin/branding");
        Assert.Contains("href=\"/jamsil-kids/admin/branding\"", page);                 // 관리 메뉴 "이름 · 색"
        Assert.Contains("아직 바꾼 기록이 없어요", page);
        var r = await admin.PostAsync("/jamsil-kids/admin/branding", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value,
            ["AppName"] = "유년부 말씀 챌린지", ["AppShortName"] = "유년부", ["ThemeColor"] = "violet",
        }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var after = await admin.GetStringAsync("/jamsil-kids/admin/branding?Ok=save");
        Assert.Contains("저장했어요", after);
        Assert.Contains("대표 색 파랑 → 보라", after);

        var home = await admin.GetStringAsync("/jamsil-kids");
        Assert.Contains("data-color=\"violet\"", home);
        Assert.Contains("유년부 말씀 챌린지</title>", home);                              // 부서 화면 제목은 부서 이름(0038)
        Assert.Contains("rel=\"manifest\" href=\"/jamsil-kids/manifest.webmanifest\"", home);
        Assert.Contains("name=\"theme-color\" content=\"#6A4BB5\"", home);
        var reading = await admin.GetStringAsync("/jamsil-kids/reading");
        Assert.Contains("data-color=\"blue\"", reading);                                  // 통독 화면은 파랑 그대로
        Assert.Contains("data-color=\"blue\"", await admin.GetStringAsync("/jamsil-kids/read?from=word"));   // 성경 본문도
        Assert.Contains("data-color=\"blue\"", await admin.GetStringAsync("/account/tenants"));            // 부서 밖 화면도
    }

    [Fact]
    public async Task 관리자가_아니면_이름_색을_못_본다()
    {
        var teacher = await Member("minsu", Roles.Teacher);
        var r = await teacher.GetAsync("/jamsil-kids/admin/branding");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.StartsWith("/account/denied", r.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task 부서_매니페스트와_아이콘은_로그인_없이_열린다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            var t = raw.Tenants.Single(x => x.Id == _kids.Id);
            (t.AppName, t.AppShortName, t.ThemeColor) = ("유년부 말씀 챌린지", "유년부<", "green");
            raw.SaveChanges();
        }
        var anon = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var m = await anon.GetAsync("/jamsil-kids/manifest.webmanifest");
        Assert.Equal(HttpStatusCode.OK, m.StatusCode);
        Assert.Equal("application/manifest+json", m.Content.Headers.ContentType!.MediaType);
        var j = JsonDocument.Parse(await m.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("유년부 말씀 챌린지", j.GetProperty("name").GetString());
        Assert.Equal("유년부<", j.GetProperty("short_name").GetString());
        Assert.Equal("/jamsil-kids/", j.GetProperty("start_url").GetString());
        Assert.Equal("/jamsil-kids/", j.GetProperty("scope").GetString());
        Assert.Equal("#1F7A5C", j.GetProperty("theme_color").GetString());
        var icon = await anon.GetStringAsync("/jamsil-kids/icon.svg");
        Assert.Contains("fill=\"#1F7A5C\"", icon);
        Assert.Contains(">유년<", icon);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/no-such/manifest.webmanifest")).StatusCode);

        var elem = JsonDocument.Parse(await anon.GetStringAsync("/jamsil-elem/manifest.webmanifest")).RootElement;
        Assert.Equal(BrandingService.DefaultName, elem.GetProperty("name").GetString());   // 비워 두면 기본 이름
        Assert.Equal(BrandingService.DefaultShortName, elem.GetProperty("short_name").GetString());
    }
}
