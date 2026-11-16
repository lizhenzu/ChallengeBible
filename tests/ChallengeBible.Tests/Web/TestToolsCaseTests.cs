using System.Net;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>
/// 시험 도구 › 케이스별 시험 · 오늘 할 일(2026-11-16): [가짜 데이터 넣기] 뒤 실제 명단 아이(가짜 계정)에게 할 일 상황을 더하고,
/// 케이스 버튼은 그 일을 할 가짜 계정(어드민 · t01 · p01)으로 가장 로그인만 한다. 8번은 그 쿠키에서만 오늘을 주일로 본다.
/// </summary>
public partial class TestToolsTests
{
    /// <summary>초등부 + 가짜 계정(s05 · s06 · s07 · s09 · t01 · p01 · admin) + t01 이 담임인 반(아이 s05 · s06).</summary>
    private int SeedElem(bool fake = true)
    {
        var t = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.Tenants.Single(x => x.Id == t.Id).Name = "잠실새내교회 초등부";
            raw.SaveChanges();
        }
        if (!fake) return t.Id;
        var ids = new Dictionary<string, string>();
        foreach (var (code, role) in new[] { ("s05", "T1001"), ("s06", "T1001"), ("s07", "T1001"), ("s09", "T1001"), ("t01", "T1002"), ("p01", (string?)null), ("admin", UserRole.AdminCode) })
            ids[code] = Seed($"jamsil-elem-{code}", $"jamsil-elem-{code}@fake-seed.test", tenantId: t.Id, roleCode: role).Id;
        InMemoryDb.AddRole(_db, ids["admin"], IdentityRoles.Admin);
        using var db = InMemoryDb.Raw(_db);
        var people = ids.ToDictionary(k => k.Key, k => { var p = new Person { TenantId = t.Id, Name = k.Key, UserId = k.Value, CreatedUtc = DateTime.UtcNow }; db.People.Add(p); return p; });
        var term = new ClassTerm { TenantId = t.Id, Year = 2026, StartDate = new DateOnly(2026, 1, 1) };
        db.ClassTerms.Add(term);
        db.SaveChanges();
        var cls = new SchoolClass { TenantId = t.Id, TermId = term.Id, Name = "1반" };
        db.Classes.Add(cls);
        db.SaveChanges();
        db.ClassMembers.Add(new ClassMember { TenantId = t.Id, TermId = term.Id, ClassId = cls.Id, PersonId = people["t01"].Id, IsTeacher = true });
        foreach (var k in new[] { "s05", "s06" }) db.ClassMembers.Add(new ClassMember { TenantId = t.Id, TermId = term.Id, ClassId = cls.Id, PersonId = people[k].Id });
        db.SaveChanges();
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

    private async Task Seeded(WebApplicationFactory<Program> app)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CaseTestService>().SeedTodoAsync("superadmin");
    }

    [Fact]
    public async Task 가짜_데이터가_없으면_케이스는_먼저_넣으라고_한다()
    {
        SeedElem(fake: false);
        Seed("superadmin", "boss@example.com", platformAdmin: true);
        var c = Client(App(enabled: true));
        await Login(c, "superadmin");
        Assert.Contains("케이스별 시험 · 오늘 할 일", await c.GetStringAsync("/test-tools"));
        Assert.Contains("먼저 위의 [가짜 데이터 넣기]", await (await Post(c, "/test-tools?handler=Case", new() { ["no"] = "1" })).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 할_일_상황은_실제_명단_아이에게_들어가고_케이스_1은_어드민으로_들어가_사_주세요가_보인다()
    {
        var t = SeedElem();
        Seed("superadmin", "boss@example.com", platformAdmin: true);
        var app = App(enabled: true);
        await Seeded(app);
        await Seeded(app);   // 다시 해도 같은 상태
        using (var raw = InMemoryDb.Raw(_db))
        {
            Assert.Equal(4, raw.Gifts.Count(g => g.TenantId == t && g.SourceKey.StartsWith("FAKE-")));
            Assert.Single(raw.Gifts.Where(g => g.TenantId == t && g.Kind == GiftKinds.Birthday && g.State == GiftStates.Open));   // t01 반 아이 생일
            Assert.Single(raw.People.Where(p => p.TenantId == t && p.NewcomerFirstVisit != null && p.CreatedByUserName == CaseTestService.FakeMarker));
            Assert.Single(raw.SignupApplications.Where(a => a.TenantId == t && a.Status == SignupStatus.Pending));
        }
        var c = Client(app);
        await Login(c, "superadmin");
        var (url, body) = await Follow(c, await Post(c, "/test-tools?handler=Case", new() { ["no"] = "1" }));
        Assert.Equal("/jamsil-elem/todo", url);
        Assert.Contains("사 주세요 · ", body);
        Assert.Contains("색연필 48색 세트", body);
        Assert.Contains("참여 신청 승인 · 1건", body);
    }

    [Fact]
    public async Task 케이스_8은_그_쿠키에서만_오늘을_주일로_보고_담임_t01_에게_우리_반_출석이_뜬다()
    {
        SeedElem();
        Seed("superadmin", "boss@example.com", platformAdmin: true);
        var app = App(enabled: true);
        await Seeded(app);
        var c = Client(app);
        await Login(c, "superadmin");
        var (url, body) = await Follow(c, await Post(c, "/test-tools?handler=Case", new() { ["no"] = "8" }));
        Assert.Equal("/jamsil-elem/todo", url);
        Assert.Contains("우리 반 출석 체크 · 1반", body);

        if (DateTime.UtcNow.AddHours(9).DayOfWeek != DayOfWeek.Sunday)   // 같은 담임이라도 5번(주일 표시 없음)이면 평일엔 출석 할 일이 없다
        {
            var (_, b5) = await Follow(c, await Post(c, "/test-tools?handler=Case", new() { ["no"] = "5" }));
            Assert.Contains("생일 선물 링크", b5);
            Assert.DoesNotContain("우리 반 출석 체크", b5);
        }
    }

    [Fact]
    public async Task 가짜_계정은_케이스를_못_돌린다()
    {
        var t = SeedElem();
        var c3 = Client(App(enabled: true));
        await Login(c3, "jamsil-elem-t01");
        Assert.DoesNotContain("케이스별 시험", await c3.GetStringAsync("/test-tools"));
        Assert.NotEqual(HttpStatusCode.OK, (await Post(c3, "/test-tools?handler=Case", new() { ["no"] = "1" })).StatusCode);
    }

    [Fact]
    public async Task 예전_케이스_데이터_지우기는_확인_글자가_있어야_하고_관리자_계정은_남긴다()
    {
        var t = SeedElem();
        Seed("superadmin", "boss@example.com", platformAdmin: true);
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.People.Add(new Person { TenantId = t, Name = $"{CaseTestService.Mark} 아이1", CreatedUtc = DateTime.UtcNow });
            raw.People.Add(new Person { TenantId = t, Name = "진짜 아이", CreatedUtc = DateTime.UtcNow });
            raw.SaveChanges();
        }
        var c = Client(App(enabled: true));
        await Login(c, "superadmin");
        Assert.Contains("확인 칸에 CASE", await (await Post(c, "/test-tools?handler=CaseClear", new() { ["confirm"] = "" })).Content.ReadAsStringAsync());
        Assert.Contains("예전 케이스 데이터를 지웠어요", await (await Post(c, "/test-tools?handler=CaseClear", new() { ["confirm"] = "CASE" })).Content.ReadAsStringAsync());
        using var after = InMemoryDb.Raw(_db);
        Assert.DoesNotContain(after.People, p => p.Name.StartsWith(CaseTestService.Mark));
        Assert.Contains(after.People, p => p.Name == "진짜 아이");
        Assert.Contains(after.Users, x => x.UserName == "jamsil-elem-admin");
    }
}
