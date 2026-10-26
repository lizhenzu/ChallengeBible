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

/// <summary>플랫폼 관리자 가장(ADR 0067, 확정 시안 플랫폼-가장). 운영자 = 플랫폼 관리자, 박지은 = 유년부 관리자, 운영자2 = 다른 플랫폼 관리자.</summary>
public partial class ImpersonationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;
    private readonly int _op, _op2, _jieun, _stopped;

    public ImpersonationTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _op = InMemoryDb.SeedAccount(_db, "operator@example.com", "운영자").Id;
        _op2 = InMemoryDb.SeedAccount(_db, "operator2@example.com", "운영자2").Id;
        _jieun = InMemoryDb.SeedAccount(_db, "jieun@example.com", "박지은").Id;
        _stopped = InMemoryDb.SeedAccount(_db, "stop@example.com", "정지됨").Id;
        InMemoryDb.SeedMembership(_db, _kids.Id, _jieun, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        using var raw = InMemoryDb.Raw(_db);
        foreach (var a in raw.Accounts)
        {
            a.PasswordHash = new PasswordHasher<Account>().HashPassword(a, "1234");
            a.IsPlatformAdmin = a.Id == _op || a.Id == _op2;
            if (a.Id == _stopped) a.Status = "Disabled";
            if (a.Id == _jieun) a.MustChangePassword = true;   // 가장할 때는 남의 비밀번호 바꾸기로 보내지 않는다
        }
        raw.SaveChanges();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Login(string email)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await c.GetStringAsync("/account/login");
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(html).Groups[1].Value, ["Email"] = email, ["Password"] = "1234",
        }));
        return c;
    }

    private static async Task<HttpResponseMessage> Post(HttpClient c, string url, string tokenFrom, params (string, string)[] form)
    {
        var token = TokenRx().Match(await c.GetStringAsync(tokenFrom)).Groups[1].Value;
        return await c.PostAsync(url, new FormUrlEncodedContent(form.Append(("__RequestVerificationToken", token)).Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2))));
    }

    [Fact]
    public async Task 계정_찾기에서_확인_뒤_들어가고_복귀하면_플랫폼_관리자로_돌아온다()
    {
        var c = await Login("operator@example.com");
        var detail = await c.GetStringAsync($"/platform/accounts?q=jieun&id={_jieun}");
        Assert.Contains("이 계정으로 들어가기", detail);
        Assert.Contains("박지은님으로 들어갈까요?", await c.GetStringAsync($"/platform/accounts?q=jieun&id={_jieun}&askImp=true"));

        var go = await Post(c, $"/platform/accounts?handler=Impersonate&q=jieun&id={_jieun}", $"/platform/accounts?q=jieun&id={_jieun}&askImp=true");
        Assert.Equal("/", go.Headers.Location!.OriginalString);

        // 박지은 그대로: 부서 관리 화면이 열리고, 맨 위에 가장 중 띠, 플랫폼 화면은 막힘, 비밀번호 바꾸기로 끌려가지 않음
        var home = await c.GetStringAsync("/jamsil-kids");
        Assert.Contains("가장 중", home);
        Assert.Contains("박지은님으로 보고 있어요", home);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/jamsil-kids/admin")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await c.GetAsync("/platform")).StatusCode);

        // 이름 · 비밀번호 바꾸기는 막힘
        var name = await Post(c, "/account?handler=Name", "/account", ("DisplayName", "바뀐 이름"));
        Assert.DoesNotContain("Ok=name", name.Headers.Location?.OriginalString ?? "");
        Assert.Equal("박지은", InMemoryDb.Raw(_db).Accounts.Single(a => a.Id == _jieun).DisplayName);

        // 복귀
        var back = await Post(c, "/account/impersonate/stop", "/jamsil-kids");
        Assert.Equal($"/platform/accounts?id={_jieun}", back.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/platform")).StatusCode);
        Assert.DoesNotContain("가장 중", await c.GetStringAsync("/platform"));

        using var raw = InMemoryDb.Raw(_db);
        var logs = raw.AuditLogs.Where(l => l.Action.StartsWith("Impersonate")).OrderBy(l => l.Id).ToList();
        Assert.Equal([ImpersonationService.StartAction, ImpersonationService.StopAction], logs.Select(l => l.Action));
        Assert.All(logs, l => Assert.Equal((_op, (int?)_op), (l.ActorAccountId, l.ImpersonatorAccountId)));
        Assert.All(logs, l => Assert.Equal($"account={_jieun}", l.Detail));
    }

    [Fact]
    public async Task 플랫폼_관리자_정지된_계정_나_자신은_가장할_수_없고_일반_계정은_시작할_수_없다()
    {
        var svc = new ImpersonationService(new InMemorySystemDb(_db), TimeProvider.System);
        Assert.Equal(ImpersonateError.PlatformAdmin, (await svc.StartAsync(_op, _op2)).Error);
        Assert.Equal(ImpersonateError.Suspended, (await svc.StartAsync(_op, _stopped)).Error);
        Assert.Equal(ImpersonateError.Self, (await svc.StartAsync(_op, _op)).Error);
        Assert.Equal(ImpersonateError.NotAdmin, (await svc.StartAsync(_jieun, _stopped)).Error);
        Assert.Empty(InMemoryDb.Raw(_db).AuditLogs);

        var c = await Login("operator@example.com");
        Assert.Contains("플랫폼 관리자 계정으로는 들어갈 수 없어요", await c.GetStringAsync($"/platform/accounts?q=operator&id={_op2}"));
    }

    [Fact]
    public async Task 가장_중_남는_감사_로그에는_가장한_관리자가_함께_적힌다()
    {
        AuditContext.Impersonator = _op;
        try
        {
            using (var ctx = InMemoryDb.Raw(_db))
            {
                ctx.AuditLogs.Add(new AuditLog { OccurredUtc = DateTime.UtcNow, ActorAccountId = _jieun, Action = "SettingChange" });
                await ctx.SaveChangesAsync();
            }
            AuditContext.Impersonator = null;
            using (var ctx = InMemoryDb.Raw(_db))
            {
                ctx.AuditLogs.Add(new AuditLog { OccurredUtc = DateTime.UtcNow, ActorAccountId = _jieun, Action = "SettingChange" });
                ctx.SaveChanges();
            }
        }
        finally { AuditContext.Impersonator = null; }
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal([(int?)_op, null], raw.AuditLogs.OrderBy(l => l.Id).Select(l => l.ImpersonatorAccountId).ToList());
    }
}
