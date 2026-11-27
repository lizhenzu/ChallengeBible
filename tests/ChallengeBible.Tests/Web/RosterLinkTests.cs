using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>
/// R-341 가입 · 명부 연결(ADR 0119, 확정 시안 가입연결-탈퇴 ① ② ③): 전화 · 생일은 선택. 가입하면 모든 부서 명부에서 같은 이름(계정 없는 줄)을
/// 가린 힌트로 보이고, 명부에 있는 정보(전화 끝 4자리 · 생일)로 본인 확인 → 그 줄과 잇고 그 부서 · 명부 역할로 바로. 5번 틀리면 막는다.
/// </summary>
public partial class RosterLinkTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids, _high, _adult;

    public RosterLinkTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        _high = InMemoryDb.SeedTenant(_db, "high", "HIGH01");
        _adult = InMemoryDb.SeedTenant(_db, "adult", "ADUL01");
        var adm = InMemoryDb.SeedAccount(_db, "kadmin@example.com", "유년관리");
        InMemoryDb.SeedMembership(_db, _kids.Id, adm.UserName!, MembershipStatus.Approved, Roles.Teacher);
        InMemoryDb.AddRole(_db, adm.Id, IdentityRoles.Admin);
        var sa = InMemoryDb.SeedAccount(_db, "superadmin@example.com", "운영자");
        InMemoryDb.AddRole(_db, sa.Id, IdentityRoles.SuperAdmin);
        using var raw = InMemoryDb.Raw(_db);
        raw.Users.Single(u => u.Id == sa.Id).UserName = "superadmin";
        raw.Tenants.Single(t => t.Id == _kids.Id).Name = "잠실새내교회 유년부";
        raw.Tenants.Single(t => t.Id == _high.Id).Name = "잠실새내교회 고등부";
        raw.Tenants.Single(t => t.Id == _adult.Id).Name = "잠실새내교회 장년부";
        raw.People.Add(new Person { TenantId = _kids.Id, Name = "홍길동", RoleCode = "T1003", Phone1 = "010-5555-1234", BirthMonth = 3, BirthDay = 15, CreatedUtc = DateTime.UtcNow });
        raw.People.Add(new Person { TenantId = _high.Id, Name = "홍길동", RoleCode = "T1001", BirthMonth = 4, BirthDay = 22, CreatedUtc = DateTime.UtcNow });
        raw.People.Add(new Person { TenantId = _adult.Id, Name = "홍길동", RoleCode = "T1001", CreatedUtc = DateTime.UtcNow });   // 확인 정보 없음
        raw.SaveChanges();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<(HttpClient C, HttpResponseMessage R)> SignUp(string name, string email, string? phone = null, int m = 0, int d = 0)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var tok = TokenRx().Match(await c.GetStringAsync("/account/signup")).Groups[1].Value;
        var r = await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tok, ["DisplayName"] = name, ["Email"] = email, ["Password"] = "1234", ["PasswordConfirm"] = "1234",
            ["Phone"] = phone ?? "", ["BMonth"] = m.ToString(), ["BDay"] = d.ToString(),
        }));
        return (c, r);
    }

    private static async Task<HttpResponseMessage> Verify(HttpClient c, int pid, string last4, int m, int d)
    {
        var tok = TokenRx().Match(await c.GetStringAsync($"/account/link/{pid}")).Groups[1].Value;
        return await c.PostAsync($"/account/link/{pid}", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = tok, ["Last4"] = last4, ["BMonth"] = m.ToString(), ["BDay"] = d.ToString() }));
    }

    private int Pid(Tenant t) { using var raw = InMemoryDb.Raw(_db); return raw.People.Where(p => p.TenantId == t.Id && p.Name == "홍길동").Min(p => p.Id); }

    [Fact]
    public async Task R341_전화_생일은_선택이고_넣으면_계정에_남고_틀린_값은_칸별로_알린다()
    {
        var (_, ok) = await SignUp("김새싹", "sprout@example.com");
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);   // 안 넣어도 가입
        var (_, bad) = await SignUp("김새싹", "sprout2@example.com", "12", 2, 31);
        var html = WebUtility.HtmlDecode(await bad.Content.ReadAsStringAsync());
        Assert.Contains("전화번호를 확인해 주세요", html);
        Assert.Contains("생일은 월과 일을 함께", html);
        Assert.Contains("더 넣기", html);

        await SignUp("이열매", "fruit@example.com", "01098765432", 7, 9);
        using var raw = InMemoryDb.Raw(_db);
        var u = raw.Users.Single(x => x.Email == "fruit@example.com");
        Assert.Equal("010-9876-5432", u.PhoneNumber);
        var s = raw.UserSettings.Single(x => x.UserName == u.UserName);
        Assert.Equal(((byte?)7, (byte?)9), (s.BirthMonth, s.BirthDay));
    }

    [Fact]
    public async Task R341_같은_이름이_있으면_가린_힌트로_후보를_보이고_정보_없는_줄은_못_고른다()
    {
        var (c, r) = await SignUp("홍길동", "gildong@example.com");
        Assert.Equal("/account/link", r.Headers.Location!.OriginalString);
        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/account/link"));
        Assert.Contains("명부에 같은 이름이 있어요", html);
        Assert.Contains("홍길동 · 유년부", html);
        Assert.Contains("전화 ●●34 · 생일 ●월 ●5일", html);
        Assert.Contains("생일 ●월 ●2일", html);
        Assert.DoesNotContain("5555", html);   // 진짜 번호 · 생일은 안 보인다
        Assert.DoesNotContain("3월", html);
        Assert.Contains("확인 정보 없음", html);
        Assert.DoesNotContain($"/account/link/{Pid(_adult)}\"", html);
        Assert.Contains("저는 다른 사람이에요 · 새로 가입할래요", html);
        Assert.Equal(HttpStatusCode.Redirect, (await c.GetAsync($"/account/link/{Pid(_adult)}")).StatusCode);   // 주소로도 못 연다

        var (_, other) = await SignUp("김다른", "other@example.com");
        Assert.NotEqual("/account/link", other.Headers.Location!.OriginalString);   // 같은 이름 없으면 그냥
    }

    [Fact]
    public async Task R341_명부_정보가_맞으면_그_줄과_잇고_명부_역할로_바로_들어가며_관리자에게_쪽지()
    {
        var (c, _) = await SignUp("홍길동", "gildong@example.com");
        var page = WebUtility.HtmlDecode(await c.GetStringAsync($"/account/link/{Pid(_kids)}"));
        Assert.Contains("전화번호 끝 4자리", page);
        Assert.Contains("생일", page);
        var r = await Verify(c, Pid(_kids), "1234", 3, 15);
        Assert.Contains("유년부에 바로 들어갔어요", WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));

        using var raw = InMemoryDb.Raw(_db);
        var me = raw.Users.Single(u => u.Email == "gildong@example.com");
        Assert.Equal(me.Id, raw.People.Single(p => p.Id == Pid(_kids)).UserId);
        Assert.Equal((_kids.Id, "T1003"), raw.DeptRoles.Where(d => d.UserId == me.Id).Select(d => new { d.TenantId, d.RoleCode }).AsEnumerable().Select(d => (d.TenantId, d.RoleCode!)).Single());
        Assert.Single(raw.People.Where(p => p.UserId == me.Id));   // 새 줄을 만들지 않았다
        var to = raw.Messages.Where(m => m.Kind == MessageKinds.System && m.Body.Contains("명부 본인 확인")).Select(m => m.RecipientUserName).ToList();
        Assert.Contains("superadmin", to);
        Assert.Contains(raw.Users.Single(u => u.Email == "kadmin@example.com").UserName, to);
    }

    [Fact]
    public async Task R341_생일만_있는_줄은_생일만_묻고_5번_틀리면_막는다()
    {
        var (c, _) = await SignUp("홍길동", "gildong@example.com");
        var page = WebUtility.HtmlDecode(await c.GetStringAsync($"/account/link/{Pid(_high)}"));
        Assert.DoesNotContain("전화번호 끝 4자리", page);
        for (var i = 1; i <= 4; i++)
            Assert.Contains($"{5 - i}번 더", WebUtility.HtmlDecode(await (await Verify(c, Pid(_high), "", 1, 1)).Content.ReadAsStringAsync()));
        Assert.Contains("5번 틀려서", WebUtility.HtmlDecode(await (await Verify(c, Pid(_high), "", 1, 1)).Content.ReadAsStringAsync()));
        Assert.Contains("5번 틀려서", WebUtility.HtmlDecode(await (await Verify(c, Pid(_high), "", 4, 22)).Content.ReadAsStringAsync()));   // 막힌 동안엔 맞아도 안 된다
        using var raw = InMemoryDb.Raw(_db);
        Assert.Null(raw.People.Single(p => p.Id == Pid(_high)).UserId);
    }

    [Fact]
    public async Task R341_다른_사람이면_보통처럼_참여하고_가입_때_넣은_전화_생일이_새_명부_줄에()
    {
        var (c, _) = await SignUp("홍길동", "gd2@example.com", "010-2222-3333", 12, 25);
        using (var raw = InMemoryDb.Raw(_db)) { foreach (var t in raw.Tenants) t.JoinListed = true; raw.SaveChanges(); }
        var tok = TokenRx().Match(await c.GetStringAsync("/join")).Groups[1].Value;
        await c.PostAsync("/join?handler=Apply", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = tok, ["TenantId"] = _kids.Id.ToString(), ["RoleKind"] = SignupRoleKind.Student }));
        using var after = InMemoryDb.Raw(_db);
        var me = after.Users.Single(u => u.Email == "gd2@example.com");
        var mine = after.People.Single(p => p.UserId == me.Id);
        Assert.NotEqual(Pid(_kids), mine.Id);   // 명부 줄과 잇지 않고 새 줄
        Assert.Equal(("010-2222-3333", (byte?)12, (byte?)25), (mine.Phone1, mine.BirthMonth, mine.BirthDay));
    }
}
