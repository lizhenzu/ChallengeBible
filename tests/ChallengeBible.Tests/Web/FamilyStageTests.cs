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

/// <summary>부서 시상 기준(R-219, ADR 0091). 오늘 = 2027-01-05(서울) → 올해 2027, 내년 2028.</summary>
public partial class FamilyStageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
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

    private int IdOf(int year, int points)
    {
        using var raw = InMemoryDb.Raw(_db);
        return raw.FamilyMilestoneConfigs.Single(c => c.TenantId == _kids.Id && c.Year == year && c.PointThreshold == points).Id;
    }

    private void Award(int points, DateTime at)
    {
        using var raw = InMemoryDb.Raw(_db);
        raw.TeamAwards.Add(new TeamAward { TenantId = _kids.Id, Kind = TeamTypes.Family, AwardKey = "철수네", Threshold = points, AwardedAt = at });
        raw.SaveChanges();
    }

    [Fact]
    public async Task R219_정하지_않으면_기본값이고_정하기는_복사_되돌리기는_지운다()
    {
        var svc = In(_kids);
        var b = await svc.GetAsync(null);
        Assert.True(b.Enabled);
        Assert.Equal((2027, 2027, false), (b.ThisYear, b.Year, b.Own));
        Assert.Equal([1000, 2000, 3000, 4000, 5000], b.Rows.Select(r => r.Points));
        Assert.All(b.Rows, r => Assert.Null(r.Id));                        // 기본값은 읽기만

        Assert.Equal(StageError.NotOwn, await svc.AddAsync(2027, 600, MilestoneTypes.Award, "육백", "🎉"));
        Assert.Equal(StageError.None, await svc.OwnAsync(2027));
        b = await svc.GetAsync(2027);
        Assert.True(b.Own);
        Assert.Equal(RuleSet.Default.FamilyMilestones.Select(m => (m.Points, m.Message)), b.Rows.Select(r => (r.Points, r.Title)));
        Assert.Equal([MilestoneTypes.Award, MilestoneTypes.Applause], b.Rows.Take(2).Select(r => r.Type));
        Assert.Equal(StageError.AlreadyOwn, await svc.OwnAsync(2027));

        Assert.False((await svc.GetAsync(2028)).Own);                       // 내년은 따로
        Assert.Equal(StageError.BadYear, await svc.OwnAsync(2026));         // 올해와 내년만
        Assert.Equal(StageError.BadYear, await svc.OwnAsync(2029));
        Assert.False((await In(_elem).GetAsync(2027)).Own);                 // 다른 부서는 그대로

        Assert.Equal(StageError.None, await svc.ResetAsync(2027));
        Assert.False((await svc.GetAsync(2027)).Own);
        Assert.Equal(StageError.NotOwn, await svc.ResetAsync(2027));
    }

    [Fact]
    public async Task R219_더하기와_고치기는_점수_문구_그림을_검사하고_같은_점수를_막는다()
    {
        var svc = In(_kids);
        await svc.OwnAsync(2027);
        Assert.Equal(StageError.Duplicate, await svc.AddAsync(2027, 1000, MilestoneTypes.Award, "또", "🎉"));
        Assert.Equal(StageError.BadPoints, await svc.AddAsync(2027, 0, MilestoneTypes.Award, "영", "🎉"));
        Assert.Equal(StageError.BadPoints, await svc.AddAsync(2027, 100_001, MilestoneTypes.Award, "많이", "🎉"));
        Assert.Equal(StageError.BadType, await svc.AddAsync(2027, 600, "BIG", "육백", "🎉"));
        Assert.Equal(StageError.BadTitle, await svc.AddAsync(2027, 600, MilestoneTypes.Award, "  ", "🎉"));
        Assert.Equal(StageError.BadTitle, await svc.AddAsync(2027, 600, MilestoneTypes.Award, new string('가', 51), "🎉"));
        Assert.Equal(StageError.BadEmoji, await svc.AddAsync(2027, 600, MilestoneTypes.Award, "육백", ""));
        Assert.Equal(StageError.BadEmoji, await svc.AddAsync(2027, 600, MilestoneTypes.Award, "육백", "123456789"));
        Assert.Equal(StageError.None, await svc.AddAsync(2027, 600, MilestoneTypes.Applause, " 육백 돌파 ", "👏"));
        var six = (await svc.GetAsync(2027)).Rows.Single(r => r.Points == 600);
        Assert.Equal(("육백 돌파", MilestoneTypes.Applause, true), (six.Title, six.Type, six.On));

        var id = IdOf(2027, 600);
        Assert.Equal(StageError.Duplicate, await svc.UpdateAsync(2027, id, 2000, MilestoneTypes.Award, "이천", "🎉"));
        Assert.Equal(StageError.None, await svc.UpdateAsync(2027, id, 1500, MilestoneTypes.Award, "천오백", "🏆"));
        Assert.Equal([1000, 1500, 2000, 3000, 4000, 5000], (await svc.GetAsync(2027)).Rows.Select(r => r.Points));
        Assert.Equal(StageError.NotFound, await svc.UpdateAsync(2027, 99999, 700, MilestoneTypes.Award, "칠백", "🎉"));
    }

    [Fact]
    public async Task R219_켜진_단계는_하나_이상이어야_한다()
    {
        var svc = In(_kids);
        await svc.OwnAsync(2027);
        foreach (var p in new[] { 1000, 2000, 3000, 4000 }) Assert.Equal(StageError.None, await svc.ToggleAsync(2027, IdOf(2027, p), false));
        var last = IdOf(2027, 5000);
        Assert.Equal(StageError.LastOn, await svc.ToggleAsync(2027, last, false));
        Assert.Equal(StageError.LastOn, await svc.DeleteAsync(2027, last));
        Assert.Equal(StageError.None, await svc.DeleteAsync(2027, IdOf(2027, 1000)));   // 꺼진 단계는 지울 수 있다
        Assert.Equal(1, (await svc.GetAsync(2027)).OnCount);
        Assert.Equal(StageError.None, await svc.ToggleAsync(2027, IdOf(2027, 2000), true));
        Assert.Equal(StageError.None, await svc.ToggleAsync(2027, last, false));
    }

    [Fact]
    public async Task R219_그해_받은_단계는_점수를_바꾸거나_지우거나_되돌릴_수_없다()
    {
        var svc = In(_kids);
        await svc.OwnAsync(2027);
        Award(3000, new DateTime(2026, 12, 20));   // 지난해 시상은 올해 기준을 잠그지 않는다
        Assert.DoesNotContain((await svc.GetAsync(2027)).Rows, r => r.Locked);

        Award(3000, new DateTime(2027, 1, 4));
        var row = (await svc.GetAsync(2027)).Rows.Single(r => r.Points == 3000);
        Assert.Equal((true, 1), (row.Locked, row.Given));
        var id = row.Id!.Value;
        Assert.Equal(StageError.Locked, await svc.UpdateAsync(2027, id, 3500, MilestoneTypes.Award, "삼천오백", "🎉"));
        Assert.Equal(StageError.None, await svc.UpdateAsync(2027, id, 3000, MilestoneTypes.Applause, "삼천! 박수", "👏"));   // 문구 · 그림 · 종류는 된다
        Assert.Equal(StageError.Locked, await svc.DeleteAsync(2027, id));
        Assert.Equal(StageError.Locked, await svc.ResetAsync(2027));
        Assert.Equal(StageError.None, await svc.ToggleAsync(2027, id, false));                                         // 끄기는 된다

        using (var raw = InMemoryDb.Raw(_db))   // 숨김 기록이 있는 단계도 잠긴다
        {
            raw.FamilyMilestonesHidden.Add(new FamilyMilestoneHidden { TenantId = _kids.Id, MilestoneConfigId = IdOf(2027, 1000), FamilyTeamId = 1, HiddenAt = DateTime.UtcNow });
            raw.SaveChanges();
        }
        Assert.Equal(StageError.Locked, await svc.DeleteAsync(2027, IdOf(2027, 1000)));
    }

    [Fact]
    public async Task R219_우리_부서_기준이_가족팀_축하_단계로_쓰인다()
    {
        var kid = InMemoryDb.SeedAccount(_db, "cheolsu@example.com", "김철수");
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var t = new FamilyTeam { TenantId = _kids.Id, Name = "철수네", InsertDT = DateTime.UtcNow, UpdateDT = DateTime.UtcNow };
            t.Members.Add(new FamilyMember { TenantId = _kids.Id, UserName = m.UserName, Relation = FamilyRelation.Student });
            raw.FamilyTeams.Add(t);
            raw.MissionActivities.Add(new MissionActivity { TenantId = _kids.Id, UserName = m.UserName, MissionDate = new DateOnly(2027, 1, 3), MissionItem = "Word.Me", Points = 700, PerformedAt = DateTime.UtcNow });
            raw.SaveChanges();
        }
        var svc = In(_kids);
        await svc.OwnAsync(2027);
        await svc.AddAsync(2027, 500, MilestoneTypes.Applause, "오백 돌파", "👏");
        await svc.ToggleAsync(2027, IdOf(2027, 2000), false);

        var (scope, tc) = Ctx(_kids, m);
        var home = await new FamilyTeamService(new InMemoryDb(_db, scope), scope, tc, new TenantClock(_time), InMemoryDb.Rules(_db)).MyFamilyAsync();
        Assert.Equal([500, 1000, 3000, 4000, 5000], home!.Stages.Select(s => s.Points));   // 꺼진 2,000 은 빠진다
        Assert.Equal([true, false, false, false, false], home.Stages.Select(s => s.On));
        Assert.Equal("오백 돌파", home.Stages[0].Message);
        Assert.Equal(1000, home.NextStage);
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

    private static Task<HttpResponseMessage> Post(HttpClient c, string page, string handler, Dictionary<string, string> form)
    {
        form["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value;
        return c.PostAsync("/jamsil-kids/admin/teams/stages?handler=" + handler, new FormUrlEncodedContent(form));
    }

    [Fact]
    public async Task R219_화면_흐름_관리자가_정하고_더하고_참여자는_못_본다()
    {
        var admin = InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜");
        InMemoryDb.SeedMembership(_db, _kids.Id, admin.UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        var c = await LoggedIn("eunhye@example.com");

        Assert.Contains("href=\"/jamsil-kids/admin/teams/stages\"", await c.GetStringAsync("/jamsil-kids/admin/teams"));   // 가족팀 화면의 "시상 기준"
        var page = await c.GetStringAsync("/jamsil-kids/admin/teams/stages");
        Assert.Contains("기본값을 쓰고 있어요", page);
        Assert.Contains("aria-current=\"page\">가족팀", page);   // 관리 메뉴는 가족팀

        var own = await Post(c, page, "Own", new() { ["year"] = "2027" });
        Assert.Equal(HttpStatusCode.Redirect, own.StatusCode);
        page = await c.GetStringAsync(own.Headers.Location!.OriginalString);
        Assert.Contains("우리 부서 기준", page);
        Assert.Contains("기본값을 복사했어요", page);

        var dup = await Post(c, page, "Add", new() { ["year"] = "2027", ["APoints"] = "3000", ["AType"] = MilestoneTypes.Award, ["ATitle"] = "또 삼천", ["AEmoji"] = "🎉" });
        Assert.Equal(HttpStatusCode.OK, dup.StatusCode);
        var dupHtml = await dup.Content.ReadAsStringAsync();
        Assert.Contains("같은 점수의 단계가 이미 있어요", dupHtml);
        Assert.Contains("value=\"또 삼천\"", dupHtml);   // 쓴 값은 그대로

        var add = await Post(c, page, "Add", new() { ["year"] = "2027", ["APoints"] = "6000", ["AType"] = MilestoneTypes.Award, ["ATitle"] = "6,000점! 대단해요", ["AEmoji"] = "🏅" });
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(6, raw.FamilyMilestoneConfigs.Count(x => x.TenantId == _kids.Id && x.Year == 2027));

        var next = await c.GetStringAsync("/jamsil-kids/admin/teams/stages?Y=2028");
        Assert.Contains("2028 · 아직 정하지 않았어요", next);
        Assert.Contains("2027 · 우리 부서 기준", await c.GetStringAsync("/jamsil-kids/admin/teams/stages?Y=1999"));   // 고를 수 없는 해는 올해로

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
        Assert.Equal(StageError.Disabled, await In(_kids).OwnAsync(2027));

        var admin = InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜");
        InMemoryDb.SeedMembership(_db, _kids.Id, admin.UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        var c = await LoggedIn("eunhye@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/jamsil-kids/admin/teams/stages")).StatusCode);
    }
}
