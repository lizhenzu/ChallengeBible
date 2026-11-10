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

/// <summary>가족팀 보기 · 관리자 바로잡기(R-90 ~ R-97, 0012 · 0083). 만들기 · 합류는 FamilyJoinTests. 오늘 = 2027-01-05(서울).</summary>
public partial class FamilyTeamTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids, _elem;
    private readonly Dictionary<string, (string UserName, SeededMember M, string Pid)> _p = [];

    public FamilyTeamTests(WebApplicationFactory<Program> app)
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
        Add(_kids, "cheolsu", "김철수", [Roles.Participant], "사랑반");
        Add(_kids, "younghee", "김영희", [Roles.Participant, Roles.Guardian]);
        Add(_kids, "daeho", "김대호", [Roles.Participant, Roles.Guardian]);
        Add(_kids, "haneul", "문하늘", [Roles.Participant], "믿음반");
        Add(_elem, "jiho", "윤지호", [Roles.Participant]);                                     // 다른 부서 참여자
        Pts("cheolsu", 600);
        Pts("younghee", 300);
        Pts("daeho", 150);
        Pts("haneul", 40);
    }

    private void Add(Tenant t, string login, string name, string[] roles, string? group = null)
    {
        var a = InMemoryDb.SeedAccount(_db, login + "@example.com", name).UserName!;
        var m = InMemoryDb.SeedMembership(_db, t.Id, a, MembershipStatus.Approved, roles);
        using var raw = InMemoryDb.Raw(_db);
        raw.SaveChanges();
        _p[login] = (a, m, m.UserName);
    }

    private void Pts(string login, int points)
    {
        using var raw = InMemoryDb.Raw(_db);
        var p = (TenantId: _p[login].M.TenantId, Id: _p[login].Pid!);
        raw.MissionActivities.Add(new MissionActivity { TenantId = p.TenantId, UserName = p.Id, MissionDate = new DateOnly(2027, 1, 3), MissionItem = "Word.Me", Points = points, PerformedAt = DateTime.UtcNow });
        raw.SaveChanges();
    }

    private FamilyTeamService In(Tenant t, string? viewer = null)
    {
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        if (viewer is not null) scope.SetUser(_p[viewer].Pid, _p[viewer].M.UserId);
        var tc = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db))
            tc.Set(raw.Tenants.Single(x => x.Id == t.Id),
                viewer is null ? null : raw.DeptRoles.Single(d => d.Id == _p[viewer].M.Id));
        return new FamilyTeamService(new InMemoryDb(_db, scope), scope, tc, new TenantClock(_time), InMemoryDb.Rules(_db));
    }

    private string P(string login) => _p[login].Pid;

    /// <summary>팀 시드. 관계: 학생은 학생, 보호자는 엄마.</summary>
    private int Team(string? name, params string[] logins)
    {
        using var raw = InMemoryDb.Raw(_db);
        var t = new FamilyTeam { TenantId = _kids.Id, Name = name ?? "", InsertDT = DateTime.UtcNow, UpdateDT = DateTime.UtcNow };
        byte order = 0;
        foreach (var l in logins)
            t.Members.Add(new FamilyMember { TenantId = _kids.Id, UserName = P(l), SortOrder = order++,
                Relation = l is "younghee" or "daeho" ? FamilyRelation.Mother : FamilyRelation.Student });
        raw.FamilyTeams.Add(t);
        raw.SaveChanges();
        return t.Id;
    }

    [Fact]
    public async Task R92_R93_합계는_구성원_시즌_점수의_합_이름이_비면_대표_학생_가족()
    {
        var svc = In(_kids);
        var id = Team(null, "younghee", "cheolsu", "daeho");
        var team = Assert.Single((await svc.GetAsync()).Teams);
        Assert.Equal(1050, team.Points);
        Assert.Equal("김철수 가족", team.DisplayName);   // 첫 구성원이 엄마여도 대표는 학생(R-93)

        Assert.Equal(FamilyError.None, await svc.RenameAsync(id, "철수네"));
        Assert.Equal("철수네", (await svc.GetAsync()).Teams.Single().DisplayName);
    }

    [Fact]
    public async Task R210_관리자가_구성원을_빼면_점수는_그대로_마지막_사람이면_팀이_사라진다()
    {
        var svc = In(_kids);
        var id = Team("철수네", "cheolsu", "younghee");
        var board = await svc.GetAsync();
        Assert.DoesNotContain(board.Solo, s => s.Name == "김철수");
        var team = board.Teams.Single();

        Assert.Equal(FamilyError.None, await svc.RemoveMemberAsync(id, team.Members.Single(m => m.Name == "김영희").MemberId));
        board = await svc.GetAsync();
        Assert.Contains(board.Solo, s => s.Name == "김영희" && s.Points == 300);
        Assert.Equal(600, board.Teams.Single().Points);

        Assert.Equal(FamilyError.None, await svc.RemoveMemberAsync(id, board.Teams.Single().Members.Single().MemberId));
        Assert.Empty((await svc.GetAsync()).Teams);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.FamilyTeams);
    }

    [Fact]
    public async Task R131_다른_부서의_팀은_보이지도_고쳐지지도_않는다()
    {
        var id = Team(null, "cheolsu");
        var other = In(_elem);
        Assert.Empty((await other.GetAsync()).Teams);
        Assert.Equal(FamilyError.NotFound, await other.RenameAsync(id, "가로채기"));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal("", raw.FamilyTeams.Single().Name);
    }

    [Fact]
    public async Task R92_R94_순위_가족팀은_팀_점수순_마지막_참여일과_다음_가족_축하()
    {
        Team(null, "cheolsu", "younghee", "daeho");   // 1,050점
        Team("하늘이네", "haneul");                           // 40점
        var r = await In(_kids, "cheolsu").RankingAsync();
        Assert.True(r.Enabled);
        Assert.Equal(["김철수 가족", "하늘이네"], r.Teams.Select(t => t.Name));
        Assert.Equal([1050, 40], r.Teams.Select(t => t.Points));
        Assert.Equal((2000, (int?)1000), (r.Teams[0].NextStage!.Value, r.Teams[1].NextStage));
        Assert.Equal(new DateOnly(2027, 1, 3), r.Teams[0].Last);
        Assert.Equal((true, false), (r.Teams[0].Mine, r.Teams[1].Mine));
        Assert.True(r.Teams[0].Members.Single(m => m.Name == "김철수").Me);
        Assert.Equal((1000, 1), (r.FirstStage, r.ReachedFirst));
    }

    [Fact]
    public async Task 홈_우리_가족팀은_구성원에게만_오늘_점수와_한_미션을_보여_준다()
    {
        Team(null, "cheolsu", "younghee");
        using (var raw = InMemoryDb.Raw(_db))
        {
            var act = new MissionActivity { TenantId = _kids.Id, UserName = P("younghee"), MissionItem = "Word.Me", MissionDate = Jan5, Points = 1 };
            raw.MissionActivities.Add(act);
            raw.SaveChanges();
        }
        var home = await In(_kids, "cheolsu").MyFamilyAsync();
        Assert.NotNull(home);
        Assert.Equal(("김철수 가족", 901, 1, 1), (home.Name, home.Total, home.TodayPoints, home.TodayPeople));
        Assert.Equal((1000, 0), (home.NextStage!.Value, home.PrevStage));   // 첫 단계 전
        var mom = home.Members.Single(m => m.Name == "김영희");
        Assert.Equal((1, 1, false), (mom.Today, mom.TodayMissions, mom.Me));
        Assert.True(home.Members.Single(m => m.Name == "김철수").Me);
        Assert.Null(await In(_kids, "haneul").MyFamilyAsync());   // 팀이 없는 사람
    }

    [Fact]
    public async Task R96_R97_이번_점수로_넘은_단계만_축하하고_홈_카드는_숨긴_단계를_빼고_보인다()
    {
        var id = Team(null, "cheolsu", "younghee", "daeho");   // 1,050점
        var c = await In(_kids).CelebrationAsync(P("cheolsu"), 150);
        Assert.NotNull(c);
        Assert.Equal((1000, 1050, (int?)2000, "Award"), (c.Points, c.Total, c.Next, c.Kind));
        Assert.Equal(3, c.Members.Count);
        Assert.Equal("김철수", c.By);                                       // 시안 "○○님이 넘겼어요"
        Assert.Null(await In(_kids).CelebrationAsync(P("younghee"), 10));   // 이번 점수로 넘지 않았으면 없다
        Assert.Null(await In(_kids).CelebrationAsync(P("haneul"), 10));     // 팀이 없는 사람

        var home = await In(_kids, "cheolsu").MyFamilyAsync();
        Assert.Equal(1000, home!.Cheer!.Points);
        Assert.Equal([true, false, false, false, false, false, false, false, false, false], home.Stages.Select(s => s.On));   // 1,000점 단위 10단계(0093)
        using (var raw = InMemoryDb.Raw(_db))   // 예전에 관리자가 숨긴 카드는 숨긴 채로
        {
            var cfg = new FamilyMilestoneConfig { TenantId = _kids.Id, Year = 2027, PointThreshold = 1000, MilestoneType = MilestoneTypes.Award, DisplayTitle = "첫 1,000점", CreatedAt = DateTime.UtcNow };
            raw.FamilyMilestoneConfigs.Add(cfg);
            raw.SaveChanges();
            raw.FamilyMilestonesHidden.Add(new FamilyMilestoneHidden { TenantId = _kids.Id, MilestoneConfigId = cfg.Id, FamilyTeamId = id, HiddenAt = DateTime.UtcNow });
            raw.SaveChanges();
        }
        Assert.Null((await In(_kids, "cheolsu").MyFamilyAsync())!.Cheer);                     // R-97 숨긴 단계는 카드에서 빠짐
        Assert.Equal([1000], (await In(_kids).RankingAsync()).Teams[0].Badges.Select(b => b.Points));   // 순위 배지는 그대로
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
    public async Task 화면_흐름_관리자는_이름을_고치고_팀을_만들_수_있다()
    {
        var admin = InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜");
        InMemoryDb.SeedMembership(_db, _kids.Id, admin.UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        var c = await LoggedIn("eunhye@example.com");

        var html = await c.GetStringAsync("/jamsil-kids/admin/teams");
        Assert.Contains("아직 팀이 없어요", html);
        Assert.Contains("href=\"/jamsil-kids/admin/teams\"", await c.GetStringAsync("/jamsil-kids/admin"));   // 관리 메뉴 "가족팀"
        Assert.Contains("href=\"/jamsil-kids/admin/teams?New=true\"", html);                                   // R-223 관리자도 만든다

        var teamId = Team(null, "cheolsu", "younghee");
        var page = await c.GetStringAsync("/jamsil-kids/admin/teams");
        Assert.Contains("김철수 가족", page);
        Assert.Contains("900", page);   // 600 + 300

        var saved = await c.PostAsync("/jamsil-kids/admin/teams?handler=Rename", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value, ["id"] = teamId.ToString(), ["name"] = "철수네",
        }));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal("철수네", raw.FamilyTeams.Single().Name);

        // R-223: 관리자가 팀 없는 사람을 방장으로 새 팀을 만든다
        var made = await c.PostAsync("/jamsil-kids/admin/teams?handler=Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value, ["type"] = TeamTypes.Family, ["creator"] = P("haneul"), ["relation"] = FamilyRelation.Student,
        }));
        Assert.Equal(HttpStatusCode.Redirect, made.StatusCode);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var t = raw.FamilyTeams.Single(x => x.CreatorUserName == P("haneul"));
            Assert.Equal("문하늘네 가족팀", t.Name);
            Assert.Single(raw.FamilyNotices, n => n.UserName == P("haneul"));   // R-224 들어감 알림
        }

        // 참여자는 관리 화면을 못 보고, 홈 · 순위 가족팀 탭에서 본다
        var kid = await LoggedIn("cheolsu@example.com");
        Assert.StartsWith("/account/denied", (await kid.GetAsync("/jamsil-kids/admin/teams")).Headers.Location!.PathAndQuery);
        var home = await kid.GetStringAsync("/jamsil-kids");
        Assert.Contains("우리 가족팀", home);
        Assert.Contains("철수네", home);
        Assert.Contains("href=\"/jamsil-kids/family-team\"", home);
        var rank = await kid.GetStringAsync("/jamsil-kids/ranking?t=team");
        Assert.Contains("우리 가족", rank);
        Assert.Contains("철수네", rank);
    }

    [Fact]
    public async Task R0012_기본_규칙에서_가족팀을_끄면_막힌다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.PlatformRuleSetVersions.Add(new PlatformRuleSetVersion { VersionNo = 2, EffectiveFrom = Jan5,
                Values = RuleService.ToJson(RuleSet.Default with { Features = RuleSet.Default.Features with { FamilyTeam = false } }), ChangedByUserName = "t" });
            raw.SaveChanges();
        }
        var svc = In(_kids);
        Assert.False((await svc.GetAsync()).Enabled);
        Assert.Equal(FamilyError.Disabled, await svc.RenameAsync(1, "x"));

        // 화면도 막고(404) 메뉴에서도 숨긴다
        var admin = InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜");
        InMemoryDb.SeedMembership(_db, _kids.Id, admin.UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        var c = await LoggedIn("eunhye@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/jamsil-kids/admin/teams")).StatusCode);
        Assert.DoesNotContain("/admin/teams", await c.GetStringAsync("/jamsil-kids/admin"));
    }
}
