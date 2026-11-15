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

/// <summary>
/// 새해 초기화 버튼(ADR 0094 정한 것 12, R-296 고침): 플랫폼 관리자가 확인 글자 RESET 을 쓰고 누르면 끝나지 않은 선물은 해 넘어 닫힘,
/// 시상 기록은 지난 기록으로 옮기고 비움, 개인 선물 점수는 누른 순간부터 0. 아무 때나 몇 번이든. 오늘 = 2027-01-05(서울).
/// </summary>
public partial class YearResetTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids;
    private readonly Dictionary<string, string> _p = [];

    public YearResetTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(_time);
        }));
        _kids = InMemoryDb.SeedTenant(_db, "kids", "KIDS01");
        foreach (var (login, name) in new[] { ("cheolsu", "김철수"), ("younghee", "김영희") })
            _p[login] = InMemoryDb.SeedMembership(_db, _kids.Id, InMemoryDb.SeedAccount(_db, login + "@example.com", name).UserName!, MembershipStatus.Approved, Roles.Participant).UserName;
        var adm = InMemoryDb.SeedAccount(_db, "admin@example.com", "윤동호");
        InMemoryDb.SeedMembership(_db, _kids.Id, adm.UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        var op = InMemoryDb.SeedAccount(_db, "operator@example.com", "운영자");
        InMemoryDb.AddRole(_db, op.Id, IdentityRoles.SuperAdmin);
        using var raw = InMemoryDb.Raw(_db);
        foreach (var u in raw.Users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234");
        raw.SaveChanges();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private void Pts(string login, int points, DateTime performedUtc)
    {
        using var raw = InMemoryDb.Raw(_db);
        raw.MissionActivities.Add(new MissionActivity { TenantId = _kids.Id, UserName = _p[login], MissionDate = new DateOnly(2027, 1, 5), MissionItem = "Word.Me", Points = points, PerformedAt = performedUtc });
        raw.SaveChanges();
    }

    private void FamilyTeam()
    {
        using var raw = InMemoryDb.Raw(_db);
        var t = new FamilyTeam { TenantId = _kids.Id, Name = "철수네", AwardKey = "철수네", TeamType = TeamTypes.Family, InsertDT = DateTime.UtcNow, UpdateDT = DateTime.UtcNow };
        t.Members.Add(new FamilyMember { TenantId = _kids.Id, UserName = _p["cheolsu"], SortOrder = 0, Relation = FamilyRelation.Student });
        t.Members.Add(new FamilyMember { TenantId = _kids.Id, UserName = _p["younghee"], SortOrder = 1, Relation = FamilyRelation.Mother });
        raw.FamilyTeams.Add(t);
        raw.SaveChanges();
    }

    private async Task<HttpClient> Login(string email)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/login")).Groups[1].Value, ["Email"] = email, ["Password"] = "1234",
        }));
        return c;
    }

    private static async Task<HttpResponseMessage> Reset(HttpClient c, string confirm)
    {
        var token = TokenRx().Match(await c.GetStringAsync("/platform/year-reset")).Groups[1].Value;
        return await c.PostAsync("/platform/year-reset", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["confirm"] = confirm }));
    }

    private void SeedEverything()
    {
        using var raw = InMemoryDb.Raw(_db);
        foreach (var state in new[] { GiftStates.Open, GiftStates.Picked, GiftStates.Purchased, GiftStates.Expired, GiftStates.Delivered, GiftStates.Cancelled })
            raw.Gifts.Add(new Gift { TenantId = _kids.Id, Kind = GiftKinds.Personal, State = state, Year = 2027, UserName = _p["cheolsu"], SourceKey = "P-" + state, MaxAmount = 5000, CreatedUtc = DateTime.UtcNow });
        var reading = new Gift { TenantId = _kids.Id, Kind = GiftKinds.Reading, State = GiftStates.Delivered, Year = 2026, UserName = _p["cheolsu"], SourceKey = "R1", MaxAmount = 10000, ItemPrice = 9000, CreatedUtc = DateTime.UtcNow };
        raw.Gifts.Add(reading);
        raw.SaveChanges();
        foreach (var g in raw.Gifts.ToList()) raw.GiftLogs.Add(new GiftLog { TenantId = _kids.Id, GiftId = g.Id, Action = GiftActions.Open, AtUtc = DateTime.UtcNow });
        raw.GiftPointUses.Add(new GiftPointUse { TenantId = _kids.Id, UserName = _p["cheolsu"], Year = 2027, GiftId = raw.Gifts.First(g => g.SourceKey == "P-" + GiftStates.Picked).Id, PointsUsed = 100, CreatedUtc = DateTime.UtcNow });
        raw.TeamAwards.Add(new TeamAward { TenantId = _kids.Id, Kind = TeamTypes.Family, AwardKey = "철수네", Threshold = 1000, AwardedAt = new DateTime(2026, 12, 20) });
        raw.TeamAwardArchives.Add(new TeamAwardArchive { TenantId = _kids.Id, Year = 2025, Kind = TeamTypes.Family, AwardKey = "철수네", Threshold = 1000, AwardedAt = new DateTime(2025, 12, 20) });
        raw.MissionActivities.Add(new MissionActivity { TenantId = _kids.Id, UserName = _p["cheolsu"], MissionDate = new DateOnly(2027, 1, 3), MissionItem = "Word.Me", Points = 300, PerformedAt = DateTime.UtcNow });
        var kid = new Person { TenantId = _kids.Id, Name = "김철수", RoleCode = "T1001", CreatedUtc = DateTime.UtcNow };
        var nc = new Person { TenantId = _kids.Id, Name = "새친구", RoleCode = "T1020", NewcomerFirstVisit = new DateOnly(2026, 12, 6), NewcomerVoluntary = false, NewcomerEvangelist = "김철수", CreatedUtc = DateTime.UtcNow };
        raw.People.AddRange(kid, nc);
        raw.SaveChanges();
        raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = kid.Id, Sunday = new DateOnly(2027, 1, 3), CreatedUtc = DateTime.UtcNow });
        foreach (var d in new[] { 6, 13, 20, 27 }) raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = nc.Id, Sunday = new DateOnly(2026, 12, d), CreatedUtc = DateTime.UtcNow });
        raw.AuditLogs.Add(new AuditLog { OccurredUtc = DateTime.UtcNow, ActorUserName = "operator", Action = "ImpersonateStart" });
        raw.ReadingRounds.Add(new ReadingRound { UserName = _p["cheolsu"], RoundNo = 1, StartedAt = new DateTime(2026, 1, 1), CompletedAt = new DateTime(2026, 12, 1) });
        raw.SaveChanges();
    }

    [Fact]
    public async Task 정한것17_RESET_을_쓰고_누르면_관리자_부서_반만_남기고_모두_지운다()
    {
        SeedEverything();
        FamilyTeam();
        int classId, adminPersonId;
        using (var raw = InMemoryDb.Raw(_db))
        {
            var term = new ClassTerm { TenantId = _kids.Id, Year = 2027, StartDate = new DateOnly(2027, 1, 1), CreatedUtc = DateTime.UtcNow };
            raw.ClassTerms.Add(term); raw.SaveChanges();
            var cls = new SchoolClass { TenantId = _kids.Id, TermId = term.Id, Name = "1학년 1반", CreatedUtc = DateTime.UtcNow };
            raw.Classes.Add(cls); raw.SaveChanges(); classId = cls.Id;
            var adminUser = raw.Users.Single(u => u.Email == "admin@example.com");
            var ap = new Person { TenantId = _kids.Id, Name = "윤동호", UserId = adminUser.Id, CreatedUtc = DateTime.UtcNow };
            raw.People.Add(ap); raw.SaveChanges(); adminPersonId = ap.Id;
            var kid = raw.People.Single(p => p.Name == "김철수");
            raw.ClassMembers.Add(new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = cls.Id, PersonId = kid.Id, CreatedUtc = DateTime.UtcNow });
            raw.ClassMembers.Add(new ClassMember { TenantId = _kids.Id, TermId = term.Id, ClassId = cls.Id, PersonId = ap.Id, IsTeacher = true, CreatedUtc = DateTime.UtcNow });
            raw.GuardianLinks.Add(new GuardianLink { TenantId = _kids.Id, GuardianUserName = _p["younghee"], StudentUserName = _p["cheolsu"], CreatedUtc = DateTime.UtcNow, CreatedByUserName = "x" });
            raw.GiftYearCloses.Add(new GiftYearClose { Year = 2027, ClosedUtc = DateTime.UtcNow.AddDays(-1), ClosedByUserName = "operator" });
            raw.SaveChanges();
        }
        var op = await Login("operator@example.com");
        Assert.Contains("되돌릴 수 없어요", await op.GetStringAsync("/platform/year-reset"));

        var body = await (await Reset(op, "RESET")).Content.ReadAsStringAsync();
        Assert.Contains("초기화했어요", body);
        using var after = InMemoryDb.Raw(_db);
        Assert.Empty(after.MissionActivities);
        Assert.Empty(after.AttendanceChecks);   // 새친구 출석도
        Assert.Empty(after.Gifts);   // 완독 선물도
        Assert.Empty(after.GiftLogs);
        Assert.Empty(after.GiftPointUses);
        Assert.Empty(after.TeamAwards);
        Assert.Empty(after.TeamAwardArchives);
        Assert.Empty(after.FamilyTeams);
        Assert.Empty(after.FamilyMembers);
        Assert.Empty(after.GuardianLinks);
        Assert.Empty(after.ReadingRounds);
        Assert.Empty(after.GiftYearCloses);   // 초기화 시각도 남기지 않음
        // 관리자(부서 관리자 · 플랫폼 관리자)만 남고, 그 명부 줄 · 반 배정 · 반은 그대로
        Assert.Equal(new[] { "admin@example.com", "operator@example.com" }, after.Users.Select(u => u.Email!).OrderBy(e => e).ToArray());
        Assert.Equal(adminPersonId, after.People.Single().Id);
        Assert.Equal(adminPersonId, after.ClassMembers.Single().PersonId);
        Assert.Equal(classId, after.Classes.Single().Id);
        Assert.Single(after.DeptRoles);
        var audit = after.AuditLogs.Single();   // 감사 로그는 이번 초기화 한 줄만
        Assert.Equal((YearResetService.Action, "missions=1;attend=5;gifts=7;awards=2;audits=1;accounts=2;people=2;teams=1;reading=1"), (audit.Action, audit.Detail));
    }

    [Fact]
    public async Task 정한것17_RESET_을_쓰지_않으면_아무것도_바뀌지_않는다()
    {
        SeedEverything();
        var op = await Login("operator@example.com");
        var body = await (await Reset(op, "reset")).Content.ReadAsStringAsync();
        Assert.Contains("RESET 를 써야 초기화해요", body);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(7, raw.Gifts.Count());
        Assert.Single(raw.MissionActivities);
        Assert.Single(raw.TeamAwards);
        Assert.Empty(raw.GiftYearCloses);
    }

    [Fact]
    public async Task 정한것17_플랫폼_관리자만_들어온다()
    {
        var admin = await Login("admin@example.com");   // 부서 관리자
        Assert.NotEqual(HttpStatusCode.OK, (await admin.GetAsync("/platform/year-reset")).StatusCode);
        var student = await Login("cheolsu@example.com");
        Assert.NotEqual(HttpStatusCode.OK, (await student.GetAsync("/platform/year-reset")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await Login("operator@example.com")).GetAsync("/platform/year-reset")).StatusCode);
    }

    [Fact]
    public async Task 정한것17_초기화_뒤에는_처음_상태처럼_다시_넣은_기록을_모두_센다()
    {
        Pts("cheolsu", 340, _time.Now.UtcDateTime.AddHours(-1));
        await Reset(await Login("operator@example.com"), "RESET");
        using (var raw = InMemoryDb.Raw(_db)) Assert.Empty(raw.Users.Where(u => u.Email == "cheolsu@example.com"));   // 학생 계정도 지움

        // 다시 가입한 아이: 초기화 전 시각으로 찍힌 기록도 그대로 센다(초기화 시각을 남기지 않음 — 가짜 데이터 넣기가 바로 점수로 잡힌다)
        var again = InMemoryDb.SeedAccount(_db, "cheolsu2@example.com", "김철수");
        _p["cheolsu"] = InMemoryDb.SeedMembership(_db, _kids.Id, again.UserName!, MembershipStatus.Approved, Roles.Participant).UserName;
        using (var raw = InMemoryDb.Raw(_db)) { foreach (var u in raw.Users.Where(u => u.Email == "cheolsu2@example.com")) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, "1234"); raw.SaveChanges(); }
        Pts("cheolsu", 340, _time.Now.UtcDateTime.AddDays(-30));
        var kid = await Login("cheolsu2@example.com");
        Assert.Contains("340점", await kid.GetStringAsync("/kids/word-mission"));
    }
}
