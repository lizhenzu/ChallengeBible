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

/// <summary>R-361 교적부(ADR 0125, 확정 시안 교적부): 교사 · 관리자만. 반별 아이 정보 보기 · 이름 찾기 · 생일 · 전화 · 전화 2 고치기.</summary>
public partial class ClassBookTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;
    private int _c1, _kidA, _kidB, _left;

    public ClassBookTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());   // 2027-01-05
        }));
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        var t = InMemoryDb.SeedAccount(_db, "teacher@example.com", "박선생");
        InMemoryDb.SeedMembership(_db, _kids.Id, t.UserName!, MembershipStatus.Approved, Roles.Teacher);
        var kid = InMemoryDb.SeedAccount(_db, "kid@example.com", "김하늘");
        InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        using var raw = InMemoryDb.Raw(_db);
        foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        var now = DateTime.UtcNow;
        var tp = new Person { TenantId = _kids.Id, Name = "박선생", UserId = t.Id, Phone1 = "010-1111-2222", CreatedUtc = now };
        var a = new Person { TenantId = _kids.Id, Name = "이바다", RoleCode = "T1001", BirthMonth = 1, BirthDay = 7, Phone1 = "010-3333-4444", CreatedUtc = now };
        var b = new Person { TenantId = _kids.Id, Name = "최별", RoleCode = "T1001", CreatedUtc = now };   // 생일 · 전화 없음
        var l = new Person { TenantId = _kids.Id, Name = "탈퇴함", RoleCode = "T1001", LeftOn = new DateOnly(2026, 12, 1), CreatedUtc = now };
        raw.People.AddRange(tp, a, b, l); raw.SaveChanges();
        var term = new ClassTerm { TenantId = _kids.Id, Year = 2027, StartDate = new DateOnly(2027, 1, 1), CreatedUtc = now };
        raw.ClassTerms.Add(term); raw.SaveChanges();
        var c1 = new SchoolClass { TenantId = _kids.Id, TermId = term.Id, Name = "1반", CreatedUtc = now };
        raw.Classes.Add(c1); raw.SaveChanges();
        raw.ClassMembers.AddRange(
            new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = c1.Id, PersonId = tp.Id, IsTeacher = true, CreatedUtc = now },
            new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = c1.Id, PersonId = a.Id, CreatedUtc = now },
            new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = c1.Id, PersonId = b.Id, CreatedUtc = now });
        raw.SaveChanges();
        (_c1, _kidA, _kidB, _left) = (c1.Id, a.Id, b.Id, l.Id);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Login(string email)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value, ["Email"] = email, ["Password"] = "1234" }));
        return c;
    }

    private async Task<HttpResponseMessage> Save(HttpClient c, int personId, string? month, string? day, string p1, string p2)
    {
        var page = await c.GetStringAsync($"/kids/classbook/{_c1}");
        var f = new Dictionary<string, string> { ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value, ["personId"] = personId.ToString(), ["phone1"] = p1, ["phone2"] = p2 };
        if (month != null) f["month"] = month;
        if (day != null) f["day"] = day;
        return await c.PostAsync($"/kids/classbook/{_c1}?handler=Save", new FormUrlEncodedContent(f));
    }

    [Fact]
    public async Task R361_학생은_교적부를_못_연다_메뉴에도_없다()
    {
        var c = await Login("kid@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/kids/classbook")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/kids/classbook/{_c1}")).StatusCode);
        Assert.DoesNotContain("교적부", await c.GetStringAsync("/kids"));
    }

    [Fact]
    public async Task R361_교사는_메뉴와_처음_화면_반_교적을_본다()
    {
        var c = await Login("teacher@example.com");
        Assert.Contains("class=\"tdr2-r\" href=\"/kids/classbook\"", await c.GetStringAsync("/kids"));
        var home = WebUtility.HtmlDecode(await c.GetStringAsync("/kids/classbook"));
        Assert.Contains("1반", home);
        Assert.Contains("박선생", home);
        var find = WebUtility.HtmlDecode(Regex.Replace(await c.GetStringAsync("/kids/classbook?searchName=" + Uri.EscapeDataString("바다")), "<[^>]+>", ""));   // 찾은 글자는 <mark> 로 칠한다
        Assert.Contains("이바다", Regex.Replace(find, @"\s+", ""));
        Assert.DoesNotContain("탈퇴함", WebUtility.HtmlDecode(await c.GetStringAsync("/kids/classbook?searchName=" + Uri.EscapeDataString("탈퇴"))));
        var css = Regex.Match(home, "href=\"(/css/classbook[^\"]*\\.css[^\"]*)\"").Groups[1].Value;   // 화면 꾸밈 파일이 실제로 있다
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(css)).StatusCode);
        var cls = WebUtility.HtmlDecode(await c.GetStringAsync($"/kids/classbook/{_c1}"));
        var js = Regex.Match(cls, "src=\"(/js/classbook[^\"]*\\.js[^\"]*)\"").Groups[1].Value;
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(js)).StatusCode);
        Assert.Contains("이바다", cls);
        Assert.Contains("010-3333-4444", cls);
        Assert.Contains("최별", cls);
        Assert.Matches("data-name=\"최별\"[^>]*data-miss=\"1\"", cls);   // 정보 빠짐
        Assert.Matches("data-name=\"이바다\"[^>]*data-miss=\"0\"", cls);
    }

    [Fact]
    public async Task R361_생일_전화_전화2만_고친다_틀린_값은_거절한다()
    {
        var c = await Login("teacher@example.com");
        var ok = await Save(c, _kidB, "3", "9", "010-5555-6666", "010 7777 8888");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("저장했어요", Regex.Unescape(await ok.Content.ReadAsStringAsync()));
        using (var raw = InMemoryDb.Raw(_db))
        {
            var p = raw.People.Single(x => x.Id == _kidB);
            Assert.Equal(((byte?)3, (byte?)9, "010-5555-6666", "010-7777-8888", "최별", "T1001"), (p.BirthMonth, p.BirthDay, p.Phone1, p.Phone2, p.Name, p.RoleCode));
        }
        Assert.DoesNotContain("저장했어요", Regex.Unescape(await (await Save(c, _kidA, "2", "30", "", "")).Content.ReadAsStringAsync()));   // 2월 30일
        Assert.DoesNotContain("저장했어요", Regex.Unescape(await (await Save(c, _kidA, null, null, "010-12ab", "")).Content.ReadAsStringAsync()));
        Assert.DoesNotContain("저장했어요", Regex.Unescape(await (await Save(c, _left, "1", "1", "", "")).Content.ReadAsStringAsync()));   // 탈퇴한 줄
        using var after = InMemoryDb.Raw(_db);
        Assert.Equal((byte?)7, after.People.Single(x => x.Id == _kidA).BirthDay);   // 거절된 저장은 그대로
    }

    [Fact]
    public async Task R361_학생이_직접_저장_주소를_불러도_막힌다()
    {
        var teacher = await Login("teacher@example.com");
        var token = TokenRx().Match(await teacher.GetStringAsync($"/kids/classbook/{_c1}")).Groups[1].Value;
        var c = await Login("kid@example.com");
        var r = await c.PostAsync($"/kids/classbook/{_c1}?handler=Save", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["personId"] = _kidB.ToString(), ["month"] = "1", ["day"] = "1" }));
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Null(raw.People.Single(x => x.Id == _kidB).BirthMonth);
    }

    [Fact]
    public void R361_처음_화면은_새친구반_학년_기타_순서로_묶고_카드에는_짧은_반_이름()
    {
        BookClass C(int id, string name, bool nc = false) => new(id, name, nc, [], 0);
        var g = RosterBookService.Group([C(1, "믿음반"), C(2, "2학년 1반"), C(3, "1학년 2반"), C(4, "새친구반", true), C(5, "1학년 1반")]);
        Assert.Equal(["new", "g1", "g2", "etc"], g.Select(x => x.Key));
        Assert.Equal(["새친구반", "1학년", "2학년", "기타"], g.Select(x => x.Title));
        Assert.Equal(["2반", "1반"], g[1].Classes.Select(x => x.Short));   // 반 순서는 그대로
        Assert.Equal("반", RosterBookService.Group([C(1, "믿음반")]).Single().Title);   // 학년 반이 없으면 "반"
    }

    [Fact]
    public async Task R361_반_카드에_담임_이름_전화번호_전화_버튼()
    {
        var c = await Login("teacher@example.com");
        var home = WebUtility.HtmlDecode(await c.GetStringAsync("/kids/classbook"));
        Assert.Contains("박선생 선생님", home);
        Assert.Contains("010-1111-2222", home);
        Assert.Contains("href=\"tel:01011112222\"", home);
        Assert.Contains($"href=\"/kids/classbook/{_c1}\"", home);
        Assert.DoesNotContain("빠짐", home);   // 확정 시안 교적부-A: 빠짐 · 생일 딱지는 처음 화면에 없다
        Assert.Contains("\"n\":\"이바다\"", Regex.Unescape(home));   // 칠 때마다 바로 찾기용 명단
        Assert.DoesNotContain("탈퇴함", Regex.Unescape(home));
    }
}

/// <summary>UI-1(05 문서): 선택 표시에 진한 굵은 선을 쓰지 않는다 — 공통 --ring 값만.</summary>
public class FocusRingRuleTests
{
    [Fact]
    public void UI1_스타일에_진한_3px_선택_표시가_없다()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "src", "ChallengeBible.Web", "wwwroot", "css"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var bad = Directory.GetFiles(Path.Combine(dir!, "src", "ChallengeBible.Web", "wwwroot", "css"), "*.css")
            .Where(f => !f.EndsWith("album.css"))   // 앨범은 "고른 사진" 표시(포커스 아님)
            .SelectMany(f => File.ReadAllLines(f).Select((l, i) => (f, i, l)))
            .Where(x => x.l.Contains(":focus") && System.Text.RegularExpressions.Regex.IsMatch(x.l, @"outline:\s*3px\s+solid\s+var\(--accent\)"))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}").ToList();
        Assert.True(bad.Count == 0, "진한 3px 선택 표시: " + string.Join(", ", bad));
        var site = File.ReadAllText(Path.Combine(dir!, "src", "ChallengeBible.Web", "wwwroot", "css", "site.css"));
        Assert.Contains("--ring-key", site);
    }
}
