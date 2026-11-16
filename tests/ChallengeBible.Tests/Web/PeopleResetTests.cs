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

/// <summary>
/// 관리자만 남기기(확정 시안 플랫폼-관리자만남기기, 2026-11-15 사용자): 플랫폼 관리자 · 부서 관리자만 남기고 모든 부서의 다른 계정 · 명부와 그 기록을 지운다.
/// 부서 · 반 · 학기와 남는 관리자의 소속 · 명부는 그대로. 확인 글자 DELETE, 플랫폼 관리자만.
/// </summary>
public partial class PeopleResetTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids;
    private readonly string _op, _admin, _kid, _teacher;

    public PeopleResetTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        var op = InMemoryDb.SeedAccount(_db, "operator@example.com", "운영자");
        InMemoryDb.AddRole(_db, op.Id, IdentityRoles.SuperAdmin);
        _op = op.UserName!;
        var adm = InMemoryDb.SeedAccount(_db, "admin@example.com", "윤동호");
        _admin = InMemoryDb.SeedMembership(_db, _kids.Id, adm.UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin).UserName;
        var kid = InMemoryDb.SeedAccount(_db, "kid@example.com", "권동석");
        _kid = InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant).UserName;
        var t = InMemoryDb.SeedAccount(_db, "teacher@example.com", "김선생");
        _teacher = InMemoryDb.SeedMembership(_db, _kids.Id, t.UserName!, MembershipStatus.Approved, Roles.Teacher).UserName;

        using var raw = InMemoryDb.Raw(_db);
        foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        var pAdmin = new Person { TenantId = _kids.Id, Name = "윤동호", UserId = adm.Id, CreatedUtc = DateTime.UtcNow };
        var pKid = new Person { TenantId = _kids.Id, Name = "권동석", UserId = kid.Id, CreatedUtc = DateTime.UtcNow };
        var pNoAcct = new Person { TenantId = _kids.Id, Name = "고성윤", RoleCode = "T1020", NewcomerFirstVisit = new DateOnly(2026, 10, 4), CreatedUtc = DateTime.UtcNow };
        raw.People.AddRange(pAdmin, pKid, pNoAcct);
        var term = new ClassTerm { TenantId = _kids.Id, Year = 2026, StartDate = new DateOnly(2026, 1, 1), CreatedUtc = DateTime.UtcNow };
        raw.ClassTerms.Add(term);
        raw.SaveChanges();
        var cls = new SchoolClass { TenantId = _kids.Id, TermId = term.Id, Name = "1반", CreatedUtc = DateTime.UtcNow };
        raw.Classes.Add(cls);
        raw.Classes.Add(new SchoolClass { TenantId = _kids.Id, TermId = term.Id, Name = "새친구반", IsNewcomer = true, CreatedUtc = DateTime.UtcNow });
        raw.SaveChanges();
        raw.ClassMembers.Add(new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = cls.Id, PersonId = pKid.Id });
        raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = pKid.Id, Sunday = new DateOnly(2026, 11, 8), CreatedUtc = DateTime.UtcNow });
        raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = pNoAcct.Id, Sunday = new DateOnly(2026, 11, 8), CreatedUtc = DateTime.UtcNow });
        raw.MissionActivities.Add(new MissionActivity { TenantId = _kids.Id, UserName = _kid, MissionDate = new DateOnly(2026, 11, 8), MissionItem = "Word.Me", Points = 10, PerformedAt = DateTime.UtcNow });
        raw.ReadingRounds.Add(new ReadingRound { UserName = _kid, RoundNo = 1, StartedAt = DateTime.UtcNow });
        raw.ReadingRounds.Add(new ReadingRound { UserName = _admin, RoundNo = 1, StartedAt = DateTime.UtcNow });   // 관리자 것은 남는다
        raw.CopyVerses.Add(new CopyVerse { UserName = _kid, BookId = 43, Chapter = 3, Verse = 16, Translation = "KRV", WrittenAt = DateTime.UtcNow });   // 성경필사(ADR 0099)
        raw.CopyVerses.Add(new CopyVerse { UserName = _admin, BookId = 43, Chapter = 3, Verse = 16, Translation = "KRV", WrittenAt = DateTime.UtcNow });
        var team = new FamilyTeam { TenantId = _kids.Id, Name = "동석네", AwardKey = "동석네", TeamType = TeamTypes.Family, InsertDT = DateTime.UtcNow, UpdateDT = DateTime.UtcNow };
        team.Members.Add(new FamilyMember { TenantId = _kids.Id, UserName = _kid, SortOrder = 0, Relation = FamilyRelation.Student });
        raw.FamilyTeams.Add(team);
        raw.SaveChanges();
        raw.Gifts.Add(new Gift { TenantId = _kids.Id, Kind = GiftKinds.Family, State = GiftStates.Open, Year = 2026, FamilyTeamId = team.Id, SourceKey = "T2026-1000", MaxAmount = 10000, CreatedUtc = DateTime.UtcNow });
        raw.Gifts.Add(new Gift { TenantId = _kids.Id, Kind = GiftKinds.Newcomer, State = GiftStates.Open, Year = 2026, PersonId = pNoAcct.Id, SourceKey = "N1", MaxAmount = 10000, CreatedUtc = DateTime.UtcNow });
        raw.TeamAwards.Add(new TeamAward { TenantId = _kids.Id, Kind = TeamTypes.Family, AwardKey = "동석네", Threshold = 1000, AwardedAt = DateTime.UtcNow });
        raw.SaveChanges();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> Login(string email)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value, ["Email"] = email, ["Password"] = "1234",
        }));
        return c;
    }

    private static async Task<string> Reset(HttpClient c, string confirm)
    {
        var token = TokenRx().Match(await c.GetStringAsync("/platform/people-reset")).Groups[1].Value;
        var res = await c.PostAsync("/platform/people-reset", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["confirm"] = confirm }));
        return await res.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task 관리자만_남기고_모든_사람과_기록_반을_지운다_새친구반_학기는_그대로()
    {
        var op = await Login("operator@example.com");
        var page = await op.GetStringAsync("/platform/people-reset");
        Assert.Contains("운영자", page);
        Assert.Contains("플랫폼 관리자", page);
        Assert.Contains("잠실새내교회 유년부 관리자", page);
        Assert.Contains("<b class=\"num\">2<u>명</u></b>", page);   // 지울 계정 2(권동석 · 김선생)

        var body = await Reset(op, "DELETE");
        Assert.Contains("지웠어요 · 계정 2명 · 명부 2명 · 반 1개 · 빈 가족팀 1개", body);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(new[] { _admin, _op }.Order().ToArray(), raw.Users.Select(u => u.UserName!).Order().ToArray());
        Assert.Equal("윤동호", raw.People.Single().Name);   // 관리자 명부만
        Assert.Single(raw.DeptRoles);   // 관리자 소속만
        Assert.Empty(raw.AttendanceChecks);
        Assert.Empty(raw.ClassMembers);
        Assert.Empty(raw.MissionActivities);
        Assert.Equal(_admin, raw.ReadingRounds.Single().UserName);
        Assert.Equal(_admin, raw.CopyVerses.Single().UserName);   // 필사도 지운 사람 것만
        Assert.Empty(raw.FamilyTeams);
        Assert.Empty(raw.FamilyMembers);
        Assert.Empty(raw.TeamAwards);
        Assert.Empty(raw.Gifts);
        Assert.Single(raw.Tenants);
        Assert.True(raw.Classes.Single().IsNewcomer);   // 반은 지우고 새친구반만 남김
        Assert.Single(raw.ClassTerms);   // 학기는 그대로
        Assert.Equal("accounts=2;people=2;teams=1;classes=1", raw.AuditLogs.Single(a => a.Action == PeopleResetService.Action).Detail);
    }

    [Fact]
    public async Task DELETE_를_쓰지_않으면_아무것도_지우지_않는다()
    {
        var body = await Reset(await Login("operator@example.com"), "delete");
        Assert.Contains("DELETE 를 써야 지워요", body);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(4, raw.Users.Count());
        Assert.Equal(3, raw.People.Count());
    }

    [Fact]
    public async Task 플랫폼_관리자만()
    {
        Assert.NotEqual(HttpStatusCode.OK, (await (await Login("admin@example.com")).GetAsync("/platform/people-reset")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await (await Login("kid@example.com")).GetAsync("/platform/people-reset")).StatusCode);
    }
}
