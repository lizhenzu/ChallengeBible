using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>부서 주소 · 권한 · 승인 흐름(R-130, 0045, API 계약 1-2).</summary>
public partial class AdminFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;

    public AdminFlowTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
    }

    private HttpClient Client() => _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private static async Task<string> Token(HttpClient c, string url) => TokenRx().Match(await c.GetStringAsync(url)).Groups[1].Value;

    /// <summary>가입 화면으로 계정을 만들고 로그인된 클라이언트를 돌려준다.</summary>
    private async Task<(HttpClient Client, int AccountId)> SignedUp(string loginId, string name)
    {
        var c = Client();
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await Token(c, "/account/signup"),
            ["DisplayName"] = name, ["LoginId"] = loginId, ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        using var raw = InMemoryDb.Raw(_db);
        return (c, raw.Accounts.Single(a => a.LoginId == loginId).Id);
    }

    [Fact]
    public async Task 없는_부서_주소는_404()
    {
        var (c, _) = await SignedUp("nobody", "아무개");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/no-such-dept")).StatusCode);
    }

    [Fact]
    public async Task 소속이_없으면_그_부서_코드가_채워진_참여_신청으로_R130()
    {
        var (c, _) = await SignedUp("cheolsu", "김철수");
        var r = await c.GetAsync("/jamsil-kids");
        Assert.Equal("/join?code=JSKIDS", r.Headers.Location!.OriginalString);
        Assert.Contains("잠실새내교회 유년부", await c.GetStringAsync("/join?code=JSKIDS"));
    }

    [Fact]
    public async Task 관리자가_아니면_소속_관리를_못_본다()
    {
        var (c, id) = await SignedUp("cheolsu", "김철수");
        InMemoryDb.SeedMembership(_db, _kids.Id, id, MembershipStatus.Approved, Roles.Participant);
        var r = await c.GetAsync("/jamsil-kids/admin/members");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.StartsWith("/account/denied", r.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task 관리자가_승인하면_아이는_로그인_뒤_바로_부서로_0045()
    {
        var (admin, adminId) = await SignedUp("eunhye", "이은혜");
        InMemoryDb.SeedMembership(_db, _kids.Id, adminId, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        var (kid, kidId) = await SignedUp("cheolsu", "김철수");
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, kidId, MembershipStatus.Requested, Roles.Participant);

        var list = await admin.GetStringAsync("/jamsil-kids/admin/members");
        Assert.Contains("김철수", list);
        Assert.Contains("참여자 희망", list);

        var approve = await admin.PostAsync("/jamsil-kids/admin/members?handler=Approve&Tab=Requested", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", TokenRx().Match(list).Groups[1].Value),
            new("id", m.Id.ToString()),
            new("roles", Roles.Participant),
            new("group", "사랑반"),
        }));
        Assert.Equal(HttpStatusCode.Redirect, approve.StatusCode);
        Assert.Contains("Ok=approve", approve.Headers.Location!.OriginalString);

        var home = await kid.GetAsync("/");
        Assert.Equal("/jamsil-kids", home.Headers.Location!.OriginalString);
        Assert.Contains("잠실새내교회 유년부", await kid.GetStringAsync("/jamsil-kids"));
    }
}
