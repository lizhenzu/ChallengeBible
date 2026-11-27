using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>행정업무 메인(2026-11-25 사용자 시안): 교사 · 관리자만 열고, 이 사람이 볼 수 있는 일만 번호 순서로 보인다. 학생 · 학부모는 열지 못한다.</summary>
public partial class OfficeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();

    public OfficeTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());   // 2027-01-05 화요일
        }));
        var kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        foreach (var (login, name, role) in new[] { ("admin", "이관리", Roles.TenantAdmin), ("teacher", "박선생", Roles.Teacher), ("kid", "김하늘", Roles.Participant) })
        {
            var u = InMemoryDb.SeedAccount(_db, login + "@example.com", name);
            InMemoryDb.SeedMembership(_db, kids.Id, u.UserName!, MembershipStatus.Approved, role);
        }
        using var raw = InMemoryDb.Raw(_db);
        foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        raw.SaveChanges();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Login(string login)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value, ["Email"] = login + "@example.com", ["Password"] = "1234" }));
        return c;
    }

    [Fact]
    public async Task 학생은_행정업무를_못_연다()
    {
        var c = await Login("kid");
        var r = await c.GetAsync("/kids/office");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);                       // 「권한이 없어요」(2026-11-26 사용자 — 찾을 수 없음 아님)
        Assert.Contains("권한이 없어요", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 관리자는_여섯_가지가_번호_순서로_보이고_도전_성경말씀_링크가_있다()
    {
        var html = await (await Login("admin")).GetStringAsync("/kids/office");
        Assert.Contains("<h1>행정업무</h1>", html);
        foreach (var (no, label) in new[] { ("01", "오늘 할 일"), ("02", "출석"), ("03", "교적부"), ("04", "인명 검색"), ("05", "생일자 관리"), ("06", "명부 관리") })
            Assert.Contains($"<span class=\"of-no\">{no}</span>{label}", html);
        Assert.Contains("href=\"/kids/todo\"", html);
        Assert.Contains("href=\"/kids/admin/members\"", html);
        Assert.Contains("class=\"of-brand\" href=\"/kids\"", html);   // 맨 아래 「도전!!! 성경말씀」(부서 홈)
        Assert.Contains("<a class=\"of-back\" href=\"/kids\">", html);
        Assert.Contains("1월 5일", html);
    }

    [Fact]
    public async Task 교사도_열고_볼_수_있는_일만_번호가_이어진다()
    {
        var html = await (await Login("teacher")).GetStringAsync("/kids/office");
        Assert.Contains("<h1>행정업무</h1>", html);
        Assert.Contains("<span class=\"of-no\">01</span>", html);
        Assert.DoesNotContain("<span class=\"of-no\">07</span>", html);   // 번호는 보이는 일만 센다(최대 여섯)
        Assert.Contains("href=\"/kids/birthdays\"", html);
    }
}
