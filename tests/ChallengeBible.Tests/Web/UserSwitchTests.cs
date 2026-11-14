using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>
/// 사람 바꾸기(ADR 0096, 확정 시안 사람바꾸기): 가장 중인 플랫폼 관리자가 로그아웃 없이 모든 계정으로 바꾼다.
/// 운영자 = 플랫폼 관리자, 박지은 = 유년부 관리자(교사), 송시온 = 유년부 학생, 김도윤 = 초등부 학생, 정지됨 = 정지 계정.
/// </summary>
public partial class UserSwitchTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids, _elem;
    private readonly string _op, _op2, _jieun, _sion, _doyun, _stopped;

    public UserSwitchTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        _op = InMemoryDb.SeedAccount(_db, "operator@example.com", "운영자").UserName!;
        _op2 = InMemoryDb.SeedAccount(_db, "operator2@example.com", "운영자2").UserName!;
        _jieun = InMemoryDb.SeedAccount(_db, "jieun@example.com", "박지은").UserName!;
        _sion = InMemoryDb.SeedAccount(_db, "sion@example.com", "송시온").UserName!;
        _doyun = InMemoryDb.SeedAccount(_db, "doyun@example.com", "김도윤").UserName!;
        _stopped = InMemoryDb.SeedAccount(_db, "stop@example.com", "정지됨").UserName!;
        InMemoryDb.SeedMembership(_db, _kids.Id, _jieun, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        InMemoryDb.SeedMembership(_db, _kids.Id, _sion, MembershipStatus.Approved, Roles.Participant);
        InMemoryDb.SeedMembership(_db, _kids.Id, _stopped, MembershipStatus.Approved, Roles.Participant);
        InMemoryDb.SeedMembership(_db, _elem.Id, _doyun, MembershipStatus.Approved, Roles.Participant);
        using var raw = InMemoryDb.Raw(_db);
        foreach (var a in raw.Users)
        {
            a.PasswordHash = new PasswordHasher<AppUser>().HashPassword(a, "1234");
            if (a.UserName == _op || a.UserName == _op2) InMemoryDb.AddRole(_db, a.Id, IdentityRoles.SuperAdmin);
            if (a.UserName == _stopped) a.LockoutEnd = DateTimeOffset.MaxValue;
        }
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

    private static async Task<HttpResponseMessage> Post(HttpClient c, string url, string tokenFrom, params (string, string)[] form)
    {
        var token = TokenRx().Match(await c.GetStringAsync(tokenFrom)).Groups[1].Value;
        return await c.PostAsync(url, new FormUrlEncodedContent(form.Append(("__RequestVerificationToken", token)).Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2))));
    }

    /// <summary>운영자로 로그인해 박지은으로 가장한 상태.</summary>
    private async Task<HttpClient> AsJieun()
    {
        var c = await Login("operator@example.com");
        await Post(c, $"/platform/accounts?handler=Impersonate&id={_jieun}", $"/platform/accounts?id={_jieun}&askImp=true");
        return c;
    }

    private static string Who(string html) => Regex.Match(html, "<b>가장 중</b> · (.+?)님으로").Groups[1].Value;

    [Fact]
    public async Task 가장_중이_아니면_바꾸기_화면은_없다()
    {
        var c = await Login("operator@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/account/impersonate/switch")).StatusCode);
        Assert.DoesNotContain("data-imp-switch", await c.GetStringAsync("/platform"));
        var kid = await Login("sion@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await kid.GetAsync("/account/impersonate/switch")).StatusCode);
    }

    [Fact]
    public async Task 가장_띠에_사람_바꾸기가_있고_목록은_지금_부서의_사람과_역할()
    {
        var c = await AsJieun();
        var home = await c.GetStringAsync("/jamsil-kids/admin");
        Assert.Contains("data-imp-switch", home);
        Assert.Contains("/account/impersonate/switch?back=%2Fjamsil-kids%2Fadmin", home);

        var list = await c.GetStringAsync("/account/impersonate/switch?frag=true");
        Assert.DoesNotContain("<html", list);   // 창 안 내용만
        Assert.Contains("송시온", list);
        Assert.Contains("박지은", list);
        Assert.DoesNotContain("김도윤", list);   // 다른 부서는 칩을 눌러야
        Assert.Contains("<i class=\"sw-t ad\">관리자</i>", list);
        Assert.Contains("<i class=\"sw-t st\">학생</i>", list);
        Assert.Contains("들어갈 수 없음", list);   // 정지된 계정

        var students = await c.GetStringAsync("/account/impersonate/switch?frag=true&role=st");
        Assert.Contains("송시온", students);
        Assert.DoesNotContain(">박지은<", students);

        var elem = await c.GetStringAsync($"/account/impersonate/switch?frag=true&dept={_elem.Id}");
        Assert.Contains("김도윤", elem);
    }

    [Fact]
    public async Task 이름으로_찾으면_모든_부서에서_찾는다()
    {
        var c = await AsJieun();
        var found = await c.GetStringAsync("/account/impersonate/switch?frag=true&q=" + Uri.EscapeDataString("김"));
        Assert.Contains("김도윤", found);
        Assert.Contains("<em>", found);   // 부서 이름을 함께
        Assert.DoesNotContain("송시온", found);
    }

    [Fact]
    public async Task 바꾸면_로그아웃_없이_그_사람이_되고_감사_로그와_최근에_본_사람에_남는다()
    {
        var c = await AsJieun();
        var go = await Post(c, "/account/impersonate/switch", "/account/impersonate/switch", ("to", _sion), ("back", "/jamsil-kids/word-mission"));
        Assert.Equal(HttpStatusCode.Redirect, go.StatusCode);
        Assert.Equal("/jamsil-kids/word-mission", go.Headers.Location!.OriginalString);   // 같은 부서 화면 그대로
        Assert.Equal("송시온", Who(await c.GetStringAsync("/jamsil-kids")));

        // 다른 부서 사람이면 그 부서 홈으로
        var go2 = await Post(c, "/account/impersonate/switch", "/account/impersonate/switch", ("to", _doyun), ("back", "/jamsil-kids/word-mission"));
        Assert.Equal("/jamsil-elem", go2.Headers.Location!.OriginalString);
        Assert.Equal("김도윤", Who(await c.GetStringAsync("/jamsil-elem")));

        using (var raw = InMemoryDb.Raw(_db))
        {
            var logs = raw.AuditLogs.Where(a => a.Action == UserSwitchService.SwitchAction).OrderBy(a => a.Id).ToList();
            Assert.Equal(2, logs.Count);
            Assert.Equal(($"from={_jieun};account={_sion}", _op), (logs[0].Detail!, logs[0].ImpersonatorUserName!));
        }
        var list = await c.GetStringAsync("/account/impersonate/switch?frag=true");
        Assert.Contains("최근에 본 사람", list);
        Assert.Contains("송시온", list);

        // 복귀하면 운영자
        var back = await Post(c, "/account/impersonate/stop", "/jamsil-elem");
        Assert.Equal(HttpStatusCode.Redirect, back.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/platform")).StatusCode);
    }

    [Fact]
    public async Task 플랫폼_관리자_정지된_계정_자신으로는_바꿀_수_없다()
    {
        var c = await AsJieun();
        foreach (var to in new[] { _op2, _stopped, _op })
        {
            var r = await Post(c, "/account/impersonate/switch", "/account/impersonate/switch", ("to", to));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);   // 오류 문구와 함께 목록 그대로
            Assert.Contains("sw-err", await r.Content.ReadAsStringAsync());
        }
        Assert.Equal("박지은", Who(await c.GetStringAsync("/jamsil-kids")));
    }

    [Fact]
    public async Task 가장한_관리자가_플랫폼_관리자가_아니게_되면_바꿀_수_없다()
    {
        var c = await AsJieun();
        var token = TokenRx().Match(await c.GetStringAsync("/account/impersonate/switch")).Groups[1].Value;
        using (var raw = InMemoryDb.Raw(_db))
        {
            var id = raw.Users.Single(u => u.UserName == _op).Id;
            raw.UserRoles.RemoveRange(raw.UserRoles.Where(r => r.UserId == id));
            raw.SaveChanges();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/account/impersonate/switch")).StatusCode);
        var r = await c.PostAsync("/account/impersonate/switch", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["to"] = _sion }));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        using var check = InMemoryDb.Raw(_db);
        Assert.DoesNotContain(check.AuditLogs, a => a.Action == UserSwitchService.SwitchAction);
    }

    [Theory]
    [InlineData("/jamsil-kids/gifts?all=true", "jamsil-kids", "/jamsil-kids/gifts?all=true")]
    [InlineData("/jamsil-kids/gifts", "jamsil-elem", "/jamsil-elem")]
    [InlineData("/platform", "jamsil-kids", "/jamsil-kids")]
    [InlineData("//evil.example", "jamsil-kids", "/jamsil-kids")]
    [InlineData(null, null, "/")]
    public void 바꾼_뒤_갈_곳(string? back, string? slug, string next) => Assert.Equal(next, UserSwitchService.Next(back, slug));
}
