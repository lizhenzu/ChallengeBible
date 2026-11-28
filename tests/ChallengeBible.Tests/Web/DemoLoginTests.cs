using System.Net;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>영상 촬영용 데모 자동 로그인(2026-11-28, ADR 0143 — 임시 기능). 꺼짐 · 기한 · 키가 틀리면 404, 맞으면 데모 계정으로 로그인한다.</summary>
public class DemoLoginTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _db = InMemoryDb.NewName();

    public DemoLoginTests(WebApplicationFactory<Program> app) => _factory = app;

    private HttpClient Client(bool enabled = true, string key = "k-1234567890", string? expires = "2027-12-31T00:00:00+09:00")
    {
        var f = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("DemoLogin:Enabled", enabled ? "true" : "false");
            b.UseSetting("DemoLogin:Key", key);
            b.UseSetting("DemoLogin:ExpiresAt", expires ?? "");
            b.ConfigureServices(s =>
            {
                s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
                s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
            });
        });
        using (var raw = InMemoryDb.Raw(_db)) { if (!raw.IdentityRoleRows.Any(r => r.Name == IdentityRoles.Admin)) { raw.IdentityRoleRows.Add(new AppRole { Name = IdentityRoles.Admin, NormalizedName = "ADMIN" }); raw.SaveChanges(); } }
        return f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("user")]
    public async Task 꺼져_있으면_404(string who) =>
        Assert.Equal(HttpStatusCode.NotFound, (await Client(enabled: false).GetAsync($"/demo/{who}?key=k-1234567890")).StatusCode);

    [Fact]
    public async Task 키가_없거나_틀리면_404이고_기한이_없거나_지났어도_404()
    {
        var c = Client();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/demo/user")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/demo/user?key=nope")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/demo/other?key=k-1234567890")).StatusCode);   // 알 수 없는 종류
        Assert.Equal(HttpStatusCode.NotFound, (await Client(expires: "2026-01-01T00:00:00+09:00").GetAsync("/demo/user?key=k-1234567890")).StatusCode);   // 기한 지남
        Assert.Equal(HttpStatusCode.NotFound, (await Client(expires: null).GetAsync("/demo/user?key=k-1234567890")).StatusCode);   // 기한 없음 = 열지 않는다
        Assert.Equal(HttpStatusCode.NotFound, (await Client(key: "").GetAsync("/demo/user?key=")).StatusCode);                       // 키를 정하지 않음
    }

    [Fact]
    public async Task 데모_일반_사용자로_로그인하면_미션홈으로_가고_가상_학생만_보이며_기록이_남는다()
    {
        var c = Client();
        var r = await c.GetAsync("/demo/user?key=k-1234567890");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/demo-school/word-mission", r.Headers.Location!.OriginalString);
        var home = await c.GetStringAsync("/demo-school/word-mission");
        Assert.Contains("김하늘님", home);
        var roster = await c.GetStringAsync("/demo-school/roster");
        Assert.Contains("이바다", roster);                 // 가상 학생
        Assert.Contains("박사랑", roster);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(10, raw.DeptRoles.Count(d => d.RoleCode == "T1001"));          // 가상 학생 10명(데모 사용자 포함)
        Assert.Equal(1, raw.Tenants.Count());                                       // 실제 영역은 건드리지 않고 데모 영역만
        Assert.True(raw.MissionActivities.Count() > 50);                            // 다양한 샘플 기록
        var log = raw.AuditLogs.Single(a => a.Action == "DemoLogin");
        Assert.Equal("demo_user", log.ActorUserName);
        Assert.Contains("account=demo_user", log.Detail);
        Assert.Contains("ip=", log.Detail);
        Assert.DoesNotContain("k-1234567890", log.Detail);                          // 키는 기록하지 않는다
        // 같은 링크를 또 열어도 데이터는 한 번만 만든다
        await c.GetAsync("/demo/user?key=k-1234567890");
        Assert.Equal(10, raw.DeptRoles.Count(d => d.RoleCode == "T1001"));
        Assert.Equal(2, raw.AuditLogs.Count(a => a.Action == "DemoLogin"));
    }

    [Fact]
    public async Task 데모_관리자는_관리자_화면으로_가고_다른_계정으로_로그인돼_있으면_바꿔_로그인한다()
    {
        var c = Client();
        await c.GetAsync("/demo/user?key=k-1234567890");
        var r = await c.GetAsync("/demo/admin?key=k-1234567890");   // 이미 데모 사용자로 로그인 → 관리자로 바꿔
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/demo-school/admin", r.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/demo-school/admin")).StatusCode);
        using var raw = InMemoryDb.Raw(_db);
        var a = raw.Users.Single(u => u.UserName == "demo_admin");
        Assert.True(raw.UserRoles.Any(x => x.UserId == a.Id));                       // 실제 부서 관리자 역할로 동작(우회 없음)
        Assert.NotEqual("-", a.PasswordHash);                                         // 긴 랜덤 비밀번호
    }
}
