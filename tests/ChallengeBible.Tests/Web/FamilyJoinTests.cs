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

/// <summary>가족이 스스로 꾸리는 가족팀(ADR 0083, R-204 ~ R-210). 오늘 = 2027-01-05(서울).</summary>
public partial class FamilyJoinTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids, _elem;
    private readonly Dictionary<string, (int AccountId, Membership M, int Pid)> _p = [];

    public FamilyJoinTests(WebApplicationFactory<Program> app)
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
        Add(_kids, "cheolsu", "김철수", Roles.Participant);
        Add(_kids, "dad", "김민호", Roles.Participant, Roles.Guardian);   // 시상 부서를 정하지 않아도 참여자면 된다
        Add(_kids, "mom", "이수진", Roles.Participant, Roles.Guardian);
        Add(_kids, "other", "문하늘", Roles.Participant);
        Add(_elem, "jiho", "윤지호", Roles.Participant);
        Pts("dad", 500);
        Pts("cheolsu", 300);
    }

    private void Add(Tenant t, string login, string name, params string[] roles)
    {
        var a = InMemoryDb.SeedAccount(_db, login + "@example.com", name).Id;
        AddMembership(t, login, a, roles);
    }

    private void AddMembership(Tenant t, string key, int account, string[] roles)
    {
        var m = InMemoryDb.SeedMembership(_db, t.Id, account, MembershipStatus.Approved, roles);
        using var raw = InMemoryDb.Raw(_db);
        var p = new Participant { TenantId = t.Id, MembershipId = m.Id };
        raw.Participants.Add(p);
        raw.SaveChanges();
        _p[key] = (account, m, p.Id);
    }

    private void Pts(string login, int points)
    {
        using var raw = InMemoryDb.Raw(_db);
        raw.PointLedgers.Add(new PointLedger { TenantId = _kids.Id, ParticipantId = _p[login].Pid, MissionDate = new DateOnly(2027, 1, 3), MissionItem = "Word.Me", Points = points, RecordedUtc = DateTime.UtcNow });
        raw.SaveChanges();
    }

    private FamilyJoinService As(string login, Tenant? t = null)
    {
        t ??= _kids;
        var (account, membership, _) = _p[login];
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        scope.SetAccount(account);
        var tc = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db)) tc.Set(raw.Tenants.Single(x => x.Id == t.Id), new Membership { Id = membership.Id, Status = MembershipStatus.Approved, Roles = [new MembershipRole { Role = Roles.Participant }] });
        var teams = new FamilyTeamService(new InMemoryDb(_db, scope), tc, new TenantClock(_time), InMemoryDb.Rules(_db), _time);
        return new FamilyJoinService(new InMemoryDb(_db, scope), new InMemorySystemDb(_db), scope, tc, teams, _time);
    }

    private async Task<int> TeamOf(string login) => (await As(login).StateAsync()).Mine!.TeamId;

    /// <summary>아빠가 만들고 철수가 신청해 수락된 팀.</summary>
    private async Task<int> DadAndKid()
    {
        Assert.Equal(FamilyError.None, await As("dad").CreateAsync("웃음폭탄네", FamilyRelation.Father));
        var id = await TeamOf("dad");
        Assert.Equal(FamilyError.None, await As("cheolsu").RequestAsync(id, FamilyRelation.Student));
        var r = (await As("dad").StateAsync()).Mine!.Requests.Single();
        Assert.Equal(FamilyError.None, await As("dad").AcceptAsync(r.RequestId));
        return id;
    }

    [Fact]
    public async Task R204_참여자는_승인_없이_만들고_바로_첫_구성원이_된다()
    {
        Assert.Equal(FamilyError.None, await As("dad").CreateAsync("  웃음폭탄네 ", FamilyRelation.Father));
        var mine = (await As("dad").StateAsync()).Mine!;
        Assert.Equal("웃음폭탄네", mine.Name);
        Assert.Equal(("김민호", FamilyRelation.Father, 500, true), (mine.Members.Single().Name, mine.Members.Single().Relation, mine.Members.Single().Points, mine.Members.Single().Me));
        Assert.Equal(500, mine.Total);
        Assert.Equal(1000, mine.Next);
        Assert.Equal(50, mine.Pct);

        Assert.Equal(FamilyError.AlreadyInTeam, await As("dad").CreateAsync("또", FamilyRelation.Father));
        Assert.Equal(FamilyError.BadRelation, await As("mom").CreateAsync("x", "Uncle"));
    }

    [Fact]
    public async Task R204_R93_이름이_비면_대표_학생_가족_같은_이름도_된다()
    {
        Assert.Equal(FamilyError.None, await As("cheolsu").CreateAsync("  ", FamilyRelation.Student));
        Assert.Equal("김철수 가족", (await As("cheolsu").StateAsync()).Mine!.Name);
        Assert.Equal(FamilyError.None, await As("dad").CreateAsync("같은이름", FamilyRelation.Father));
        Assert.Equal(FamilyError.None, await As("mom").CreateAsync("같은이름", FamilyRelation.Mother));
    }

    [Fact]
    public async Task R205_사람_이름과_팀_이름으로_찾고_점수는_보이지_않는다()
    {
        await DadAndKid();
        var hit = Assert.Single(await As("other").SearchAsync("철수"));
        Assert.Equal("웃음폭탄네", hit.Name);
        Assert.Equal([("김민호", FamilyRelation.Father), ("김철수", FamilyRelation.Student)], hit.Members.Select(m => (m.Name, m.Relation)));
        Assert.Single(await As("other").SearchAsync("폭탄"));
        Assert.Empty(await As("other").SearchAsync("없는이름"));
        Assert.Empty(await As("other").SearchAsync("  "));
        Assert.Empty(await As("jiho", _elem).SearchAsync("철수"));   // 다른 부서 팀은 찾을 수 없다
    }

    [Fact]
    public async Task R206_R207_신청만으로는_합류되지_않고_팀_구성원이_수락하면_신청한_역할로_들어온다()
    {
        Assert.Equal(FamilyError.None, await As("dad").CreateAsync("웃음폭탄네", FamilyRelation.Father));
        var id = await TeamOf("dad");
        Assert.Equal(FamilyError.None, await As("cheolsu").RequestAsync(id, FamilyRelation.Student));

        var kid = await As("cheolsu").StateAsync();
        Assert.Null(kid.Mine);
        Assert.Equal(("웃음폭탄네", FamilyRelation.Student), (kid.Pending!.TeamName, kid.Pending.Relation));
        Assert.Equal(500, (await As("dad").StateAsync()).Mine!.Total);   // 아직 합계에 들지 않는다

        // 한 번에 한 팀, 신청 중에는 만들 수 없다
        Assert.Equal(FamilyError.HasPending, await As("cheolsu").RequestAsync(id, FamilyRelation.Student));
        Assert.Equal(FamilyError.HasPending, await As("cheolsu").CreateAsync("내팀", FamilyRelation.Student));

        // 구성원이 아니면 수락 못 한다(본인 포함)
        var req = (await As("dad").StateAsync()).Mine!.Requests.Single();
        Assert.Equal(("김철수", FamilyRelation.Student), (req.Name, req.Relation));
        Assert.Equal(FamilyError.NotMember, await As("cheolsu").AcceptAsync(req.RequestId));
        Assert.Equal(FamilyError.NotMember, await As("other").AcceptAsync(req.RequestId));

        Assert.Equal(FamilyError.None, await As("dad").AcceptAsync(req.RequestId));
        var mine = (await As("cheolsu").StateAsync()).Mine!;
        Assert.Equal(800, mine.Total);
        Assert.Equal(FamilyRelation.Student, mine.Members.Single(m => m.Me).Relation);
        Assert.Empty(mine.Requests);
        Assert.Null((await As("cheolsu").StateAsync()).Pending);

        // 나중에 든 구성원도 수락할 수 있다
        Assert.Equal(FamilyError.None, await As("mom").RequestAsync(id, FamilyRelation.Mother));
        var momReq = (await As("cheolsu").StateAsync()).Mine!.Requests.Single();
        Assert.Equal(FamilyError.None, await As("cheolsu").AcceptAsync(momReq.RequestId));
        Assert.Equal(3, (await As("dad").StateAsync()).Mine!.Members.Count);
    }

    [Fact]
    public async Task R206_R207_취소와_거절을_하면_신청이_없어지고_다른_팀에_신청할_수_있다()
    {
        Assert.Equal(FamilyError.None, await As("dad").CreateAsync("A", FamilyRelation.Father));
        Assert.Equal(FamilyError.None, await As("mom").CreateAsync("B", FamilyRelation.Mother));
        var a = await TeamOf("dad");
        var b = await TeamOf("mom");

        Assert.Equal(FamilyError.None, await As("cheolsu").RequestAsync(a, FamilyRelation.Student));
        Assert.Equal(FamilyError.None, await As("cheolsu").CancelAsync());
        Assert.Equal(FamilyError.NotFound, await As("cheolsu").CancelAsync());
        Assert.Empty((await As("dad").StateAsync()).Mine!.Requests);

        Assert.Equal(FamilyError.None, await As("cheolsu").RequestAsync(b, FamilyRelation.Student));
        var r = (await As("mom").StateAsync()).Mine!.Requests.Single();
        Assert.Equal(FamilyError.NotMember, await As("dad").RejectAsync(r.RequestId));   // 다른 팀 구성원은 못 한다
        Assert.Equal(FamilyError.None, await As("mom").RejectAsync(r.RequestId));
        Assert.Null((await As("cheolsu").StateAsync()).Pending);
        Assert.Equal(FamilyError.NotFound, await As("cheolsu").RequestAsync(99999, FamilyRelation.Student));   // 없는 팀
        Assert.Equal(FamilyError.None, await As("cheolsu").RequestAsync(a, FamilyRelation.Student));
    }

    [Fact]
    public async Task R208_나가면_점수는_남고_합계에서만_빠지며_다른_팀에_갈_수_있다()
    {
        var id = await DadAndKid();
        Assert.Equal(FamilyError.AlreadyInTeam, await As("cheolsu").CreateAsync("다른팀", FamilyRelation.Student));
        Assert.Equal(FamilyError.None, await As("cheolsu").LeaveAsync());
        Assert.Null((await As("cheolsu").StateAsync()).Mine);
        Assert.Equal(500, (await As("dad").StateAsync()).Mine!.Total);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(300, raw.PointLedgers.Where(l => l.ParticipantId == _p["cheolsu"].Pid).Sum(l => l.Points));

        Assert.Equal(FamilyError.None, await As("cheolsu").RequestAsync(id, FamilyRelation.Student));   // 다시 돌아올 수 있다
        Assert.Equal(FamilyError.NotMember, await As("cheolsu").LeaveAsync());
    }

    [Fact]
    public async Task R208_이미_넘은_축하_기록은_나가도_취소하지_않는다()
    {
        var id = await DadAndKid();
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.FamilyMilestonesReached.Add(new FamilyMilestoneReached { TenantId = _kids.Id, FamilyTeamId = id, Year = 2027, Threshold = 800, ReachedUtc = DateTime.UtcNow });
            raw.SaveChanges();
        }
        Assert.Equal(FamilyError.None, await As("cheolsu").LeaveAsync());
        using (var raw = InMemoryDb.Raw(_db)) Assert.Single(raw.FamilyMilestonesReached);
    }

    [Fact]
    public async Task R208_구성원끼리_내보낼_수_있고_나를_내보낼_수는_없다()
    {
        await DadAndKid();
        var mine = (await As("dad").StateAsync()).Mine!;
        var kid = mine.Members.Single(m => !m.Me);
        var me = mine.Members.Single(m => m.Me);
        Assert.Equal(FamilyError.NotFound, await As("dad").KickAsync(me.MemberId));
        Assert.Equal(FamilyError.NotMember, await As("other").KickAsync(kid.MemberId));
        Assert.Equal(FamilyError.None, await As("dad").KickAsync(kid.MemberId));
        Assert.Null((await As("cheolsu").StateAsync()).Mine);
    }

    [Fact]
    public async Task R208_마지막_사람이_나가면_팀과_신청과_축하_기록이_사라진다()
    {
        Assert.Equal(FamilyError.None, await As("dad").CreateAsync("웃음폭탄네", FamilyRelation.Father));
        var id = await TeamOf("dad");
        Assert.Equal(FamilyError.None, await As("cheolsu").RequestAsync(id, FamilyRelation.Student));
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.FamilyMilestonesReached.Add(new FamilyMilestoneReached { TenantId = _kids.Id, FamilyTeamId = id, Year = 2027, Threshold = 500, ReachedUtc = DateTime.UtcNow });
            raw.SaveChanges();
        }
        Assert.Equal(FamilyError.None, await As("dad").LeaveAsync());
        using (var raw = InMemoryDb.Raw(_db))
        {
            Assert.Empty(raw.FamilyTeams);
            Assert.Empty(raw.FamilyMembers);
            Assert.Empty(raw.FamilyJoinRequests);
            Assert.Empty(raw.FamilyMilestonesReached);
        }
        Assert.Null((await As("cheolsu").StateAsync()).Pending);
        Assert.Equal(FamilyError.None, await As("cheolsu").CreateAsync("웃음폭탄네", FamilyRelation.Student));   // 이름을 다시 쓸 수 있다
    }

    [Fact]
    public async Task R209_구성원은_팀_이름을_바꾸고_내_역할은_내가_바꾼다()
    {
        await DadAndKid();
        Assert.Equal(FamilyError.None, await As("cheolsu").RenameAsync("말씀 수호대"));
        Assert.Equal("말씀 수호대", (await As("dad").StateAsync()).Mine!.Name);
        Assert.Equal(FamilyError.None, await As("cheolsu").RenameAsync(" "));
        Assert.Equal("김철수 가족", (await As("dad").StateAsync()).Mine!.Name);
        Assert.Equal(FamilyError.NotMember, await As("other").RenameAsync("가로채기"));

        Assert.Equal(FamilyError.None, await As("cheolsu").SetRelationAsync(FamilyRelation.Other));
        var mine = (await As("dad").StateAsync()).Mine!;
        Assert.Equal(FamilyRelation.Other, mine.Members.Single(m => m.Name == "김철수").Relation);
        Assert.Equal(FamilyRelation.Father, mine.Members.Single(m => m.Name == "김민호").Relation);   // 남의 역할은 그대로
        Assert.Equal(FamilyError.BadRelation, await As("cheolsu").SetRelationAsync("Uncle"));
        Assert.Equal(FamilyError.None, await As("cheolsu").SetRelationAsync(FamilyRelation.Father));   // 같은 역할이 둘이어도 된다
    }

    [Fact]
    public async Task R210_한_사람은_한_부서의_가족팀에만_다른_부서_팀에_있으면_막힌다()
    {
        var dad = _p["dad"].AccountId;
        AddMembership(_elem, "dad-elem", dad, [Roles.Participant, Roles.Guardian]);
        Assert.Equal(FamilyError.None, await As("dad-elem", _elem).CreateAsync("초등부 팀", FamilyRelation.Father));

        Assert.Equal(FamilyError.InOtherDept, await As("dad").CreateAsync("유년부 팀", FamilyRelation.Father));
        Assert.Equal(FamilyError.None, await As("mom").CreateAsync("B", FamilyRelation.Mother));
        Assert.Equal(FamilyError.InOtherDept, await As("dad").RequestAsync(await TeamOf("mom"), FamilyRelation.Father));

        // 신청 뒤 다른 부서 팀에 들어가면 수락할 때 막히고 신청은 지워진다
        Assert.Equal(FamilyError.None, await As("dad-elem", _elem).LeaveAsync());
        Assert.Equal(FamilyError.None, await As("dad").RequestAsync(await TeamOf("mom"), FamilyRelation.Father));
        Assert.Equal(FamilyError.None, await As("dad-elem", _elem).CreateAsync("다시", FamilyRelation.Father));
        var r = (await As("mom").StateAsync()).Mine!.Requests.Single();
        Assert.Equal(FamilyError.InOtherDept, await As("mom").AcceptAsync(r.RequestId));
        Assert.Empty((await As("mom").StateAsync()).Mine!.Requests);
    }

    [Fact]
    public async Task R131_다른_부서_사람은_이_부서_팀에_신청하거나_바꿀_수_없다()
    {
        var id = await DadAndKid();
        Assert.Equal(FamilyError.NotFound, await As("jiho", _elem).RequestAsync(id, FamilyRelation.Student));
        var kid = (await As("dad").StateAsync()).Mine!.Members.Single(m => !m.Me);
        Assert.Equal(FamilyError.NotMember, await As("jiho", _elem).KickAsync(kid.MemberId));
    }

    [Fact]
    public async Task R172_임시_부서에서는_쓸_수_없다()
    {
        var lobby = InMemoryDb.SeedTenant(_db, "lobby-kids", "LOBBYK");
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.Tenants.Single(t => t.Id == lobby.Id).IsLobby = true;
            raw.SaveChanges();
        }
        using (var raw = InMemoryDb.Raw(_db)) lobby = raw.Tenants.Single(t => t.Id == lobby.Id);
        AddMembership(lobby, "lobby-kid", InMemoryDb.SeedAccount(_db, "lk@example.com", "임시").Id, [Roles.Participant]);
        Assert.False((await As("lobby-kid", lobby).StateAsync()).Enabled);
        Assert.Null(await As("lobby-kid", lobby).EntryAsync());
        Assert.Equal(FamilyError.Disabled, await As("lobby-kid", lobby).CreateAsync("x", FamilyRelation.Student));
        Assert.Empty(await As("lobby-kid", lobby).SearchAsync("김"));
    }

    [Fact]
    public async Task 입구는_팀_유무와_기다리는_신청_수를_보여_주고_참여자가_아니면_없다()
    {
        var empty = await As("other").EntryAsync();
        Assert.NotNull(empty);
        Assert.Null(empty.Name);

        Assert.Equal(FamilyError.None, await As("dad").CreateAsync("웃음폭탄네", FamilyRelation.Father));
        Assert.Equal(FamilyError.None, await As("other").RequestAsync(await TeamOf("dad"), FamilyRelation.Student));
        Assert.Equal("웃음폭탄네", (await As("other").EntryAsync())!.PendingTeam);
        var entry = (await As("dad").EntryAsync())!;
        Assert.Equal(("웃음폭탄네", 500, 1), (entry.Name, entry.Total, entry.Waiting));
    }

    [Fact]
    public async Task R0012_기본_규칙에서_가족팀을_끄면_화면도_입구도_막힌다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.PlatformRuleSetVersions.Add(new PlatformRuleSetVersion { VersionNo = 2, EffectiveFrom = new DateOnly(2027, 1, 5),
                Values = RuleService.ToJson(RuleSet.Default with { Features = RuleSet.Default.Features with { FamilyTeam = false } }), ChangedByAccountId = 1 });
            raw.SaveChanges();
        }
        Assert.Null(await As("cheolsu").EntryAsync());
        Assert.Equal(FamilyError.Disabled, await As("cheolsu").CreateAsync("x", FamilyRelation.Student));
        var c = await LoggedIn("cheolsu@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/jamsil-kids/family-team")).StatusCode);
        Assert.DoesNotContain("/family-team", await c.GetStringAsync("/jamsil-kids/word-mission"));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRx();

    private async Task<HttpClient> LoggedIn(string email)
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            var a = raw.Accounts.Single(x => x.Email == email);
            a.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<Account>().HashPassword(a, "1234");
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

    private static Task<HttpResponseMessage> Post(HttpClient c, string page, string handler, params (string K, string V)[] form)
    {
        var fields = form.Select(f => new KeyValuePair<string, string>(f.K, f.V)).Prepend(new("__RequestVerificationToken", TokenRx().Match(page).Groups[1].Value));
        return c.PostAsync($"/jamsil-kids/family-team?handler={handler}", new FormUrlEncodedContent(fields));
    }

    [Fact]
    public async Task 화면_흐름_입구에서_만들고_찾아서_신청하고_수락한다()
    {
        // 입구: 팀이 없으면 만들기
        var dad = await LoggedIn("dad@example.com");
        var home = await dad.GetStringAsync("/jamsil-kids/word-mission");
        Assert.Contains("href=\"/jamsil-kids/family-team\"", home);
        Assert.Contains("우리 가족팀 만들기", home);

        // 만들기
        var page = await dad.GetStringAsync("/jamsil-kids/family-team?New=true");
        Assert.Contains("새 가족팀 만들기", page);
        var r = await Post(dad, page, "Create", ("name", "<b>웃음폭탄네</b>"), ("relation", FamilyRelation.Father));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var mine = await dad.GetStringAsync(r.Headers.Location!.OriginalString);
        Assert.Contains("가족팀을 만들었어요", mine);
        Assert.Contains("&lt;b&gt;웃음폭탄네&lt;/b&gt;", mine);   // 이름은 자유이지만 글자 그대로만 보인다
        Assert.DoesNotContain("<b>웃음폭탄네</b>", mine);

        // 찾기 → 신청
        var kid = await LoggedIn("cheolsu@example.com");
        var found = await kid.GetStringAsync("/jamsil-kids/family-team?Q=%ED%8F%AD%ED%83%84");
        Assert.Contains("<mark>폭탄</mark>", found);
        Assert.Contains("김민호", found);
        int team;
        using (var raw = InMemoryDb.Raw(_db)) team = raw.FamilyTeams.Single().Id;
        var join = await kid.GetStringAsync($"/jamsil-kids/family-team?Join={team}");
        Assert.Contains("나는 이 가족의 누구인가요?", join);
        r = await Post(kid, join, "Request", ("team", team.ToString()), ("relation", FamilyRelation.Student));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("수락을 기다리는 중", await kid.GetStringAsync("/jamsil-kids/family-team"));

        // 입구에 숫자, 팀 화면에서 수락
        Assert.Contains("기다리는 신청 1건", await dad.GetStringAsync("/jamsil-kids/word-mission"));
        mine = await dad.GetStringAsync("/jamsil-kids/family-team");
        Assert.Contains("학생으로 신청", mine);
        int request;
        using (var raw = InMemoryDb.Raw(_db)) request = raw.FamilyJoinRequests.Single().Id;
        r = await Post(dad, mine, "Accept", ("request", request.ToString()));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var after = await kid.GetStringAsync("/jamsil-kids/family-team");
        Assert.Contains("구성원 2명", after);
        Assert.Contains("800", after);   // 500 + 300

        // 잘못된 값이면 이유를 보여 준다
        r = await Post(kid, after, "Relation", ("relation", "Uncle"));
        Assert.Contains("나는 누구인지 골라 주세요", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 화면_교사만이면_만들_수_없다는_안내만_보인다()
    {
        var t = InMemoryDb.SeedAccount(_db, "teacher@example.com", "박선생");
        InMemoryDb.SeedMembership(_db, _kids.Id, t.Id, MembershipStatus.Approved, Roles.Teacher);
        var c = await LoggedIn("teacher@example.com");
        var page = await c.GetStringAsync("/jamsil-kids/family-team");
        Assert.Contains("참여자가 되면", page);
        Assert.DoesNotContain("새 가족팀 만들기", page);
    }
}
