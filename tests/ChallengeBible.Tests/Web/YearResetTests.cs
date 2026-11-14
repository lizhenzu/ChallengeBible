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

    private void SeedGiftsAndAward()
    {
        using var raw = InMemoryDb.Raw(_db);
        foreach (var state in new[] { GiftStates.Open, GiftStates.Picked, GiftStates.Purchased, GiftStates.Expired, GiftStates.Delivered })
            raw.Gifts.Add(new Gift { TenantId = _kids.Id, Kind = GiftKinds.Personal, State = state, Year = 2027, UserName = _p["cheolsu"], SourceKey = "P-" + state, MaxAmount = 5000, CreatedUtc = DateTime.UtcNow });
        raw.TeamAwards.Add(new TeamAward { TenantId = _kids.Id, Kind = TeamTypes.Family, AwardKey = "철수네", Threshold = 1000, AwardedAt = new DateTime(2026, 12, 20) });
        raw.SaveChanges();
    }

    [Fact]
    public async Task 정한것12_RESET_을_쓰고_누르면_끝나지_않은_선물은_닫히고_시상_기록은_옮겨져_비워진다()
    {
        SeedGiftsAndAward();
        var op = await Login("operator@example.com");
        var page = await op.GetStringAsync("/platform/year-reset");
        Assert.Contains("새해 초기화", page);

        var res = await Reset(op, "RESET");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(4, raw.Gifts.Count(g => g.State == GiftStates.YearClosed));
        Assert.Equal(GiftStates.Delivered, raw.Gifts.Single(g => g.SourceKey == "P-" + GiftStates.Delivered).State);   // 끝난 선물은 그대로
        Assert.Equal(4, raw.GiftLogs.Count(l => l.Action == GiftActions.YearClose));
        Assert.Empty(raw.TeamAwards);
        var arc = raw.TeamAwardArchives.Single();
        Assert.Equal((TeamTypes.Family, "철수네", 1000, (short)2026), (arc.Kind, arc.AwardKey, arc.Threshold, arc.Year));
        Assert.Equal(_time.Now.UtcDateTime, raw.GiftYearCloses.Single(c => c.Year == 2027).ClosedUtc);
        Assert.Equal("gifts=4;awards=1", raw.AuditLogs.Single(a => a.Action == YearResetService.Action).Detail);
    }

    [Fact]
    public async Task 정한것12_RESET_을_쓰지_않으면_아무것도_바뀌지_않는다()
    {
        SeedGiftsAndAward();
        var op = await Login("operator@example.com");
        var body = await (await Reset(op, "reset")).Content.ReadAsStringAsync();
        Assert.Contains("RESET 를 써야 초기화해요", body);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(0, raw.Gifts.Count(g => g.State == GiftStates.YearClosed));
        Assert.Single(raw.TeamAwards);
        Assert.Empty(raw.GiftYearCloses);
    }

    [Fact]
    public async Task 정한것12_플랫폼_관리자만_들어온다()
    {
        var admin = await Login("admin@example.com");   // 부서 관리자
        Assert.NotEqual(HttpStatusCode.OK, (await admin.GetAsync("/platform/year-reset")).StatusCode);
        var student = await Login("cheolsu@example.com");
        Assert.NotEqual(HttpStatusCode.OK, (await student.GetAsync("/platform/year-reset")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await Login("operator@example.com")).GetAsync("/platform/year-reset")).StatusCode);
    }

    [Fact]
    public async Task 정한것12_개인_선물_점수는_누른_순간부터_0에서_다시_센다_미션_기록은_그대로()
    {
        Pts("cheolsu", 340, _time.Now.UtcDateTime.AddHours(-1));
        var kid = await Login("cheolsu@example.com");
        Assert.Contains("340점", await kid.GetStringAsync("/kids/word-mission"));

        await Reset(await Login("operator@example.com"), "RESET");
        var home = await kid.GetStringAsync("/kids/word-mission");
        Assert.Contains("<span class=\"num\">100</span>점 더 모으면 돼요", home);   // 0점

        _time.Now = _time.Now.AddHours(1);
        Pts("cheolsu", 60, _time.Now.UtcDateTime);   // 누른 뒤 점수만 센다
        Assert.Contains("<span class=\"num\">40</span>점 더 모으면 돼요", await kid.GetStringAsync("/kids/word-mission"));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(400, raw.MissionActivities.Where(a => a.UserName == _p["cheolsu"]).Sum(a => a.Points));   // 미션 기록 · 순위는 그대로
    }

    [Fact]
    public async Task 정한것12_초기화하면_가족팀_선물이_같은_단계로_다시_생기고_여러_번_눌러도_된다()
    {
        FamilyTeam();
        Pts("cheolsu", 1100, _time.Now.UtcDateTime.AddHours(-1));
        var kid = await Login("cheolsu@example.com");
        Assert.Contains("가족 선물을 골라요", await kid.GetStringAsync("/kids/word-mission"));
        var op = await Login("operator@example.com");

        for (var round = 1; round <= 2; round++)
        {
            await Reset(op, "RESET");
            _time.Now = _time.Now.AddMinutes(1);
            Assert.Contains("가족 선물을 골라요", await kid.GetStringAsync("/kids/word-mission"));
            using var raw = InMemoryDb.Raw(_db);
            Assert.Equal(round, raw.Gifts.Count(g => g.State == GiftStates.YearClosed));
            var open = raw.Gifts.Single(g => g.State == GiftStates.Open);
            Assert.Equal((10000, 1000), (open.MaxAmount, TeamGiftService.StageOf(open.SourceKey)));
            Assert.StartsWith("T2027-1000-r", open.SourceKey);
        }
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(2, raw.AuditLogs.Count(a => a.Action == YearResetService.Action));
    }
}
