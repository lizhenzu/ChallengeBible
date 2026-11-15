using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>
/// 여러 부서 한 번에 만들기(확정 시안 플랫폼-부서-여러개만들기, 2026-11-15 사용자): 줄마다 [+ 새 부서 만들기]와 같고(참여 코드 · 역할 코드 18),
/// 한 줄이라도 틀리면 하나도 만들지 않는다. 처음에는 아직 없는 영아부 · 유아부 · 유치부를 채워 둔다. 플랫폼 관리자만.
/// </summary>
public partial class BulkTenantTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();

    public BulkTenantTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));
        var kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        var op = InMemoryDb.SeedAccount(_db, "operator@example.com", "운영자");
        InMemoryDb.AddRole(_db, op.Id, IdentityRoles.SuperAdmin);
        var adm = InMemoryDb.SeedAccount(_db, "admin@example.com", "윤동호");
        InMemoryDb.SeedMembership(_db, kids.Id, adm.UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        using var raw = InMemoryDb.Raw(_db);
        foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        raw.SaveChanges();
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

    private static async Task<HttpResponseMessage> Bulk(HttpClient c, params (string Name, string Slug, string Color)[] rows)
    {
        var form = new List<KeyValuePair<string, string>> { new("__RequestVerificationToken", TokenRx().Match(await c.GetStringAsync("/platform/tenants")).Groups[1].Value) };
        foreach (var r in rows) { form.Add(new("BulkName", r.Name)); form.Add(new("BulkSlug", r.Slug)); form.Add(new("BulkColor", r.Color)); }
        return await c.PostAsync("/platform/tenants?handler=Bulk", new FormUrlEncodedContent(form));
    }

    private List<Tenant> Tenants() { using var raw = InMemoryDb.Raw(_db); return raw.Tenants.OrderBy(t => t.Id).ToList(); }

    [Fact]
    public async Task 처음에는_아직_없는_영아부_유아부_유치부를_채워_두고_한_번에_만든다()
    {
        var op = await Login("operator@example.com");
        var page = await op.GetStringAsync("/platform/tenants");
        Assert.Contains("+ 여러 부서 한 번에 만들기", page);
        Assert.Contains("value=\"jamsil-infant\"", page);
        Assert.Contains("3곳 한 번에 만들기", page);

        var res = await Bulk(op, ("잠실새내교회 영아부", "jamsil-infant", "green"), ("잠실새내교회 유아부", "jamsil-toddler", "red"), ("잠실새내교회 유치부", "jamsil-kindergarten", "violet"), ("", "", "blue"));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var made = Tenants().Skip(1).ToList();
        Assert.Equal(["jamsil-infant", "jamsil-toddler", "jamsil-kindergarten"], made.Select(t => t.Slug).ToArray());   // 빈 줄은 건너뜀
        Assert.All(made, t => Assert.True(t.MissionEnabled && t.ReadingEnabled && t.TimeZoneId == "Asia/Seoul" && t.JoinCode.Length == 6));
        Assert.Equal(3, made.Select(t => t.JoinCode).Distinct().Count());
        Assert.Equal("violet", made[2].ThemeColor);
        using (var raw = InMemoryDb.Raw(_db)) Assert.All(made, t => Assert.Equal(18, raw.CodeData.Count(c => c.TenantId == t.Id && c.MajorCD == CodeData.RoleMajor)));

        var after = await op.GetStringAsync(res.Headers.Location!.OriginalString);
        Assert.Contains("3곳을 만들었어요", after);
        Assert.DoesNotContain("value=\"jamsil-infant\"", after);   // 다 있으니 빈 줄 하나만
        Assert.Contains("1곳 한 번에 만들기", after);
    }

    [Fact]
    public async Task 한_줄이라도_틀리면_하나도_만들지_않고_그_줄을_알려_준다()
    {
        var op = await Login("operator@example.com");
        var body = await (await Bulk(op, ("잠실새내교회 영아부", "jamsil-infant", "green"), ("겹친 주소", "jamsil-kids", "red"), ("같은 줄", "jamsil-infant", "blue"), ("", "Bad Slug", "blue"))).Content.ReadAsStringAsync();
        Assert.Single(Tenants());   // 하나도 안 만듦
        Assert.Contains("이미 쓰고 있는 주소예요.", body);
        Assert.Contains("위 줄과 주소가 같아요.", body);
        Assert.Contains("부서 이름을 1 ~ 100자로 써 주세요.", body);
        Assert.Contains("bk-row bad", body);
    }

    [Fact]
    public async Task 플랫폼_관리자만()
    {
        var admin = await Login("admin@example.com");   // 부서 관리자
        Assert.NotEqual(HttpStatusCode.OK, (await admin.GetAsync("/platform/tenants")).StatusCode);
        var res = await admin.PostAsync("/platform/tenants?handler=Bulk", new FormUrlEncodedContent(new Dictionary<string, string> { ["BulkName"] = "x", ["BulkSlug"] = "xx", ["BulkColor"] = "blue" }));
        Assert.NotEqual(HttpStatusCode.OK, res.StatusCode);
        Assert.Single(Tenants());
    }
}
