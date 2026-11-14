using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChallengeBible.Tests.Web;

/// <summary>R-275 부서 홈 인사 카드의 역할 칩(확정 시안 C, 2026-11-14): 학생은 반, 교사 · 임원은 담임 반 · 새친구반 담당, 임원은 부서 역할 이름, 학부모는 "학부모".</summary>
public partial class RoleChipTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;
    private readonly int _term, _class3, _ncClass;

    public RoleChipTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        using var raw = InMemoryDb.Raw(_db);
        var term = new ClassTerm { TenantId = _kids.Id, Year = 2026, StartDate = new(2026, 1, 1) };
        raw.ClassTerms.Add(term);
        raw.SaveChanges();
        var c3 = new SchoolClass { TenantId = _kids.Id, TermId = term.Id, Name = "3학년 믿음반" };
        var nc = new SchoolClass { TenantId = _kids.Id, TermId = term.Id, Name = "새친구반", IsNewcomer = true };
        raw.Classes.AddRange(c3, nc);
        raw.SaveChanges();
        (_term, _class3, _ncClass) = (term.Id, c3.Id, nc.Id);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    /// <summary>가입 → 소속(역할 코드) → 명부 줄 → 반(있으면). 부서 홈 HTML 을 돌려준다.</summary>
    private async Task<string> Home(string email, string? roleCode, int? classId = null, bool teacher = false)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/signup")).Groups[1].Value,
            ["DisplayName"] = "송시온", ["Email"] = email + "@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        string userName;
        using (var raw0 = InMemoryDb.Raw(_db)) userName = raw0.Users.Single(a => a.Email == email + "@example.com").UserName!;
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, userName, MembershipStatus.Approved, Roles.Participant);
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.DeptRoles.Single(d => d.Id == m.Id && d.TenantId == _kids.Id).RoleCode = roleCode;
            var p = new Person { TenantId = _kids.Id, Name = "송시온", UserId = m.UserId };
            raw.People.Add(p);
            raw.SaveChanges();
            if (classId is int cid) raw.ClassMembers.Add(new ClassMember { TenantId = _kids.Id, TermId = _term, ClassId = cid, PersonId = p.Id, IsTeacher = teacher });
            raw.SaveChanges();
        }
        // 로그인 쿠키의 역할은 가입 때 정해지므로 다시 로그인
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value,
            ["Email"] = userName, ["Password"] = "1234",
        }));
        return Regex.Replace(await c.GetStringAsync("/jamsil-kids"), ">\\s+<", "><");   // 태그 사이 공백은 보지 않는다
    }

    private static string Chips(params (string Kind, string Text)[] chips) =>
        "<span class=\"hj-roles\">" + string.Concat(chips.Select(x => $"<i class=\"{x.Kind}\">{x.Text}</i>")) + "</span>";

    [Fact]
    public async Task R275_학생은_학생_칩과_반_이름_칩()
        => Assert.Contains(Chips(("st", "학생"), ("cl", "3학년 믿음반")), await Home("st", "T1001", _class3));

    [Fact]
    public async Task R275_반이_없는_학생은_학생_칩만()
        => Assert.Contains(Chips(("st", "학생")), await Home("st0", "T1001"));

    [Fact]
    public async Task R275_담임_교사는_교사_칩과_반_담임()
        => Assert.Contains(Chips(("te", "교사"), ("cl", "3학년 믿음반 담임")), await Home("te", "T1002", _class3, teacher: true));

    [Fact]
    public async Task R275_임원은_부서_역할_이름이고_새친구반을_맡으면_담당()
    {
        Assert.Contains(Chips(("of", "부감")), await Home("of", "T1006"));
        Assert.Contains(Chips(("of", "서기"), ("cl", "새친구반 담당")), await Home("of2", "T1008", _ncClass, teacher: true));
    }

    [Fact]
    public async Task R275_학부모는_학부모_칩만()
        => Assert.Contains(Chips(("pa", "학부모")), await Home("pa", null, _class3));

    [Fact]
    public async Task R275_새친구는_새친구_칩과_반()
        => Assert.Contains(Chips(("nc", "새친구"), ("cl", "3학년 믿음반")), await Home("nc", "T1020", _class3));
}
