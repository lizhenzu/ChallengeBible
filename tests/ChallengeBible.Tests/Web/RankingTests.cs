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

/// <summary>참여자 순위(R-80 ~ R-84, 확정 시안: 말씀-순위). 오늘 = 2027-01-05 화요일(서울). 목표 = 8,000점(R-82).</summary>
public partial class RankingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids, _elem;
    private readonly Dictionary<string, (Membership M, int Pid)> _p = [];

    public RankingTests(WebApplicationFactory<Program> app)
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
        Add(_kids, "harin", "강하린", [Roles.Participant]);
        Add(_kids, "younghee", "김영희", [Roles.Participant, Roles.Guardian]);
        Add(_kids, "eunhye", "이은혜", [Roles.Participant, Roles.Teacher]);
        Add(_kids, "zero", "오영점", [Roles.Participant]);                // 0점 → 목록에 없음(R-80)
        Add(_elem, "jiho", "윤지호", [Roles.Participant]);                // 다른 부서
        Pts("cheolsu", (1, 3), (3, 3), (4, 3), (5, 3));                   // 12점 · 연속 3일(3 ~ 5일)
        Pts("harin", (2, 12));                                             // 12점 · 이름순으로 강하린이 앞
        Pts("younghee", (5, 9000));                                        // 목표 넘음 → 100%
        Pts("eunhye", (4, 2));
        Pts("jiho", (5, 50));
        Pts("zero", (1, 0));
    }

    private void Add(Tenant t, string login, string name, string[] roles, string? group = null)
    {
        var a = InMemoryDb.SeedAccount(_db, login + "@example.com", name).Id;
        var m = InMemoryDb.SeedMembership(_db, t.Id, a, MembershipStatus.Approved, roles);
        using var raw = InMemoryDb.Raw(_db);
        raw.Memberships.Single(x => x.Id == m.Id).GroupLabel = group;
        var p = new Participant { TenantId = t.Id, MembershipId = m.Id };
        raw.Participants.Add(p);
        raw.SaveChanges();
        _p[login] = (m, p.Id);
    }

    private void Pts(string login, params (int Day, int Points)[] days)
    {
        using var raw = InMemoryDb.Raw(_db);
        var p = raw.Participants.Single(x => x.Id == _p[login].Pid);
        foreach (var (day, pts) in days)
            raw.PointLedgers.Add(new PointLedger { TenantId = p.TenantId, ParticipantId = p.Id, MissionDate = new DateOnly(2027, 1, day), MissionItem = "Word.Me", Points = pts, RecordedUtc = DateTime.UtcNow });
        raw.SaveChanges();
    }

    private RankingService As(string login)
    {
        var scope = new RequestScope();
        scope.SetTenant(_kids.Id);
        var tc = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db))
        {
            var mid = _p[login].M.Id;
            tc.Set(raw.Tenants.Single(t => t.Id == _kids.Id), new Membership { Id = mid, Status = MembershipStatus.Approved, Roles = [new MembershipRole { Role = Roles.Participant }] });
        }
        return new RankingService(new InMemoryDb(_db, scope), tc, new TenantClock(_time), InMemoryDb.Rules(_db));
    }

    [Fact]
    public async Task R80_R82_점수가_있는_이_부서_참여자만_점수순_같으면_이름순()
    {
        var r = await As("cheolsu").GetAsync();
        Assert.Equal(8000, r.Target);
        Assert.Equal(["김영희", "강하린", "김철수", "이은혜"], r.Rows.Select(x => x.Name).ToArray());
        Assert.Equal([1, 2, 3, 4], r.Rows.Select(x => x.Rank).ToArray());
        Assert.Equal(1, r.Achieved);
    }

    [Fact]
    public async Task R81_R83_R84_달성률은_100에서_자르고_연속_참여일과_내_카드()
    {
        var rows = (await As("cheolsu").GetAsync()).Rows;
        Assert.Equal(100, rows.Single(x => x.Name == "김영희").Percent);
        var me = rows.Single(x => x.Name == "김철수");
        Assert.Equal((12, 0.15, Jan5, 3, true, "사랑반"), (me.Points, me.Percent, me.LastDate!.Value, me.Streak, me.Me, me.GroupLabel));
        Assert.False(rows.Single(x => x.Name == "강하린").Me);
        Assert.Equal(1, rows.Single(x => x.Name == "강하린").Streak);
    }

    [Fact]
    public async Task 구분은_학생_부모님_선생님_거르기와_정렬()
    {
        var rows = (await As("cheolsu").GetAsync()).Rows;
        Assert.Equal((RankKind.Parent, RankKind.Teacher), (rows.Single(x => x.Name == "김영희").Kind, rows.Single(x => x.Name == "이은혜").Kind));
        Assert.Equal(["강하린", "김철수"], RankingService.View(rows, "student", null, "pts").Select(x => x.Name).ToArray());
        Assert.Equal(["김영희", "김철수", "이은혜", "강하린"], RankingService.View(rows, "all", null, "recent").Select(x => x.Name).ToArray());   // 같은 날이면 점수순
        Assert.Equal(["강하린", "김영희", "김철수", "이은혜"], RankingService.View(rows, null, null, "name").Select(x => x.Name).ToArray());
        Assert.Equal(["김철수"], RankingService.View(rows, null, "철수", null).Select(x => x.Name).ToArray());
    }

    [Fact]
    public async Task 시즌_시작일_전_점수는_세지_않는다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.TenantSeasons.Add(new TenantSeason { TenantId = _kids.Id, Year = 2027, StartDate = new DateOnly(2027, 1, 3), ChangedByAccountId = 1 });
            raw.SaveChanges();
        }
        var rows = (await As("cheolsu").GetAsync()).Rows;
        Assert.Equal(9, rows.Single(x => x.Name == "김철수").Points);              // 1일 3점 빠짐
        Assert.DoesNotContain(rows, x => x.Name == "강하린");                      // 2일 점수뿐 → 0점
    }

    [Fact]
    public async Task 기본_규칙에서_순위를_끄면_보이지_않는다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.PlatformRuleSetVersions.Add(new PlatformRuleSetVersion { VersionNo = 2, EffectiveFrom = Jan5,
                Values = RuleService.ToJson(RuleSet.Default with { Features = RuleSet.Default.Features with { Ranking = false } }), ChangedByAccountId = 1 });
            raw.SaveChanges();
        }
        Assert.False((await As("cheolsu").GetAsync()).Enabled);
    }

    // ---- 화면 흐름 ----

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    [Fact]
    public async Task 미션_홈_아래_메뉴에서_순위로_가고_내_카드에_나()
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.PostAsync("/account/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(await c.GetStringAsync("/account/signup")).Groups[1].Value,
            ["DisplayName"] = "박서연", ["Email"] = "seoyeon@example.com", ["Password"] = "1234", ["PasswordConfirm"] = "1234",
        }));
        int id;
        using (var raw = InMemoryDb.Raw(_db)) id = raw.Accounts.Single(a => a.Email == "seoyeon@example.com").Id;
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, id, MembershipStatus.Approved, Roles.Participant);
        using (var raw = InMemoryDb.Raw(_db))
        {
            var p = new Participant { TenantId = _kids.Id, MembershipId = m.Id };
            raw.Participants.Add(p);
            raw.SaveChanges();
            raw.PointLedgers.Add(new PointLedger { TenantId = _kids.Id, ParticipantId = p.Id, MissionDate = Jan5, MissionItem = "Word.Me", Points = 1, RecordedUtc = DateTime.UtcNow });
            raw.SaveChanges();
        }
        ContentSeed.Word(_db, Jan5);
        Assert.Contains("href=\"/jamsil-kids/ranking\"", await c.GetStringAsync("/jamsil-kids/word-mission"));
        var html = await c.GetStringAsync("/jamsil-kids/ranking");
        Assert.Contains("목표 8,000점", html);
        Assert.Contains("TOP 3", html);
        Assert.Matches(@"박서연\s*<em>나</em>", html);                                       // R-84 내 카드
        Assert.DoesNotContain("윤지호", html);
    }
}
