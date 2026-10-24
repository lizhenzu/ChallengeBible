using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>진급 화면 흐름: 소속 관리에서 보내기 → 받는 부서 진급 화면에서 승인(0026 · 0030 · 0058, R-139).</summary>
public partial class PromotionFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids, _elem;

    public PromotionFlowTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private static async Task<string> Token(HttpClient c, string url) => TokenRx().Match(await c.GetStringAsync(url)).Groups[1].Value;

    private async Task<(HttpClient Client, int AccountId)> SignedUp(string email, string name)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await Token(c, "/account/signup"),
            ["DisplayName"] = name, ["Email"] = email + "@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        using var raw = InMemoryDb.Raw(_db);
        return (c, raw.Accounts.Single(a => a.Email == email + "@example.com").Id);
    }

    private static Task<HttpResponseMessage> Post(HttpClient c, string url, string token, params (string, string)[] form) =>
        c.PostAsync(url, new FormUrlEncodedContent(form.Append(("__RequestVerificationToken", token)).Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2))));

    [Fact]
    public async Task 보내고_받는_부서에서_승인하면_옮겨진다()
    {
        var (kidsAdmin, kidsAdminId) = await SignedUp("eunhye", "이은혜");
        var (elemAdmin, elemAdminId) = await SignedUp("jieun", "박지은");
        var (_, kidId) = await SignedUp("cheolsu", "김철수");
        InMemoryDb.SeedMembership(_db, _kids.Id, kidsAdminId, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        InMemoryDb.SeedMembership(_db, _elem.Id, elemAdminId, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        var kid = InMemoryDb.SeedMembership(_db, _kids.Id, kidId, MembershipStatus.Approved, Roles.Participant);

        // 보내기: 고르고 → 부서 확인(이름만) → 보내기
        var members = "/jamsil-kids/admin/members?Tab=Approved";
        var html = await kidsAdmin.GetStringAsync(members);
        Assert.Contains("진급 보내기", html);
        var token = TokenRx().Match(html).Groups[1].Value;
        var check = await Post(kidsAdmin, "/jamsil-kids/admin/members?handler=PromoteCheck&Tab=Approved", token, ("pick", kid.Id.ToString()), ("code", "jselem"));
        var checkHtml = await check.Content.ReadAsStringAsync();
        Assert.Contains("받는 부서: jamsil-elem 부서", checkHtml);
        Assert.Contains("1명 진급 요청 보내기", checkHtml);
        var sent = await Post(kidsAdmin, "/jamsil-kids/admin/members?handler=Promote&Tab=Approved", token, ("pick", kid.Id.ToString()), ("code", "JSELEM"));
        Assert.Equal("/jamsil-kids/admin/promotions?Tab=out&Ok=sent", sent.Headers.Location!.OriginalString);
        Assert.Contains("기다리는 중", await kidsAdmin.GetStringAsync("/jamsil-kids/admin/promotions?Tab=out"));

        // 받는 부서: 메뉴 숫자 · 관리 홈 카드 · 받은 요청
        Assert.Contains("받은 진급 요청", await elemAdmin.GetStringAsync("/jamsil-elem/admin"));
        var inbox = await elemAdmin.GetStringAsync("/jamsil-elem/admin/promotions");
        Assert.Contains("cheolsu@example.com", inbox);
        Assert.Contains("기다리는 요청 1건", inbox);
        int reqId;
        using (var raw = InMemoryDb.Raw(_db)) reqId = raw.PromotionRequests.Single().Id;
        var ok = await Post(elemAdmin, "/jamsil-elem/admin/promotions?handler=Approve&Tab=in", TokenRx().Match(inbox).Groups[1].Value,
            ("id", reqId.ToString()), ("roles", Roles.Participant), ("group", "사랑반"));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Contains("김철수", await elemAdmin.GetStringAsync("/jamsil-elem/admin/members?Tab=Approved"));

        using var after = InMemoryDb.Raw(_db);
        Assert.False(after.Memberships.Any(m => m.Id == kid.Id));
        Assert.True(after.Memberships.Any(m => m.TenantId == _elem.Id && m.AccountId == kidId && m.GroupLabel == "사랑반"));
    }

    [Fact]
    public async Task 다른_부서의_요청은_승인할_수_없고_관리자가_아니면_못_본다()
    {
        var (kidsAdmin, kidsAdminId) = await SignedUp("eunhye", "이은혜");
        var (kidC, kidId) = await SignedUp("cheolsu", "김철수");
        InMemoryDb.SeedMembership(_db, _kids.Id, kidsAdminId, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        InMemoryDb.SeedMembership(_db, _kids.Id, kidId, MembershipStatus.Approved, Roles.Participant);
        var other = InMemoryDb.SeedTenant(_db, "other-kids", "OTHERK");
        var someone = InMemoryDb.SeedAccount(_db, "x@example.com", "오서준");
        InMemoryDb.SeedMembership(_db, other.Id, someone.Id, MembershipStatus.Approved, Roles.Participant);
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.PromotionRequests.Add(new PromotionRequest { AccountId = someone.Id, SourceTenantId = other.Id, TargetTenantId = _elem.Id, RequestedByAccountId = someone.Id });
            raw.SaveChanges();
        }
        int reqId;
        using (var raw = InMemoryDb.Raw(_db)) reqId = raw.PromotionRequests.Single().Id;

        var html = await kidsAdmin.GetStringAsync("/jamsil-kids/admin/promotions");
        Assert.DoesNotContain("오서준", html);
        var r = await Post(kidsAdmin, "/jamsil-kids/admin/promotions?handler=Approve&Tab=in", await Token(kidsAdmin, "/jamsil-kids/admin/members?Tab=Approved"),
            ("id", reqId.ToString()), ("roles", Roles.Participant));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);   // 같은 화면에 "찾을 수 없어요"
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(PromotionStatus.Pending, raw.PromotionRequests.Single().Status);

        var denied = await kidC.GetAsync("/jamsil-kids/admin/promotions");
        Assert.StartsWith("/account/denied", denied.Headers.Location!.PathAndQuery);
    }
}
