using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>R-364 부서 관리자 로그인: 첫 화면 버튼(나이순 · 영아부 ~ 청년부 · 관리자 계정이 있는 부서만) → 비밀번호만 넣고 그 부서 홈으로.</summary>
public partial class DeptAdminLoginTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();

    public DeptAdminLoginTests(WebApplicationFactory<Program> factory)
    {
        _app = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));
        // 일부러 나이순이 아닌 차례로 만든다
        Dept("jamsil-highschool", "HS0001", "잠실새내교회 고등부", admin: true);
        Dept("jamsil-kids", "KD0001", "잠실새내교회 유년부", admin: true);
        Dept("jamsil-infant", "IN0001", "잠실새내교회 영아부", admin: true);
        Dept("jamsil-youth", "YT0001", "잠실새내교회 청년부", admin: true);
        Dept("jamsil-adult", "AD0001", "잠실새내교회 장년부", admin: true);    // 장년부는 버튼 없음
        Dept("jamsil-toddler", "TD0001", "잠실새내교회 유아부", admin: false);  // 관리자 계정이 없으면 버튼 없음
    }

    private void Dept(string slug, string code, string name, bool admin)
    {
        var t = InMemoryDb.SeedTenant(_db, slug, code);
        using (var raw = InMemoryDb.Raw(_db)) { raw.Tenants.Single(x => x.Id == t.Id).Name = name; raw.SaveChanges(); }
        if (!admin) return;
        var u = InMemoryDb.SeedAccount(_db, $"{slug}-admin@fake-seed.test", "관리자");
        using (var raw = InMemoryDb.Raw(_db))
        {
            var row = raw.Users.Single(x => x.Id == u.Id);
            (row.UserName, row.NormalizedUserName) = (slug + "-admin", AppUser.Normalize(slug + "-admin"));
            row.PasswordHash = new PasswordHasher<AppUser>().HashPassword(row, "pw-1234");
            raw.DeptRoles.Add(new DeptRoleUserAssoc { TenantId = t.Id, UserId = row.Id, RoleCode = "T1030" });
            raw.SaveChanges();
        }
        InMemoryDb.AddRole(_db, u.Id, IdentityRoles.Admin);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private HttpClient Client() => _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<HttpResponseMessage> Post(HttpClient c, string url, string password)
    {
        var tok = TokenRx().Match(await c.GetStringAsync(url)).Groups[1].Value;
        return await c.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = tok, ["Password"] = password }));
    }

    [Fact]
    public async Task R364_첫_화면에_관리자_계정이_있는_부서만_나이순으로_영아부부터_청년부까지_버튼이_있다()
    {
        var html = await Client().GetStringAsync("/");
        Assert.Contains("부서 관리자 로그인", html);
        var labels = Regex.Matches(html, "class=\"dl-btn [^\"]*\" href=\"/account/dept-login/([^\"]+)\">([^<]+)</a>").Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToArray();
        Assert.Equal(new[]
        {
            ("jamsil-infant", "영아부 관리자"), ("jamsil-kids", "유년부 관리자"), ("jamsil-highschool", "고등부 관리자"), ("jamsil-youth", "청년부 관리자"),
        }, labels);
        Assert.DoesNotContain("-admin", html);   // 아이디는 화면에 쓰지 않는다
    }

    [Fact]
    public async Task R364_비밀번호만_넣으면_그_부서_홈으로_가고_틀리면_다시_묻는다()
    {
        var c = Client();
        var page = await c.GetStringAsync("/account/dept-login/jamsil-kids");
        Assert.Contains("유년부 관리자", page);
        Assert.DoesNotContain("jamsil-kids-admin", page);

        var wrong = await Post(c, "/account/dept-login/jamsil-kids", "nope");
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("비밀번호가 맞지 않아요", await wrong.Content.ReadAsStringAsync());

        var ok = await Post(c, "/account/dept-login/jamsil-kids", "pw-1234");
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Equal("/jamsil-kids", ok.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task R364_버튼이_없는_부서는_404()
    {
        var c = Client();
        foreach (var slug in new[] { "jamsil-adult", "jamsil-toddler", "nowhere" })
        {
            var r = await c.GetAsync("/account/dept-login/" + slug);
            Assert.True(r.StatusCode == HttpStatusCode.NotFound, $"{slug}: {(int)r.StatusCode} {r.Headers.Location}");
        }
    }
}
