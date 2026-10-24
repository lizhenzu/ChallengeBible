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

/// <summary>가족팀 관리(확정 시안: 관리-가족팀, R-90 ~ R-97, 0012 · 0046). 오늘 = 2027-01-05(서울).</summary>
public partial class FamilyTeamTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids, _elem;
    private readonly Dictionary<string, (int AccountId, Membership M, int Pid)> _p = [];

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
        Add(_kids, "younghee", "김영희", [Roles.Participant, Roles.Guardian], family: "kids");
        Add(_kids, "daeho", "김대호", [Roles.Participant, Roles.Guardian], family: "kids");
        Add(_kids, "haneul", "문하늘", [Roles.Participant], "믿음반");
        Add(_kids, "seongmin", "오성민", [Roles.Participant, Roles.Guardian], family: "elem");   // 다른 부서를 가족 부서로(0046)
        Add(_kids, "nobody", "정모름", [Roles.Participant, Roles.Guardian]);                    // 아직 안 고름
        Add(_elem, "jiho", "윤지호", [Roles.Participant]);                                     // 다른 부서 참여자
        Pts("cheolsu", 600);
        Pts("younghee", 300);
        Pts("daeho", 150);
        Pts("haneul", 40);
    }

    private void Add(Tenant t, string login, string name, string[] roles, string? group = null, string? family = null)
    {
        var a = InMemoryDb.SeedAccount(_db, login + "@example.com", name, family switch { "kids" => _kids.Id, "elem" => _elem.Id, _ => null }).Id;
        var m = InMemoryDb.SeedMembership(_db, t.Id, a, MembershipStatus.Approved, roles);
        using var raw = InMemoryDb.Raw(_db);
        raw.Memberships.Single(x => x.Id == m.Id).GroupLabel = group;
        var p = new Participant { TenantId = t.Id, MembershipId = m.Id };
        raw.Participants.Add(p);
        raw.SaveChanges();
        _p[login] = (a, m, p.Id);
    }

    private void Pts(string login, int points)
    {
        using var raw = InMemoryDb.Raw(_db);
        var p = raw.Participants.Single(x => x.Id == _p[login].Pid);
        raw.PointLedgers.Add(new PointLedger { TenantId = p.TenantId, ParticipantId = p.Id, MissionDate = new DateOnly(2027, 1, 3), MissionItem = "Word.Me", Points = points, RecordedUtc = DateTime.UtcNow });
        raw.SaveChanges();
    }

    private FamilyTeamService In(Tenant t)
    {
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        scope.SetAccount(1);
        var tc = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db)) tc.Set(raw.Tenants.Single(x => x.Id == t.Id), null);
        return new FamilyTeamService(new InMemoryDb(_db, scope), new InMemorySystemDb(_db), scope, tc, new TenantClock(_time), InMemoryDb.Rules(_db), _time);
    }

    private int P(string login) => _p[login].Pid;

    [Fact]
    public async Task R90_R92_R93_팀을_만들면_합계는_구성원_시즌_점수의_합_이름이_비면_대표_학생_가족()
    {
        var svc = In(_kids);
        var (e, id) = await svc.CreateAsync(" ", [P("younghee"), P("cheolsu"), P("daeho")]);
        Assert.Equal(FamilyError.None, e);
        var team = Assert.Single((await svc.GetAsync()).Teams);
        Assert.Equal(1050, team.Points);
        Assert.Equal("김철수 가족", team.DisplayName);   // 첫 구성원이 엄마여도 대표는 학생(R-93)
        Assert.Equal([FamilyRelation.Mother, FamilyRelation.Student, FamilyRelation.Mother], team.Members.Select(m => m.Relation));   // 보호자는 엄마로 시작

        // 관계 · 이름 저장, 순서 옮기기
        var dad = team.Members.Single(m => m.Name == "김대호");
        Assert.Equal(FamilyError.None, await svc.SaveAsync(id!.Value, "철수네", "메모", new Dictionary<int, string> { [dad.MemberId] = FamilyRelation.Father }));
        Assert.Equal(FamilyError.None, await svc.MoveAsync(id.Value, dad.MemberId, up: true));
        team = (await svc.GetAsync()).Teams.Single();
        Assert.Equal(("철수네", "메모"), (team.DisplayName, team.Memo));
        Assert.Equal(["김영희", "김대호", "김철수"], team.Members.Select(m => m.Name));
        Assert.Equal(FamilyRelation.Father, team.Members[1].Relation);
        Assert.Equal(FamilyError.BadRelation, await svc.SaveAsync(id.Value, null, null, new Dictionary<int, string> { [dad.MemberId] = "Uncle" }));
    }

    [Fact]
    public async Task R91_한_사람은_활성_팀_하나에만_팀을_멈추면_다른_팀에_들어갈_수_있다()
    {
        var svc = In(_kids);
        var (_, a) = await svc.CreateAsync(null, [P("cheolsu")]);
        Assert.Equal(FamilyError.AlreadyInTeam, (await svc.CreateAsync(null, [P("cheolsu"), P("haneul")])).Error);
        Assert.Equal(FamilyError.None, await svc.SetActiveAsync(a!.Value, false));
        var (e, b) = await svc.CreateAsync(null, [P("cheolsu")]);
        Assert.Equal(FamilyError.None, e);
        Assert.Equal(FamilyError.MembersElsewhere, await svc.SetActiveAsync(a.Value, true));   // 철수가 다른 팀에 있어 다시 켤 수 없다
        var board = await svc.GetAsync();
        Assert.Equal(1, board.ActiveTeams);
        Assert.DoesNotContain(board.Solo, s => s.Name == "김철수");

        // 빼면 팀 없는 참여자로, 점수는 그대로
        var member = board.Teams.Single(t => t.Id == b).Members.Single();
        Assert.Equal(FamilyError.None, await svc.RemoveMemberAsync(b!.Value, member.MemberId));
        Assert.Contains((await svc.GetAsync()).Solo, s => s.Name == "김철수" && s.Points == 600);
    }

    [Fact]
    public async Task R90_0046_같은_부서_참여자만_부모님은_가족_부서가_여기일_때만()
    {
        var svc = In(_kids);
        Assert.Equal(FamilyError.NotParticipant, (await svc.CreateAsync(null, [_p["jiho"].Pid])).Error);   // 다른 부서 참여자
        Assert.Equal(FamilyError.FamilyTenantElsewhere, (await svc.CreateAsync(null, [P("seongmin")])).Error);
        Assert.Equal(FamilyError.FamilyTenantNotChosen, (await svc.CreateAsync(null, [P("nobody")])).Error);
        Assert.Equal(FamilyError.NoMember, (await svc.CreateAsync(null, [])).Error);
        var solo = (await svc.GetAsync()).Solo;
        Assert.Equal(FamilyError.FamilyTenantElsewhere, solo.Single(s => s.Name == "오성민").Blocked);
        Assert.Equal(FamilyError.None, solo.Single(s => s.Name == "김철수").Blocked);
        Assert.DoesNotContain(solo, s => s.Name == "윤지호");

        var (_, id) = await svc.CreateAsync(null, [P("cheolsu")]);
        Assert.Equal(FamilyError.FamilyTenantElsewhere, await svc.AddMemberAsync(id!.Value, P("seongmin")));
        Assert.Equal(FamilyError.None, await svc.AddMemberAsync(id.Value, P("younghee")));
    }

    [Fact]
    public async Task R97_넘은_단계만_축하_카드를_숨기고_다시_보인다()
    {
        var svc = In(_kids);
        var (_, id) = await svc.CreateAsync(null, [P("cheolsu"), P("younghee"), P("daeho")]);   // 1,050점 → 1,000 단계만
        var team = (await svc.GetAsync()).Teams.Single();
        Assert.Equal([1000], team.Reached.Select(s => s.Points));
        Assert.Equal(FamilyError.NotReached, await svc.SetStageHiddenAsync(1, id!.Value, 2000, true));
        Assert.Equal(FamilyError.None, await svc.SetStageHiddenAsync(1, id.Value, 1000, true));
        Assert.True((await svc.GetAsync()).Teams.Single().Reached.Single().Hidden);
        Assert.Equal(FamilyError.None, await svc.SetStageHiddenAsync(1, id.Value, 1000, false));
        Assert.False((await svc.GetAsync()).Teams.Single().Reached.Single().Hidden);
    }

    [Fact]
    public async Task R131_다른_부서의_팀은_보이지도_고쳐지지도_않는다()
    {
        var (_, id) = await In(_kids).CreateAsync(null, [P("cheolsu")]);
        var other = In(_elem);
        Assert.Empty((await other.GetAsync()).Teams);
        Assert.Equal(FamilyError.NotFound, await other.SaveAsync(id!.Value, "가로채기", null, new Dictionary<int, string>()));
        Assert.Equal(FamilyError.NotFound, await other.SetActiveAsync(id.Value, false));
        Assert.Equal(FamilyError.NotFound, await other.AddMemberAsync(id.Value, _p["jiho"].Pid));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Null(raw.FamilyTeams.Single().Name);
    }

    [Fact]
    public async Task R0012_기본_규칙에서_가족팀을_끄면_막힌다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.PlatformRuleSetVersions.Add(new PlatformRuleSetVersion { VersionNo = 2, EffectiveFrom = Jan5,
                Values = RuleService.ToJson(RuleSet.Default with { Features = RuleSet.Default.Features with { FamilyTeam = false } }), ChangedByAccountId = 1 });
            raw.SaveChanges();
        }
        var svc = In(_kids);
        Assert.False((await svc.GetAsync()).Enabled);
        Assert.Equal(FamilyError.Disabled, (await svc.CreateAsync(null, [P("cheolsu")])).Error);
    }
}
