using System.Net;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;

namespace ChallengeBible.Tests.Web;

/// <summary>시험 도구 › 케이스별 시험 · 오늘 할 일(확정 시안 케이스시험, 2026-11-16): 초등부에 [케이스] 데이터를 만들고 그 사람으로 가장 로그인, 지우기는 [케이스] 데이터만.</summary>
public partial class TestToolsTests
{
    private int SeedElem()
    {
        var t = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        using var raw = InMemoryDb.Raw(_db);
        raw.Tenants.Single(x => x.Id == t.Id).Name = "잠실새내교회 초등부";
        raw.SaveChanges();
        return t.Id;
    }

    /// <summary>리다이렉트를 따라가 마지막 주소와 본문을 돌려준다.</summary>
    private static async Task<(string Url, string Body)> Follow(HttpClient c, HttpResponseMessage r)
    {
        var url = "";
        for (var i = 0; i < 6 && r.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found; i++)
        {
            url = r.Headers.Location!.OriginalString;
            r = await c.GetAsync(url);
        }
        return (url, await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 케이스_1은_고른_선물을_만들고_부장으로_들어가_할_일_화면에서_사_주세요가_보인다()
    {
        var t = SeedElem();
        Seed("superadmin", "boss@example.com", platformAdmin: true);
        var app = App(enabled: true);
        var c = Client(app);
        await Login(c, "superadmin");
        Assert.Contains("케이스별 시험 · 오늘 할 일", await c.GetStringAsync("/test-tools"));

        var (url, body) = await Follow(c, await Post(c, "/test-tools?handler=Case", new() { ["no"] = "1" }));
        Assert.Equal("/jamsil-elem/todo", url);
        Assert.Contains("사 주세요 · [케이스] 아이1", body);
        using var raw = InMemoryDb.Raw(_db);
        var u = raw.Users.Single(x => x.UserName == "jamsil-elem-k-boo");
        Assert.EndsWith(TestToolsService.EmailDomain, u.Email);
        Assert.Equal("T1005", raw.DeptRoles.Single(d => d.UserId == u.Id).RoleCode);
        Assert.Contains(raw.Gifts, g => g.TenantId == t && g.State == GiftStates.Picked);
    }

    [Fact]
    public async Task 케이스_계정은_비밀번호로_들어갈_수_없고_가짜_계정은_케이스를_못_돌린다()
    {
        SeedElem();
        Seed("superadmin", "boss@example.com", platformAdmin: true);
        var app = App(enabled: true);
        var c = Client(app);
        await Login(c, "superadmin");
        await Post(c, "/test-tools?handler=Case", new() { ["no"] = "9" });

        var c2 = Client(app);
        var tok = TokenRx().Match(await c2.GetStringAsync("/account/login")).Groups[1].Value;
        var r = await c2.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tok, ["Email"] = "jamsil-elem-k-tch", ["Password"] = TestToolsService.Password,
        }));
        Assert.NotEqual(HttpStatusCode.Redirect, r.StatusCode);   // 1234 로는 못 들어간다(가장으로만)

        var t2 = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        Seed("jamsil-kids-t01", "jamsil-kids-t01@fake-seed.test", tenantId: t2.Id, roleCode: "T1002");
        var c3 = Client(app);
        await Login(c3, "jamsil-kids-t01");
        Assert.DoesNotContain("케이스별 시험", await c3.GetStringAsync("/test-tools"));
        var denied = await Post(c3, "/test-tools?handler=Case", new() { ["no"] = "1" });
        Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
        Assert.False(denied.Headers.Location?.OriginalString.Contains("/todo") ?? false);
        using var raw = InMemoryDb.Raw(_db);
        Assert.DoesNotContain(raw.People, p => p.Name == $"{CaseTestService.Mark} 아이1");   // 아무것도 만들지 않았다
    }

    [Fact]
    public async Task 케이스_8은_그_쿠키에서만_오늘을_주일로_보고_우리_반_출석_할_일이_뜬다()
    {
        SeedElem();
        Seed("superadmin", "boss@example.com", platformAdmin: true);
        var app = App(enabled: true);
        var c = Client(app);
        await Login(c, "superadmin");
        var (url, body) = await Follow(c, await Post(c, "/test-tools?handler=Case", new() { ["no"] = "8" }));
        Assert.Equal("/jamsil-elem/todo", url);
        Assert.Contains("우리 반 출석 체크 · [케이스] 반", body);

        // 같은 담임이라도 케이스 5 로 들어가면(주일 표시 없음) 주일이 아닌 날엔 출석 할 일이 없다
        if (DateTime.UtcNow.AddHours(9).DayOfWeek != DayOfWeek.Sunday)
        {
            var (_, b5) = await Follow(c, await Post(c, "/test-tools?handler=Case", new() { ["no"] = "5" }));
            Assert.Contains("생일 선물 링크 · [케이스] 생일아이", b5);
            Assert.DoesNotContain("우리 반 출석 체크", b5);
        }
    }

    [Fact]
    public async Task 케이스_지우기는_확인_글자가_있어야_하고_케이스_데이터만_지우며_관리자_계정은_남긴다()
    {
        var t = SeedElem();
        Seed("superadmin", "boss@example.com", platformAdmin: true);
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.People.Add(new Person { TenantId = t, Name = "진짜 아이", CreatedUtc = DateTime.UtcNow });
            raw.SaveChanges();
        }
        var app = App(enabled: true);
        var c = Client(app);
        await Login(c, "superadmin");
        foreach (var n in new[] { 2, 5, 7, 10 }) await Post(c, "/test-tools?handler=Case", new() { ["no"] = n.ToString() });
        c = Client(app);
        await Login(c, "superadmin");

        Assert.Contains("확인 칸에 CASE", await (await Post(c, "/test-tools?handler=CaseClear", new() { ["confirm"] = "" })).Content.ReadAsStringAsync());
        var body = await (await Post(c, "/test-tools?handler=CaseClear", new() { ["confirm"] = "CASE" })).Content.ReadAsStringAsync();
        Assert.Contains("케이스 데이터를 지웠어요", body);
        using var after = InMemoryDb.Raw(_db);
        Assert.DoesNotContain(after.People, p => p.UserId == null && p.Name.StartsWith(CaseTestService.Mark));
        Assert.Contains(after.People, p => p.Name == "진짜 아이");
        Assert.Empty(after.Gifts.Where(g => g.TenantId == t));
        Assert.Empty(after.SignupApplications.Where(a => a.TenantId == t));
        Assert.DoesNotContain(after.DailyWords, w => w.Title.StartsWith(CaseTestService.Mark));
        Assert.DoesNotContain(after.Classes, x => x.Name.StartsWith(CaseTestService.Mark));
        var adm = after.Users.Single(x => x.UserName == "jamsil-elem-k-adm");   // 케이스 관리자 계정은 남는다(CLAUDE.md)
        var adminRole = after.IdentityRoleRows.Single(r => r.Name == IdentityRoles.Admin).Id;
        Assert.Contains(after.UserRoles, r => r.UserId == adm.Id && r.RoleId == adminRole);
        Assert.Contains(after.Users, x => x.UserName == "superadmin");
    }
}
