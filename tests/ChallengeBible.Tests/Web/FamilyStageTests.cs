using System.Net;
using System.Text.RegularExpressions;
using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChallengeBible.Tests.Web;

/// <summary>부서 시상 기준(R-219, ADR 0093): 1,000점 단위 10단계마다 선물 켜기 · 금액. 오늘 = 2027-01-05(서울) → 올해 2027, 내년 2028.</summary>
public partial class FamilyStageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private static readonly int[] Ten = [1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000, 10000];
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids, _elem;

    public FamilyStageTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
    }

    private (RequestScope Scope, TenantContext Tc) Ctx(Tenant t, SeededMember? viewer = null)
    {
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        if (viewer is not null) scope.SetUser(viewer.UserName, viewer.UserId);
        var tc = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db))
            tc.Set(raw.Tenants.Single(x => x.Id == t.Id), viewer is null ? null : raw.DeptRoles.Single(d => d.Id == viewer.Id));
        return (scope, tc);
    }

    private FamilyStageService In(Tenant t)
    {
        var (scope, tc) = Ctx(t);
        return new FamilyStageService(new InMemoryDb(_db, scope), tc, new TenantClock(_time), InMemoryDb.Rules(_db));
    }

    private static bool[] On(params int[] off) => Ten.Select(p => !off.Contains(p)).ToArray();
    private static string?[] Amounts(params (int Points, string Amount)[] set) => Ten.Select(p => set.FirstOrDefault(x => x.Points == p).Amount ?? "5,000").ToArray();

    [Fact]
    public async Task R219_정하지_않으면_10단계_모두_켬_5000원이다()
    {
        var b = await In(_kids).GetAsync(null);
        Assert.True(b.Enabled);
        Assert.Equal((2027, 2027, false, 5000), (b.ThisYear, b.Year, b.Own, b.DefaultAmount));
        Assert.Equal(Ten, b.Rows.Select(r => r.Points));
        Assert.All(b.Rows, r => Assert.Equal((true, 5000), (r.On, r.Amount)));
        Assert.Equal((10, 50000), (b.OnCount, b.OnTotal));
        Assert.Equal(1000, RuleSet.Default.FamilyAwardStep);   // 단위 · 단계 수 · 처음 금액은 규칙에서 읽는다
    }

    [Fact]
    public async Task R219_저장하면_우리_부서_기준이_되고_끈_단계는_빠진다()
    {
        var svc = In(_kids);
        Assert.Equal(StageError.None, (await svc.SaveAsync(2027, On(2000, 4000), Amounts((1000, "3,000"), (3000, "10000"), (10000, "30,000원")))).Error);
        var b = await svc.GetAsync(2027);
        Assert.True(b.Own);
        Assert.Equal(On(2000, 4000), b.Rows.Select(r => r.On));
        Assert.Equal([3000, 5000, 10000, 5000, 5000, 5000, 5000, 5000, 5000, 30000], b.Rows.Select(r => r.Amount));
        Assert.Equal(8, b.OnCount);
        using (var raw = InMemoryDb.Raw(_db))
            Assert.All(raw.FamilyMilestoneConfigs.Where(c => c.TenantId == _kids.Id), c => Assert.Equal(MilestoneTypes.Award, c.MilestoneType));   // 박수 단계는 없다

        // 다시 저장하면 줄을 고친다(늘어나지 않는다)
        await svc.SaveAsync(2027, On(), Amounts());
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(10, raw.FamilyMilestoneConfigs.Count(c => c.TenantId == _kids.Id && c.Year == 2027));
        Assert.Equal(10, (await svc.GetAsync(2027)).OnCount);
    }

    [Fact]
    public async Task R219_금액은_0에서_100만원_숫자만이고_틀리면_아무것도_저장하지_않는다()
    {
        var svc = In(_kids);
        var (e, bad) = await svc.SaveAsync(2027, On(), Amounts((1000, "만원"), (3000, "1000001"), (5000, "-1")));
        Assert.Equal(StageError.BadAmount, e);
        Assert.Equal([0, 2, 4], bad);
        Assert.False((await svc.GetAsync(2027)).Own);

        // 끈 단계의 금액은 비거나 틀려도 된다(처음 금액을 남긴다)
        Assert.Equal(StageError.None, (await svc.SaveAsync(2027, On(2000), Amounts((2000, "")))).Error);
        Assert.Equal(5000, (await svc.GetAsync(2027)).Rows[1].Amount);
        Assert.Equal(StageError.None, (await svc.SaveAsync(2027, On(), Amounts((1000, "0")))).Error);   // 0원도 된다
        Assert.Equal(StageError.BadForm, (await svc.SaveAsync(2027, [true, true], ["1", "2"])).Error);
    }

    [Fact]
    public async Task R219_올해와_내년만이고_부서마다_따로다()
    {
        var svc = In(_kids);
        Assert.Equal(StageError.BadYear, (await svc.SaveAsync(2026, On(), Amounts())).Error);
        Assert.Equal(StageError.BadYear, (await svc.SaveAsync(2029, On(), Amounts())).Error);
        Assert.Equal(StageError.None, (await svc.SaveAsync(2028, On(1000), Amounts())).Error);
        Assert.False((await svc.GetAsync(2027)).Own);
        Assert.True((await svc.GetAsync(2028)).Own);
        Assert.Equal(2027, (await svc.GetAsync(1999)).Year);   // 고를 수 없는 해는 올해로
        Assert.False((await In(_elem).GetAsync(2028)).Own);
    }

    [Fact]
    public async Task R219_받은_팀_수를_보여_주고_받은_단계도_끄거나_금액을_바꿀_수_있다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.TeamAwards.Add(new TeamAward { TenantId = _kids.Id, Kind = TeamTypes.Family, AwardKey = "철수네", Threshold = 3000, AwardedAt = new DateTime(2027, 1, 4) });
            raw.TeamAwards.Add(new TeamAward { TenantId = _kids.Id, Kind = TeamTypes.Friend, AwardKey = "단짝", Threshold = 3000, AwardedAt = new DateTime(2027, 1, 4) });
            raw.TeamAwards.Add(new TeamAward { TenantId = _kids.Id, Kind = TeamTypes.Family, AwardKey = "하늘네", Threshold = 3000, AwardedAt = new DateTime(2026, 5, 1) });   // 지난해
            raw.SaveChanges();
        }
        var svc = In(_kids);
        Assert.Equal(2, (await svc.GetAsync(2027)).Rows[2].Given);
        Assert.Equal(StageError.None, (await svc.SaveAsync(2027, On(3000), Amounts((3000, "1,000")))).Error);
        Assert.False((await svc.GetAsync(2027)).Rows[2].On);
    }

    [Fact]
    public async Task R219_부서_기준이_가족팀_축하_단계로_쓰인다()
    {
        var kid = InMemoryDb.SeedAccount(_db, "cheolsu@example.com", "김철수");
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var t = new FamilyTeam { TenantId = _kids.Id, Name = "철수네", InsertDT = DateTime.UtcNow, UpdateDT = DateTime.UtcNow };
            t.Members.Add(new FamilyMember { TenantId = _kids.Id, UserName = m.UserName, Relation = FamilyRelation.Student });
            raw.FamilyTeams.Add(t);
            raw.MissionActivities.Add(new MissionActivity { TenantId = _kids.Id, UserName = m.UserName, MissionDate = new DateOnly(2027, 1, 3), MissionItem = "Word.Me", Points = 1500, PerformedAt = DateTime.UtcNow });
            raw.SaveChanges();
        }
        FamilyTeamService Teams()
        {
            var (scope, tc) = Ctx(_kids, m);
            return new FamilyTeamService(new InMemoryDb(_db, scope), scope, tc, new TenantClock(_time), InMemoryDb.Rules(_db));
        }
        var home = await Teams().MyFamilyAsync();
        Assert.Equal(Ten, home!.Stages.Select(s => s.Points));
        Assert.All(home.Stages, s => Assert.Equal(FamilyMilestoneKind.Award, s.Kind));
        Assert.Equal(2000, home.NextStage);

        await In(_kids).SaveAsync(2027, On(2000, 3000), Amounts());
        home = await Teams().MyFamilyAsync();
        Assert.Equal([1000, 4000, 5000, 6000, 7000, 8000, 9000, 10000], home!.Stages.Select(s => s.Points));
        Assert.Equal(4000, home.NextStage);   // 끈 단계는 건너뛴다
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> LoggedIn(string email)
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            var a = raw.Users.Single(x => x.Email == email);
            a.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<AppUser>().HashPassword(a, "1234");
            raw.SaveChanges();
        }
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var h = await c.GetStringAsync("/account/login");
        await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(h).Groups[1].Value, ["Email"] = email, ["Password"] = "1234",
        }));
        return c;
    }

    [Fact]
    public async Task R219_화면_흐름_관리자가_저장하고_참여자는_못_본다()
    {
        var admin = InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜");
        InMemoryDb.SeedMembership(_db, _kids.Id, admin.UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        var c = await LoggedIn("eunhye@example.com");

        Assert.Contains("href=\"/jamsil-kids/admin/teams/stages\"", await c.GetStringAsync("/jamsil-kids/admin/teams"));
        var page = await c.GetStringAsync("/jamsil-kids/admin/teams/stages");
        Assert.Contains("처음 값을 쓰고 있어요", page);
        Assert.Contains("aria-current=\"page\">가족팀", page);
        Assert.Equal(10, Regex.Matches(page, "name=\"amount\"").Count);

        var form = new List<KeyValuePair<string, string>> { new("__RequestVerificationToken", TokenRx().Match(page).Groups[1].Value), new("year", "2027") };
        for (var i = 0; i < 10; i++)
        {
            if (i != 1) form.Add(new("on", i.ToString()));
            form.Add(new("amount", i == 4 ? "만원" : "5,000"));
        }
        var bad = await c.PostAsync("/jamsil-kids/admin/teams/stages", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        var badHtml = await bad.Content.ReadAsStringAsync();
        Assert.Contains("빨간 칸을 확인해 주세요", badHtml);
        Assert.Contains("value=\"만원\"", badHtml);

        form[form.FindIndex(x => x.Key == "amount" && x.Value == "만원")] = new("amount", "10,000");
        var ok = await c.PostAsync("/jamsil-kids/admin/teams/stages", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        page = await c.GetStringAsync(ok.Headers.Location!.OriginalString);
        Assert.Contains("저장했어요", page);
        Assert.Contains("9단계에서 선물을 줘요. 2,000점은 선물이 없어요.", page);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(10000, raw.FamilyMilestoneConfigs.Single(x => x.PointThreshold == 5000).GiftAmount);

        Assert.Equal(10, Regex.Matches(await c.GetStringAsync("/jamsil-kids/admin/teams/stages?D=true"), "value=\"5,000\"").Count);   // 처음 값으로(저장 안 함)
        Assert.Contains("2028 · 아직 정하지 않았어요", await c.GetStringAsync("/jamsil-kids/admin/teams/stages?Y=2028"));

        var kid = InMemoryDb.SeedAccount(_db, "cheolsu@example.com", "김철수");
        InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        var kc = await LoggedIn("cheolsu@example.com");
        Assert.StartsWith("/account/denied", (await kc.GetAsync("/jamsil-kids/admin/teams/stages")).Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task R219_가족팀을_끄면_시상_기준도_막힌다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.PlatformRuleSetVersions.Add(new PlatformRuleSetVersion { VersionNo = 2, EffectiveFrom = Jan5,
                Values = RuleService.ToJson(RuleSet.Default with { Features = RuleSet.Default.Features with { FamilyTeam = false } }), ChangedByUserName = "t" });
            raw.SaveChanges();
        }
        Assert.False((await In(_kids).GetAsync(null)).Enabled);
        Assert.Equal(StageError.Disabled, (await In(_kids).SaveAsync(2027, On(), Amounts())).Error);
        var admin = InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜");
        InMemoryDb.SeedMembership(_db, _kids.Id, admin.UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        var c = await LoggedIn("eunhye@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/jamsil-kids/admin/teams/stages")).StatusCode);
    }
}
