using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>운영 점검(확정 시안: 플랫폼-운영점검): 최근 오류(메모리 50건 · 개인 내용 없음) · 서버 상태 · 감사 로그. 플랫폼 관리자만.</summary>
public partial class OpsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();

    public OpsTests(WebApplicationFactory<Program> app) =>
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));

    private static HttpContext Http(string path, string query = "")
    {
        var h = new DefaultHttpContext();
        h.Request.Method = "POST";
        h.Request.Path = path;
        h.Request.QueryString = new QueryString(query);
        h.TraceIdentifier = "trace-1";
        return h;
    }

    private static Exception Thrown(string message)
    {
        try { throw new InvalidOperationException(message + "\n둘째 줄은 남기지 않음"); }
        catch (Exception ex) { return ex; }
    }

    [Fact]
    public void 오류는_묶어서_세고_물음표_뒤와_둘째_줄은_남기지_않으며_50건까지만()
    {
        var log = new ErrorLog(TimeProvider.System);
        log.Record(Http("/jamsil-kids/admin", "?email=secret@example.com"), Thrown("첫 줄"));
        log.Record(Http("/jamsil-kids/admin"), Thrown("첫 줄"));
        log.Record(Http("/x"), Thrown("다른 오류"));
        var g = log.Groups();
        Assert.Equal(2, g.Count);
        var same = g.Single(x => x.Message == "첫 줄");
        Assert.Equal((2, "POST /jamsil-kids/admin", "trace-1"), (same.Count, same.LastUrl, same.LastTraceId));
        Assert.DoesNotContain("secret", same.LastUrl + same.Stack);
        Assert.DoesNotContain("둘째 줄", same.Message + same.Stack);
        Assert.Contains("InvalidOperationException", same.Type);

        for (var i = 0; i < 60; i++) log.Record(Http("/y"), Thrown($"오류 {i}"));
        Assert.Equal(ErrorLog.Capacity, log.Groups().Sum(x => x.Count));   // 오래된 것부터 버림

        var aborted = Http("/z");
        var cts = new CancellationTokenSource(); cts.Cancel();
        aborted.RequestAborted = cts.Token;
        log.Record(aborted, new OperationCanceledException());   // 사용자가 떠난 것은 오류가 아님
        Assert.DoesNotContain(log.Groups(), x => x.LastUrl == "POST /z");
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> LoggedIn(string email, bool platformAdmin)
    {
        var a = InMemoryDb.SeedAccount(_db, email, email[..email.IndexOf('@')]);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var row = raw.Accounts.Single(x => x.Id == a.Id);
            row.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<Account>().HashPassword(row, "1234");
            row.IsPlatformAdmin = platformAdmin;
            raw.SaveChanges();
        }
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var h = await c.GetStringAsync("/account/login");
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(h).Groups[1].Value, ["Email"] = email, ["Password"] = "1234",
        }));
        return c;
    }

    [Fact]
    public async Task 화면은_플랫폼_관리자만_서버_상태_최근_오류_감사_로그를_거른다()
    {
        var kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        var op = await LoggedIn("operator@example.com", platformAdmin: true);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var opId = raw.Accounts.Single(a => a.Email == "operator@example.com").Id;
            raw.AuditLogs.AddRange(
                new AuditLog { OccurredUtc = DateTime.UtcNow, ActorAccountId = opId, Action = "PromotionApproved", TenantId = kids.Id, Detail = "x" },
                new AuditLog { OccurredUtc = DateTime.UtcNow.AddMinutes(-1), ActorAccountId = opId, Action = SeasonService.LogAction, TenantId = kids.Id, Detail = "2027 시즌 시작일 1월 3일" },
                new AuditLog { OccurredUtc = DateTime.UtcNow.AddDays(-40), ActorAccountId = opId, Action = "FamilyTenant", Detail = "옛 기록" });
            raw.SaveChanges();
        }
        _app.Services.GetRequiredService<ErrorLog>().Record(Http("/jamsil-kids"), Thrown("보이는 오류"));

        var html = await op.GetStringAsync("/platform/ops");
        Assert.Contains("DB 연결", html);
        Assert.Contains("검사 안 함 · 시험용 메모리 DB", html);
        Assert.Contains("보이는 오류", html);
        Assert.Contains("진급 승인", html);
        Assert.Contains("2027 시즌 시작일 1월 3일", html);
        Assert.DoesNotContain("옛 기록", html);                                    // 기본은 최근 30일
        Assert.Contains("옛 기록", await op.GetStringAsync("/platform/ops?d=0"));
        var onlySeason = await op.GetStringAsync($"/platform/ops?a={SeasonService.LogAction}");
        Assert.DoesNotContain("<span class=\"tag\">진급 승인</span>", onlySeason);
        Assert.Contains("href=\"/platform/ops\"", html);                           // 플랫폼 메뉴 "운영 점검"

        var user = await LoggedIn("cheolsu@example.com", platformAdmin: false);
        Assert.StartsWith("/account/denied", (await user.GetAsync("/platform/ops")).Headers.Location!.PathAndQuery);
    }
}
