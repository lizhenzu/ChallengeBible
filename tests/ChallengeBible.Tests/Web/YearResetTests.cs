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
    public async Task 정한것17_RESET_을_쓰고_누르면_미션_출석_선물_시상_감사로그를_지우고_새친구_출석_완독_팀구성은_그대로()
    {
        SeedEverything();
        FamilyTeam();
        var op = await Login("operator@example.com");
        var page = await op.GetStringAsync("/platform/year-reset");
        Assert.Contains("되돌릴 수 없어요", page);

        var body = await (await Reset(op, "RESET")).Content.ReadAsStringAsync();
        Assert.Contains("초기화했어요", body);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.MissionActivities);
        Assert.Empty(raw.TeamAwards);
        Assert.Empty(raw.TeamAwardArchives);
        Assert.Empty(raw.GiftPointUses);
        Assert.Equal(GiftKinds.Reading, raw.Gifts.Single().Kind);   // 완독 선물만 남는다
        Assert.Equal(raw.Gifts.Single().Id, raw.GiftLogs.Single().GiftId);   // 완독 선물 이력도 그대로
        var nc = raw.People.Single(p => p.Name == "새친구");
        Assert.Equal(4, raw.AttendanceChecks.Count(a => a.PersonId == nc.Id));   // 새친구 출석 그대로
        Assert.Equal(4, raw.AttendanceChecks.Count());   // 새친구가 아닌 아이 출석은 지움
        Assert.NotNull(nc.NewcomerFirstVisit);
        Assert.Single(raw.ReadingRounds);   // 성경통독 그대로
        Assert.Single(raw.FamilyTeams);   // 팀 구성 그대로
        Assert.Equal(2, raw.FamilyMembers.Count());
        var audit = raw.AuditLogs.Single();   // 감사 로그는 이번 초기화 한 줄만
        Assert.Equal((YearResetService.Action, "missions=1;attend=1;gifts=6;awards=2;audits=1"), (audit.Action, audit.Detail));
        Assert.Equal(_time.Now.UtcDateTime, raw.GiftYearCloses.Single(c => c.Year == 2027).ClosedUtc);
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
    public async Task 정한것17_개인_선물_점수는_0부터_다시_센다()
    {
        Pts("cheolsu", 340, _time.Now.UtcDateTime.AddHours(-1));
        var kid = await Login("cheolsu@example.com");
        Assert.Contains("340점", await kid.GetStringAsync("/kids/word-mission"));

        await Reset(await Login("operator@example.com"), "RESET");
        Assert.Contains("<span class=\"num\">100</span>점 더 모으면 돼요", await kid.GetStringAsync("/kids/word-mission"));   // 0점

        _time.Now = _time.Now.AddHours(1);
        Pts("cheolsu", 60, _time.Now.UtcDateTime);
        Assert.Contains("<span class=\"num\">40</span>점 더 모으면 돼요", await kid.GetStringAsync("/kids/word-mission"));
    }

    [Fact]
    public async Task 정한것17_가족팀_점수가_0이_되어_선물이_다시_생기지_않고_다시_넘으면_생긴다()
    {
        FamilyTeam();
        Pts("cheolsu", 1100, _time.Now.UtcDateTime.AddHours(-1));
        var kid = await Login("cheolsu@example.com");
        Assert.Contains("가족 선물을 골라요", await kid.GetStringAsync("/kids/word-mission"));
        await Reset(await Login("operator@example.com"), "RESET");
        Assert.DoesNotContain("가족 선물을 골라요", await kid.GetStringAsync("/kids/word-mission"));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Empty(raw.Gifts);

        _time.Now = _time.Now.AddMinutes(1);
        Pts("cheolsu", 1000, _time.Now.UtcDateTime);
        Assert.Contains("가족 선물을 골라요", await kid.GetStringAsync("/kids/word-mission"));
    }

    [Fact]
    public async Task 정한것17_새친구_전도_선물은_초기화_뒤에_4번째_출석을_채운_새친구만()
    {
        SeedEverything();
        var admin = await Login("admin@example.com");
        await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(2, raw.Gifts.Count(g => g.Kind == GiftKinds.Newcomer || g.Kind == GiftKinds.Invite));   // 초기화 전: 새친구 · 전도

        await Reset(await Login("operator@example.com"), "RESET");
        await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        using (var raw = InMemoryDb.Raw(_db)) Assert.Empty(raw.Gifts.Where(g => g.Kind == GiftKinds.Newcomer || g.Kind == GiftKinds.Invite));   // 4번째 출석(12월 27일)이 초기화(1월 5일) 전 → 다시 안 생김

        int nc;
        using (var raw = InMemoryDb.Raw(_db))
        {
            var p = new Person { TenantId = _kids.Id, Name = "새친구2", RoleCode = "T1020", NewcomerFirstVisit = new DateOnly(2026, 12, 20), NewcomerVoluntary = true, CreatedUtc = DateTime.UtcNow };
            raw.People.Add(p); raw.SaveChanges(); nc = p.Id;
            foreach (var d in new[] { new DateOnly(2026, 12, 20), new DateOnly(2026, 12, 27), new DateOnly(2027, 1, 3), new DateOnly(2027, 1, 10) })
                raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = nc, Sunday = d, CreatedUtc = DateTime.UtcNow });
            raw.SaveChanges();
        }
        _time.Now = new DateTimeOffset(2027, 1, 10, 1, 0, 0, TimeSpan.Zero);
        await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(nc, raw.Gifts.Single(g => g.Kind == GiftKinds.Newcomer).PersonId);   // 4번째가 초기화 뒤(1월 10일) → 생김
    }

    [Fact]
    public async Task 정한것17_생일_선물은_고르는_기간이_초기화_뒤에_시작된_아이만()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.People.Add(new Person { TenantId = _kids.Id, Name = "생일이", RoleCode = "T1001", BirthMonth = 2, BirthDay = 1, CreatedUtc = DateTime.UtcNow });   // 기간 1월 2일 ~
            raw.People.Add(new Person { TenantId = _kids.Id, Name = "생일둘", RoleCode = "T1001", BirthMonth = 2, BirthDay = 10, CreatedUtc = DateTime.UtcNow });  // 기간 1월 11일 ~
            raw.SaveChanges();
        }
        var admin = await Login("admin@example.com");
        await Reset(await Login("operator@example.com"), "RESET");   // 1월 5일
        await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        using (var raw = InMemoryDb.Raw(_db)) Assert.Empty(raw.Gifts);   // 생일이: 기간이 초기화 전에 시작 → 안 생김

        _time.Now = new DateTimeOffset(2027, 1, 12, 0, 0, 0, TimeSpan.Zero);
        await admin.GetStringAsync("/kids/admin/gifts?s=pick");
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal("B2027-0210-r270105000000", raw.Gifts.Single().SourceKey);   // 생일둘만
    }
}
