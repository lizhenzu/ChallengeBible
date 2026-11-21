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

/// <summary>R-362 교사 · 임원 및 기타(2026-11-21 확정 시안 역할-담임-시안2): 부서 관리자만. 임원 칸(중복 · 빈 자리) · 역할 바꾸기 · 담임 넣기 · 옮기기 · 빼기.</summary>
public partial class StaffTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;
    private int _c1, _c2, _boss1, _boss2, _t1, _t2, _kid;
    private string _adminUserId = "";

    public StaffTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        var adm = InMemoryDb.SeedAccount(_db, "admin@example.com", "관리자");
        InMemoryDb.SeedMembership(_db, _kids.Id, adm.UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        var kidAcc = InMemoryDb.SeedAccount(_db, "kid@example.com", "김하늘");
        InMemoryDb.SeedMembership(_db, _kids.Id, kidAcc.UserName!, MembershipStatus.Approved, Roles.Participant);
        using var raw = InMemoryDb.Raw(_db);
        foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        raw.DeptRoles.Single(d => d.UserId == adm.Id).RoleCode = UserRole.AdminCode;   // 부서 관리자 계정 = 어드민 역할
        _adminUserId = adm.Id;
        var now = DateTime.UtcNow;
        var b1 = new Person { TenantId = _kids.Id, Name = "유부장", RoleCode = "T1005", CreatedUtc = now };
        var b2 = new Person { TenantId = _kids.Id, Name = "정부장", RoleCode = "T1005", CreatedUtc = now };
        var t1 = new Person { TenantId = _kids.Id, Name = "최교사", RoleCode = "T1002", CreatedUtc = now };
        var t2 = new Person { TenantId = _kids.Id, Name = "한교사", RoleCode = "T1002", CreatedUtc = now };
        var k = new Person { TenantId = _kids.Id, Name = "이학생", RoleCode = "T1001", CreatedUtc = now };
        raw.People.AddRange(b1, b2, t1, t2, k); raw.SaveChanges();
        var term = new ClassTerm { TenantId = _kids.Id, Year = 2027, StartDate = new DateOnly(2027, 1, 1), CreatedUtc = now };
        raw.ClassTerms.Add(term); raw.SaveChanges();
        var c1 = new SchoolClass { TenantId = _kids.Id, TermId = term.Id, Name = "1학년 1반", CreatedUtc = now };
        var c2 = new SchoolClass { TenantId = _kids.Id, TermId = term.Id, Name = "1학년 2반", CreatedUtc = now };
        raw.Classes.AddRange(c1, c2); raw.SaveChanges();
        raw.ClassMembers.Add(new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = c1.Id, PersonId = t1.Id, IsTeacher = true, CreatedUtc = now });
        raw.SaveChanges();
        (_c1, _c2, _boss1, _boss2, _t1, _t2, _kid) = (c1.Id, c2.Id, b1.Id, b2.Id, t1.Id, t2.Id, k.Id);
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

    private async Task<HttpResponseMessage> Post(HttpClient c, string page, string handler, Dictionary<string, string> f)
    {
        f["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync(page)).Groups[1].Value;
        return await c.PostAsync($"{page}?handler={handler}", new FormUrlEncodedContent(f));
    }

    [Fact]
    public async Task R362_부서_관리자만_들어오고_메뉴에_교사와_임원_및_기타가_있다()
    {
        var kid = await Login("kid@example.com");
        Assert.NotEqual(HttpStatusCode.OK, (await kid.GetAsync("/kids/admin/teachers")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await kid.GetAsync("/kids/admin/officers")).StatusCode);
        var a = await Login("admin@example.com");
        var home = WebUtility.HtmlDecode(await a.GetStringAsync("/kids/admin"));
        Assert.Contains("/kids/admin/teachers", home);
        Assert.Contains("임원 및 기타", home);
    }

    [Fact]
    public async Task R362_임원_칸은_중복과_빈_자리를_보여_준다()
    {
        var a = await Login("admin@example.com");
        var page = WebUtility.HtmlDecode(await a.GetStringAsync("/kids/admin/officers"));
        Assert.Contains("부장이(가) 2명이에요", page);   // 유부장 · 정부장
        Assert.Contains("유부장 · 정부장", page);
        Assert.Matches("st-slot\\s+empty[^>]*data-code=\"T1009\"", page);   // 회계 빈 자리
    }

    [Fact]
    public async Task R362_역할을_바꾸면_명부와_소속에_들어가고_어드민_계정은_못_바꾼다()
    {
        var a = await Login("admin@example.com");
        await Post(a, "/kids/admin/officers", "Role", new() { ["personId"] = _boss2.ToString(), ["code"] = "T1006" });   // 정부장 → 부감
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal("T1006", raw.People.Single(p => p.Id == _boss2).RoleCode);
        Assert.DoesNotContain("부장이(가) 2명", WebUtility.HtmlDecode(await a.GetStringAsync("/kids/admin/officers")));

        int adminPid;
        using (var raw = InMemoryDb.Raw(_db)) adminPid = raw.People.Single(p => p.UserId == _adminUserId).Id;
        await Post(a, "/kids/admin/officers", "Role", new() { ["personId"] = adminPid.ToString(), ["code"] = "T1001" });   // R-315
        await Post(a, "/kids/admin/officers", "Role", new() { ["personId"] = _t2.ToString(), ["code"] = UserRole.AdminCode });   // 어드민으로는 못 바꾼다
        using var after = InMemoryDb.Raw(_db);
        Assert.Equal(UserRole.AdminCode, after.DeptRoles.Single(d => d.UserId == _adminUserId).RoleCode);
        Assert.Equal("T1002", after.People.Single(p => p.Id == _t2).RoleCode);
    }

    [Fact]
    public async Task R362_담임을_넣고_옮기고_빼고_학생은_담임이_못_된다()
    {
        var a = await Login("admin@example.com");
        var page = WebUtility.HtmlDecode(await a.GetStringAsync("/kids/admin/teachers"));
        Assert.Contains("담임 최교사", page);
        Assert.Contains("반이 없는 교사", page);
        Assert.Contains("한교사", page);

        await Post(a, "/kids/admin/teachers", "Add", new() { ["classId"] = _c2.ToString(), ["personId"] = _t2.ToString() });
        await Post(a, "/kids/admin/teachers", "Add", new() { ["classId"] = _c2.ToString(), ["personId"] = _kid.ToString() });   // 학생 — 안 됨
        await Post(a, "/kids/admin/teachers", "Move", new() { ["fromId"] = _c1.ToString(), ["toId"] = _c2.ToString(), ["personId"] = _t1.ToString() });
        using (var raw = InMemoryDb.Raw(_db))
        {
            Assert.True(raw.ClassMembers.Any(m => m.ClassId == _c2 && m.PersonId == _t2 && m.IsTeacher));
            Assert.True(raw.ClassMembers.Any(m => m.ClassId == _c2 && m.PersonId == _t1 && m.IsTeacher));
            Assert.False(raw.ClassMembers.Any(m => m.PersonId == _kid));
            Assert.False(raw.ClassMembers.Any(m => m.ClassId == _c1));
        }
        await Post(a, "/kids/admin/teachers", "Remove", new() { ["classId"] = _c2.ToString(), ["personId"] = _t2.ToString() });
        using var after = InMemoryDb.Raw(_db);
        Assert.False(after.ClassMembers.Any(m => m.PersonId == _t2));
    }

    [Fact]
    public async Task 부서_메뉴판의_부서_관리는_맨_아래_관리_묶음에()
    {
        var a = await Login("admin@example.com");
        var html = WebUtility.HtmlDecode(await a.GetStringAsync("/kids"));
        var nav = html[html.IndexOf("<nav aria-label=\"메뉴\">")..];
        var g = nav.IndexOf("<h2 class=\"tdr-g\">관리</h2>");
        Assert.True(g > nav.IndexOf("사진첩"), "관리 묶음은 함께하기 뒤");
        Assert.True(nav.IndexOf("href=\"/kids/admin\"") > g, "부서 관리는 관리 묶음 안");
        Assert.True(nav.IndexOf("href=\"/kids/admin\"") > nav.IndexOf("인명 검색"));
    }

    [Fact]
    public async Task R362_담임을_다른_선생님으로_바꾸면_원래_선생님은_반이_없는_교사로()
    {
        var a = await Login("admin@example.com");
        await Post(a, "/kids/admin/teachers", "Replace", new() { ["classId"] = _c1.ToString(), ["oldId"] = _t1.ToString(), ["newId"] = _t2.ToString() });
        using (var raw = InMemoryDb.Raw(_db))
        {
            Assert.True(raw.ClassMembers.Any(m => m.ClassId == _c1 && m.PersonId == _t2 && m.IsTeacher));
            Assert.False(raw.ClassMembers.Any(m => m.PersonId == _t1));
        }
        await Post(a, "/kids/admin/teachers", "Replace", new() { ["classId"] = _c1.ToString(), ["oldId"] = _t2.ToString(), ["newId"] = _kid.ToString() });   // 학생으로는 안 됨 → 그대로
        using var after = InMemoryDb.Raw(_db);
        Assert.True(after.ClassMembers.Any(m => m.ClassId == _c1 && m.PersonId == _t2 && m.IsTeacher));
        Assert.False(after.ClassMembers.Any(m => m.PersonId == _kid));
    }

    [Fact]
    public async Task R362_봉사_기타_새친구도_한_줄_칸으로_보이고_바꿀_수_있다()
    {
        var a = await Login("admin@example.com");
        var page = WebUtility.HtmlDecode(await a.GetStringAsync("/kids/admin/officers"));
        foreach (var code in new[] { "T1017", "T1018", UserRole.EtcCode, "T1020" })
            Assert.Matches($"class=\"st-slot[^\"]*\" data-act=\"slot\" data-code=\"{code}\"", page);
        await Post(a, "/kids/admin/officers", "Role", new() { ["personId"] = _t2.ToString(), ["code"] = "T1017" });   // 한교사 → 영상
        await Post(a, "/kids/admin/officers", "Role", new() { ["personId"] = _kid.ToString(), ["code"] = "T1020" });  // 이학생 → 새친구
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal("T1017", raw.People.Single(p => p.Id == _t2).RoleCode);
        Assert.Equal("T1020", raw.People.Single(p => p.Id == _kid).RoleCode);
    }

    [Fact]
    public void R362_임원_봉사_고르기_목록에서는_학생을_빼고_기타_새친구에는_남긴다()
    {
        var js = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ChallengeBible.Web", "wwwroot", "js", "staff.js"));
        Assert.Contains("const noStudent = code !== 'T1099' && code !== 'T1020';", js);
        Assert.Contains("!(noStudent && p.c === 'T1001')", js);
        Assert.Contains("p.c !== 'T1001'", js);   // 봉사 추가 목록도
    }
}
