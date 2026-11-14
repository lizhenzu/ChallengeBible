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

/// <summary>가족팀 시상 기록(R-220, ADR 0092). 오늘 = 2027-01-05(서울). 기본 단계: 1,000 · 3,000 · 5,000 = 축하 + 보상, 2,000 · 4,000 = 박수.</summary>
public partial class FamilyAwardTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids;
    private readonly Dictionary<string, string> _p = [];

    public FamilyAwardTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        foreach (var (login, name) in new[] { ("cheolsu", "김철수"), ("younghee", "김영희"), ("haneul", "문하늘"), ("minji", "박민지") })
        {
            var a = InMemoryDb.SeedAccount(_db, login + "@example.com", name).UserName!;
            _p[login] = InMemoryDb.SeedMembership(_db, _kids.Id, a, MembershipStatus.Approved, Roles.Participant).UserName;
        }
    }

    private void Pts(string login, int day, int points)
    {
        using var raw = InMemoryDb.Raw(_db);
        raw.MissionActivities.Add(new MissionActivity { TenantId = _kids.Id, UserName = _p[login], MissionDate = new DateOnly(2027, 1, day), MissionItem = "Word.Me", Points = points, PerformedAt = DateTime.UtcNow });
        raw.SaveChanges();
    }

    private int Team(string name, params string[] logins)
    {
        using var raw = InMemoryDb.Raw(_db);
        var t = new FamilyTeam { TenantId = _kids.Id, Name = name, AwardKey = name, InsertDT = DateTime.UtcNow, UpdateDT = DateTime.UtcNow };
        var order = 0;
        foreach (var l in logins) t.Members.Add(new FamilyMember { TenantId = _kids.Id, UserName = _p[l], SortOrder = order++, Relation = l == "younghee" ? FamilyRelation.Mother : FamilyRelation.Student });
        raw.FamilyTeams.Add(t);
        raw.SaveChanges();
        return t.Id;
    }

    private FamilyAwardService In()
    {
        var scope = new RequestScope();
        scope.SetTenant(_kids.Id);
        var tc = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db)) tc.Set(raw.Tenants.Single(x => x.Id == _kids.Id), null);
        var db = new InMemoryDb(_db, scope);
        var clock = new TenantClock(_time);
        var rules = InMemoryDb.Rules(_db);
        return new FamilyAwardService(db, tc, clock, rules, new FamilyTeamService(db, scope, tc, clock, rules));
    }

    private FamilyStageService Stages()
    {
        var scope = new RequestScope();
        scope.SetTenant(_kids.Id);
        var tc = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db)) tc.Set(raw.Tenants.Single(x => x.Id == _kids.Id), null);
        return new FamilyStageService(new InMemoryDb(_db, scope), tc, new TenantClock(_time), InMemoryDb.Rules(_db));
    }

    [Fact]
    public async Task R220_축하_보상_단계를_넘고_기록이_없으면_줄_차례이고_넘은_날_순이다()
    {
        var a = Team("철수네", "cheolsu", "younghee");
        var b = Team("하늘이네", "haneul");
        Team("민지네", "minji");
        Pts("cheolsu", 2, 600); Pts("younghee", 4, 500);   // 1,100 — 1월 4일에 1,000 을 넘음
        Pts("haneul", 3, 2100);                            // 2,100 — 1월 3일에 1,000 · 2,000 을 넘음(모두 선물 단계, 0093)
        Pts("minji", 3, 300);                              // 아직

        var board = await In().GetAsync();
        Assert.True(board.Enabled);
        Assert.Equal([(b, 1000, new DateOnly(2027, 1, 3)), (b, 2000, new DateOnly(2027, 1, 3)), (a, 1000, new DateOnly(2027, 1, 4))], board.Due.Select(d => (d.TeamId, d.Stage, d.Crossed!.Value)));
        var first = board.Due[2];
        Assert.Equal(("철수네", 1100, "🎁", 10000, false), (first.Team, first.Points, first.Emoji, first.Amount, first.Friend));   // 1,000점 처음 금액 1만(R-290)
        Assert.Equal(2, first.Members.Count);
        Assert.Empty(board.Given);
    }

    [Fact]
    public async Task R220_선물_줬어요는_기록과_감사_로그를_남기고_줄_차례에서_빠진다()
    {
        var a = Team("철수네", "cheolsu", "younghee");
        Pts("cheolsu", 2, 1200);
        using (var raw = InMemoryDb.Raw(_db)) { raw.FamilyTeams.Single(t => t.Id == a).Name = "철수 가족"; raw.SaveChanges(); }   // 이름을 바꿔도 키는 처음 이름

        Assert.Equal(AwardError.None, await In().GiveAsync(a, 1000, "eunhye"));
        using (var raw = InMemoryDb.Raw(_db))
        {
            var w = Assert.Single(raw.TeamAwards);
            Assert.Equal((TeamTypes.Family, "철수네", 1000, _kids.Id), (w.Kind, w.AwardKey, w.Threshold, w.TenantId));
            var log = Assert.Single(raw.AuditLogs, l => l.Action == FamilyAwardService.GiveAction);
            Assert.Equal(("eunhye", _kids.Id), (log.ActorUserName, log.TenantId));
        }
        var board = await In().GetAsync();
        Assert.Empty(board.Due);
        var g = Assert.Single(board.Given);
        Assert.Equal(("철수 가족", 1000, Jan5), (g.Team, g.Stage, g.Day));   // 지금 이름으로 보여 준다

        Assert.Equal(AwardError.Already, await In().GiveAsync(a, 1000, "eunhye"));
    }

    [Fact]
    public async Task R220_끈_단계_안_넘은_단계_없는_팀은_기록할_수_없다()
    {
        var a = Team("철수네", "cheolsu");
        Pts("cheolsu", 2, 2500);
        await Stages().SaveAsync(2027, Enumerable.Range(1, 10).Select(k => k != 2).ToList(), Enumerable.Repeat<string?>("5,000", 10).ToList());
        Assert.Equal(AwardError.NotAward, await In().GiveAsync(a, 2000, "eunhye"));     // 부서가 끈 단계
        Assert.Equal(AwardError.NotReached, await In().GiveAsync(a, 3000, "eunhye"));   // 아직
        Assert.Equal(AwardError.NotAward, await In().GiveAsync(a, 1234, "eunhye"));     // 없는 단계
        Assert.Equal(AwardError.NotFound, await In().GiveAsync(99999, 1000, "eunhye"));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.TeamAwards);
    }

    [Fact]
    public async Task R220_기록_취소는_줄을_지우고_다시_줄_차례가_된다()
    {
        var a = Team("철수네", "cheolsu");
        Pts("cheolsu", 2, 1200);
        await In().GiveAsync(a, 1000, "eunhye");
        var id = (await In().GetAsync()).Given.Single().AwardId;

        Assert.Equal(AwardError.None, await In().UndoAsync(id, "eunhye"));
        using (var raw = InMemoryDb.Raw(_db))
        {
            Assert.Empty(raw.TeamAwards);
            Assert.Single(raw.AuditLogs, l => l.Action == FamilyAwardService.UndoAction);
        }
        Assert.Equal([a], (await In().GetAsync()).Due.Select(d => d.TeamId));
        Assert.Equal(AwardError.NotFound, await In().UndoAsync(id, "eunhye"));
    }

    [Fact]
    public async Task R220_같은_팀_같은_점수는_해가_바뀌어도_한_번만이고_올해_기록만_보인다()
    {
        var a = Team("철수네", "cheolsu");
        Pts("cheolsu", 2, 1200);
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.TeamAwards.Add(new TeamAward { TenantId = _kids.Id, Kind = TeamTypes.Family, AwardKey = "철수네", Threshold = 1000, AwardedAt = new DateTime(2026, 5, 1) });
            raw.SaveChanges();
        }
        var board = await In().GetAsync();
        Assert.Empty(board.Due);
        Assert.Empty(board.Given);
        Assert.Equal(AwardError.Already, await In().GiveAsync(a, 1000, "eunhye"));
    }

    [Fact]
    public async Task R220_부서_시상_기준의_켜진_단계와_금액을_따른다()
    {
        var a = Team("철수네", "cheolsu");
        Pts("cheolsu", 2, 1200);
        Assert.Single((await In().GetAsync()).Due);                                       // 처음 값: 1,000 켬
        await Stages().SaveAsync(2027, Enumerable.Range(1, 10).Select(k => k != 1).ToList(), Enumerable.Repeat<string?>("7,000", 10).ToList());
        Assert.Empty((await In().GetAsync()).Due);                                        // 1,000 을 끄면 줄 차례에서 빠진다
        await Stages().SaveAsync(2027, Enumerable.Repeat(true, 10).ToList(), Enumerable.Repeat<string?>("7,000", 10).ToList());
        var d = Assert.Single((await In().GetAsync()).Due);
        Assert.Equal((a, 1000, 7000), (d.TeamId, d.Stage, d.Amount));
        Assert.Equal(AwardError.None, await In().GiveAsync(a, 1000, "eunhye"));
        Assert.Equal(1, (await Stages().GetAsync(2027)).Rows[0].Given);                   // 시상 기준에 받은 팀 수가 보인다
    }

    [Fact]
    public async Task R220_친구팀도_같은_기준이고_기록은_친구로_남는다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            var t = new FamilyTeam { TenantId = _kids.Id, Name = "말씀 단짝", AwardKey = "말씀 단짝", TeamType = TeamTypes.Friend, CreatorUserName = _p["cheolsu"], InsertDT = DateTime.UtcNow, UpdateDT = DateTime.UtcNow };
            t.Members.Add(new FamilyMember { TenantId = _kids.Id, UserName = _p["cheolsu"], Relation = FamilyRelation.Student });
            t.Members.Add(new FamilyMember { TenantId = _kids.Id, UserName = _p["haneul"], Relation = FamilyRelation.Student, SortOrder = 1 });
            raw.FamilyTeams.Add(t);
            raw.SaveChanges();
        }
        Pts("cheolsu", 2, 600); Pts("haneul", 3, 500);
        var d = Assert.Single((await In().GetAsync()).Due);
        Assert.Equal(("말씀 단짝", 1000, true), (d.Team, d.Stage, d.Friend));
        Assert.Equal(AwardError.None, await In().GiveAsync(d.TeamId, 1000, "eunhye"));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(TeamTypes.Friend, Assert.Single(raw.TeamAwards).Kind);
        Assert.True(Assert.Single((await In().GetAsync()).Given).Friend);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> LoggedIn(string email)
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            var u = raw.Users.Single(x => x.Email == email);
            u.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<AppUser>().HashPassword(u, "1234");
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
    public async Task R220_화면_흐름_관리자가_기록하고_취소하며_참여자는_못_본다()
    {
        var admin = InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜");
        InMemoryDb.SeedMembership(_db, _kids.Id, admin.UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        var a = Team("철수네", "cheolsu", "younghee");
        Pts("cheolsu", 4, 1100);
        var c = await LoggedIn("eunhye@example.com");

        Assert.Contains("href=\"/jamsil-kids/admin/teams/awards\"", await c.GetStringAsync("/jamsil-kids/admin/teams"));
        var page = await c.GetStringAsync("/jamsil-kids/admin/teams/awards");
        Assert.Contains("1월 4일에 넘음", page);
        Assert.DoesNotContain("선물 줬어요", page);   // 선물 처리로 하나로(ADR 0094 F)
        Assert.Contains("href=\"/jamsil-kids/admin/gifts?s=pick\"", page);
        Assert.Contains("aria-current=\"page\">가족팀", page);
        await c.PostAsync("/jamsil-kids/admin/teams/awards?handler=Give", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value, ["team"] = a.ToString(), ["stage"] = "1000",
        }));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Empty(raw.TeamAwards);   // 이 화면에서는 더 기록하지 않는다

        var kid = await LoggedIn("cheolsu@example.com");
        Assert.StartsWith("/account/denied", (await kid.GetAsync("/jamsil-kids/admin/teams/awards")).Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task R220_가족팀을_끄면_시상_기록도_막힌다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.PlatformRuleSetVersions.Add(new PlatformRuleSetVersion { VersionNo = 2, EffectiveFrom = Jan5,
                Values = RuleService.ToJson(RuleSet.Default with { Features = RuleSet.Default.Features with { FamilyTeam = false } }), ChangedByUserName = "t" });
            raw.SaveChanges();
        }
        Assert.False((await In().GetAsync()).Enabled);
        Assert.Equal(AwardError.Disabled, await In().GiveAsync(1, 1000, "eunhye"));
        var admin = InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜");
        InMemoryDb.SeedMembership(_db, _kids.Id, admin.UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        var c = await LoggedIn("eunhye@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/jamsil-kids/admin/teams/awards")).StatusCode);
    }
}
