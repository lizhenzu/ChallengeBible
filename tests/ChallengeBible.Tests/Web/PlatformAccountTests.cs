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

/// <summary>플랫폼 · 계정 찾기(0035 임시 비밀번호, 0029 성경 읽음 초기화, 0050 이메일 로그인).</summary>
public partial class PlatformAccountTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids, _pre;
    private readonly int _op, _jieun, _jieun2, _kid;

    public PlatformAccountTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _pre = InMemoryDb.SeedTenant(_db, "jamsil-pre", "JSPRE1");
        _op = InMemoryDb.SeedAccount(_db, "operator@example.com", "운영자").Id;
        _jieun = InMemoryDb.SeedAccount(_db, "jieun@example.com", "박지은", familyTenantId: _kids.Id).Id;
        _jieun2 = InMemoryDb.SeedAccount(_db, "jieun2@example.com", "박지은").Id;
        _kid = InMemoryDb.SeedAccount(_db, "cheolsu@example.com", "김철수").Id;
        InMemoryDb.SeedMembership(_db, _pre.Id, _jieun, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        InMemoryDb.SeedMembership(_db, _kids.Id, _jieun, MembershipStatus.Approved, Roles.Guardian);
        InMemoryDb.SeedMembership(_db, _kids.Id, _jieun2, MembershipStatus.Requested, Roles.Participant);
        InMemoryDb.SeedMembership(_db, _kids.Id, _kid, MembershipStatus.Approved, Roles.Participant);
        using var raw = InMemoryDb.Raw(_db);
        raw.Accounts.Single(a => a.Id == _op).IsPlatformAdmin = true;
        foreach (var (who, v) in new[] { (_jieun, 1), (_jieun, 2), (_kid, 1) })
            raw.VerseReadMarks.Add(new VerseReadMark { AccountId = who, BookId = 1, Chapter = 1, Verse = (short)v, ReadUtc = DateTime.UtcNow });
        raw.Bookmarks.Add(new Bookmark { AccountId = _jieun, BookId = 1, Chapter = 1, Verse = 1, CreatedUtc = DateTime.UtcNow });
        raw.SaveChanges();
    }

    private PlatformAccountService Service() => new(new InMemorySystemDb(_db), TimeProvider.System);

    [Fact]
    public async Task 이름이나_이메일로_찾고_부서_관리자_표시()
    {
        var hits = await Service().SearchAsync(_op, "박지은");
        Assert.Equal(["jieun@example.com", "jieun2@example.com"], hits.Select(h => h.Email).ToArray());
        Assert.True(hits[0].TenantAdmin);
        Assert.False(hits[1].TenantAdmin);                                 // 신청 중 · 참여자
        Assert.Single(await Service().SearchAsync(_op, "CHEOLSU"));         // 이메일은 대소문자 무시
        Assert.Empty(await Service().SearchAsync(_op, " "));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service().SearchAsync(_kid, "박"));
    }

    [Fact]
    public async Task 자세히는_모든_부서_소속과_가족_부서()
    {
        var d = (await Service().DetailAsync(_op, _jieun))!;
        Assert.Equal(2, d.Memberships.Count);
        Assert.Equal([_pre.Name], d.AdminOf);
        Assert.Equal(_kids.Name, d.FamilyTenant);
        Assert.True(d.Memberships.Single(m => m.Tenant == _kids.Name).Family);
    }

    [Fact]
    public async Task R0035_임시_비밀번호는_한_번만_보이고_다음_로그인에_바꾸게_한다()
    {
        var temp = await Service().ResetPasswordAsync(_op, _jieun);
        Assert.Matches("^[a-z]+-\\d{4}$", temp!);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var a = raw.Accounts.Single(x => x.Id == _jieun);
            Assert.True(a.MustChangePassword);
            Assert.NotEqual(PasswordVerificationResult.Failed, new PasswordHasher<Account>().VerifyHashedPassword(a, a.PasswordHash, temp!));
            Assert.DoesNotContain(temp!, raw.AuditLogs.Single().Detail);     // 원문은 어디에도 남기지 않는다
        }
        Assert.Null(await Service().ResetPasswordAsync(_op, _op));           // 플랫폼 관리자 계정은 안 된다
        Assert.Contains((await Service().DetailAsync(_op, _jieun))!.Log, l => l.Text.StartsWith("임시 비밀번호 발급 · 박지은"));
    }

    [Fact]
    public async Task R0029_성경_읽음_초기화는_이메일을_똑같이_써야_하고_그_계정_것만()
    {
        Assert.Null(await Service().ResetBibleMarksAsync(_op, _jieun, "jieun2@example.com"));
        Assert.Equal(3, await Service().ResetBibleMarksAsync(_op, _jieun, " JIEUN@example.com "));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal([_kid], raw.VerseReadMarks.Select(m => m.AccountId).ToArray());
        Assert.Empty(raw.Bookmarks);
        Assert.Contains("읽음 2 · 책갈피 1", raw.AuditLogs.Single().Detail);
    }

    // ---- 화면 흐름 ----

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Login(string email, string password)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await c.GetStringAsync("/account/login");
        var r = await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(html).Groups[1].Value, ["Email"] = email, ["Password"] = password,
        }));
        c.DefaultRequestHeaders.Add("X-Last-Login", r.Headers.Location?.OriginalString ?? "");
        return c;
    }

    [Fact]
    public async Task 운영자가_임시_비밀번호를_만들면_그_계정은_로그인_뒤_비밀번호부터_바꾼다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            var op = raw.Accounts.Single(a => a.Id == _op);
            op.PasswordHash = new PasswordHasher<Account>().HashPassword(op, "1234");
            raw.SaveChanges();
        }
        var op1 = await Login("operator@example.com", "1234");
        var page = await op1.GetStringAsync("/platform/accounts?q=" + Uri.EscapeDataString("박지은"));
        Assert.Contains("aria-current=\"page\">계정 찾기", page);
        Assert.Contains("관리자예요", page);                                       // 첫 결과(부서 관리자) 자세히
        var r = await op1.PostAsync($"/platform/accounts?handler=ResetPassword&q=x&id={_jieun}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value,
        }));
        Assert.True(r.Headers.CacheControl!.NoStore);                             // 임시 비밀번호 응답은 브라우저에 남기지 않는다
        var html = await r.Content.ReadAsStringAsync();
        var temp = Regex.Match(html, "<code>([a-z]+-\\d{4})</code>").Groups[1].Value;
        Assert.NotEmpty(temp);
        Assert.DoesNotContain(temp, await op1.GetStringAsync($"/platform/accounts?q=x&id={_jieun}"));   // 다시 열면 안 보인다

        var who = await Login("jieun@example.com", temp);
        Assert.Equal("/account/change-password",who.DefaultRequestHeaders.GetValues("X-Last-Login").Single().ToLowerInvariant());
    }

    [Fact]
    public async Task 플랫폼_관리자가_아니면_계정_찾기를_못_본다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            var k = raw.Accounts.Single(a => a.Id == _kid);
            k.PasswordHash = new PasswordHasher<Account>().HashPassword(k, "1234");
            raw.SaveChanges();
        }
        var c = await Login("cheolsu@example.com", "1234");
        Assert.StartsWith("/account/denied", (await c.GetAsync("/platform/accounts?q=a")).Headers.Location!.PathAndQuery);
    }
}
