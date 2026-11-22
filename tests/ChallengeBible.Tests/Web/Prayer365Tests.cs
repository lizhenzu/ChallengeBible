using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>
/// 자녀를 위한 365 기도(ADR 0106, R-316 ~ R-319): 학생 · 새친구에게는 없음, 하루 한 번, 부서별 집계, 나의 기도 기록은 그 사람 전체.
/// 오늘 = 2027-01-05(화, 서울) → 월일 "0105".
/// </summary>
public partial class Prayer365Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids, _elem;

    public Prayer365Tests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        _elem = InMemoryDb.SeedTenant(_db, "elem", "ELEM01");
        Member("teacher", _kids, Roles.Teacher);
        Member("mom", _kids, Roles.Guardian);
        Member("kid", _kids, Roles.Participant);                       // 학생 T1001
        Member("newbie", _kids, Roles.Participant);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var id = raw.Users.Single(u => u.Email == "newbie@example.com").Id;
            raw.DeptRoles.Single(d => d.UserId == id).RoleCode = ClassService.NewcomerCode;   // 새친구 T1020
            raw.SaveChanges();
        }
        Member("admin", _kids, Roles.Teacher, Roles.TenantAdmin);
        Member("dad", _elem, Roles.Guardian);
        Member("teacher", _elem, Roles.Teacher, alreadyAccount: true);   // 두 부서에 속한 교사
        var op = InMemoryDb.SeedAccount(_db, "operator@example.com", "운영자");
        InMemoryDb.AddRole(_db, op.Id, IdentityRoles.SuperAdmin);
        using var r2 = InMemoryDb.Raw(_db);
        foreach (var u in r2.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        r2.SaveChanges();
    }

    private void Member(string who, Tenant t, string role, string? role2 = null, bool alreadyAccount = false)
    {
        if (!alreadyAccount) InMemoryDb.SeedAccount(_db, who + "@example.com", who);
        string userName;
        using (var raw = InMemoryDb.Raw(_db)) userName = raw.Users.Single(u => u.Email == who + "@example.com").UserName!;
        InMemoryDb.SeedMembership(_db, t.Id, userName, MembershipStatus.Approved, role2 is null ? [role] : [role, role2]);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Login(string who)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value, ["Email"] = who + "@example.com", ["Password"] = "1234",
        }));
        return c;
    }

    private static async Task<HttpResponseMessage> Pray(HttpClient c, string slug)
    {
        var tok = TokenRx().Match(await c.GetStringAsync($"/{slug}/prayer365")).Groups[1].Value;
        return await c.PostAsync($"/{slug}/prayer365?handler=Pray", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = tok }));
    }

    private static async Task<string> Home(HttpClient c, string slug)
    {
        await c.GetAsync($"/{slug}");   // 로그인 직후 한 번은 오늘 할 일로 갈 수 있다(R-309)
        return await c.GetStringAsync($"/{slug}");
    }

    [Fact]
    public async Task R316_학생과_새친구에게는_화면_카드_메뉴가_없고_교사_학부모는_본다()
    {
        foreach (var who in new[] { "kid", "newbie" })
        {
            var c = await Login(who);
            Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/kids/prayer365")).StatusCode);
            var home = await Home(c, "kids");
            Assert.DoesNotContain("/kids/prayer365", home);
        }
        foreach (var who in new[] { "teacher", "mom" })
        {
            var c = await Login(who);
            var page = await c.GetStringAsync("/kids/prayer365");
            Assert.Contains("PRAYER 365", page);
            Assert.Contains("(샘플)", page);                       // 진짜를 넣기 전에는 샘플(ADR 0106 정한 것 3)
            Assert.Contains("1월 5일 · 화요일", page);
            Assert.Contains("한치호 지음", page);
            var home = await Home(c, "kids");
            Assert.Contains("<a class=\"vb-item\" href=\"/kids/prayer365\"", home);      // 프로그램 목록(시안 B)
            Assert.DoesNotContain("class=\"tdr2-r\" href=\"/kids/prayer365\"", home);      // 2026-11-22 2차: 메뉴에서 프로그램 그룹 제거
        }
        var anon = await _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }).GetAsync("/kids/prayer365");
        Assert.NotEqual(HttpStatusCode.OK, anon.StatusCode);
    }

    [Fact]
    public async Task R317_오늘_기도_했어요는_한_사람_하루_한_번이고_다른_부서에서_눌러도_한_번()
    {
        var c = await Login("teacher");
        Assert.Equal(HttpStatusCode.Redirect, (await Pray(c, "kids")).StatusCode);
        Assert.Contains("✓ 오늘 기도했어요", await c.GetStringAsync("/kids/prayer365"));
        await Pray(c, "kids");
        await Pray(c, "elem");   // 같은 날 다른 부서
        using var raw = InMemoryDb.Raw(_db);
        var log = Assert.Single(raw.Prayer365Logs);
        Assert.Equal((_kids.Id, new DateOnly(2027, 1, 5), "0105"), (log.TenantId, log.PrayDate, log.MonthDay));
    }

    [Fact]
    public async Task R318_함께_기도는_우리_부서만_세고_부서_관리자는_우리_부서_참여자만_본다()
    {
        await Pray(await Login("teacher"), "kids");
        await Pray(await Login("mom"), "kids");
        await Pray(await Login("dad"), "elem");
        var mom = await Login("mom");
        Assert.Contains("우리 부서 <b>2</b>명이 오늘 함께 기도했어요", await mom.GetStringAsync("/kids/prayer365"));
        var dad = await Login("dad");
        Assert.Contains("우리 부서 <b>1</b>명이 오늘 함께 기도했어요", await dad.GetStringAsync("/elem/prayer365"));

        var admin = await Login("admin");
        var page = await admin.GetStringAsync("/kids/admin/prayer365");
        Assert.Contains("teacher", page);
        Assert.Contains("mom", page);
        Assert.DoesNotMatch(@"\bdad\b", page);   // 파일 이름 버전 글자(예: site.x3dad9.css)에 걸리지 않게
        Assert.Contains("href=\"/kids/admin/prayer365\"", page);   // 관리 ☰ 메뉴
        Assert.NotEqual(HttpStatusCode.OK, (await mom.GetAsync("/kids/admin/prayer365")).StatusCode);   // 부서 관리자만

        var op = await Login("operator");
        await op.GetAsync("/platform");
        var stats = await op.GetStringAsync("/platform/prayer365-stats");
        Assert.Contains("<small>오늘 기도한 사람</small><b>3<u>명</u></b>", stats);
        Assert.Contains("<td>kids 부서</td><td class=\"n\">2</td><td class=\"n\">2</td>", stats);   // 부서별(R-318)
        Assert.Contains("<td>elem 부서</td><td class=\"n\">1</td><td class=\"n\">1</td>", stats);
        Assert.Contains("href=\"/platform/prayer365-stats\"", stats);
    }

    [Fact]
    public void R319_나의_기도_기록은_기도율_연속_최장을_유년부와_같이_센다()
    {
        var today = new DateOnly(2027, 1, 5);
        var st = Prayer365Stats.Compute([new(2027, 1, 1), new(2027, 1, 2), new(2027, 1, 3), new(2027, 1, 5)], today);
        Assert.Equal((4, 1, 3, 80, true), (st.PrayedDays, st.Streak, st.Longest, st.RatePct, st.PrayedToday));   // 5일 중 4일
        var notYet = Prayer365Stats.Compute([new(2027, 1, 3), new(2027, 1, 4)], today);
        Assert.Equal((2, 100, false), (notYet.Streak, notYet.RatePct, notYet.PrayedToday));   // 오늘 안 눌렀으면 어제까지로
        Assert.Equal(-1, Prayer365Stats.Compute([], today).RatePct);
        Assert.Equal((366, 60, "2월 29일"), (Prayer365Service.AllDays().Count(), Prayer365Service.DayIndex("0229"), Prayer365Service.Label("0229")));
    }

    [Fact]
    public async Task 플랫폼_관리자는_유년부_JSON_으로_진짜_기도문을_넣고_고치고_샘플로_빈_날을_채운다()
    {
        var op = await Login("operator");
        await op.GetAsync("/platform");
        var page = await op.GetStringAsync("/platform/prayer365");
        Assert.Contains("href=\"/platform/prayer365/edit?md=0105\"", page);
        var json = "Results\r\n[{\"MonthDay\":\"0105\",\"Title\":\"진짜 제목\",\"VerseRef\":\"시 1:1\",\"Body\":\"사랑의 하나님,\\n진짜 본문\\n아멘.\",\"Fixes\":\"[{\\\"From\\\":\\\"a\\\",\\\"To\\\":\\\"b\\\"}]\",\"IsReviewed\":true},"
            + "{\"MonthDay\":\"1332\",\"Title\":\"틀린 날\",\"Body\":\"x\"}]";
        var tok = TokenRx().Match(page).Groups[1].Value;
        var res = await op.PostAsync("/platform/prayer365?handler=Import", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = tok, ["json"] = json }));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Contains("기도문 1편을 넣었어요. 날짜 · 제목 · 본문이 빠진 1줄은 건너뛰었어요.", await op.GetStringAsync("/platform/prayer365"));
        using (var raw = InMemoryDb.Raw(_db))
        {
            var p = Assert.Single(raw.Prayers365);
            Assert.Equal(("진짜 제목", false, true), (p.Title, p.IsSample, p.IsReviewed));
        }
        var mom = await Login("mom");
        var pray = await mom.GetStringAsync("/kids/prayer365");
        Assert.Contains("진짜 제목", pray);
        Assert.Contains("<p class=\"call\">사랑의 하나님,</p>", pray);
        Assert.Contains("p365-amen", pray);

        tok = TokenRx().Match(await op.GetStringAsync("/platform/prayer365")).Groups[1].Value;
        await op.PostAsync("/platform/prayer365?handler=Samples", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = tok }));
        using (var raw = InMemoryDb.Raw(_db))
        {
            Assert.Equal(366, raw.Prayers365.Count());
            Assert.Equal("진짜 제목", raw.Prayers365.Single(p => p.MonthDay == "0105").Title);   // 있는 날은 그대로
        }
        Assert.NotEqual(HttpStatusCode.OK, (await mom.GetAsync("/platform/prayer365")).StatusCode);
    }

    [Fact]
    public void 마이그레이션은_기도_기록과_조회_수에_부서_격리를_붙이고_기도문은_공용이다()
    {
        var m = new Prayer365_ForTest();
        var sql = string.Join("\n", m.Up().OfType<SqlOperation>().Select(o => o.Sql));
        foreach (var t in new[] { "Prayer365Log", "Prayer365View" })
            Assert.Contains($"ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.{t},", sql);
        Assert.DoesNotContain(m.Up().OfType<CreateTableOperation>(), o => o.Name == "Prayer365" && o.Columns.Any(c => c.Name == "TenantId"));
    }

    private sealed class Prayer365_ForTest : ChallengeBible.Infrastructure.Migrations.Prayer365
    {
        public IReadOnlyList<MigrationOperation> Up() { var b = new Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer"); base.Up(b); return b.Operations; }
    }
}
