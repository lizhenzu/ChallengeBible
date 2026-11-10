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
    private readonly Dictionary<string, (string UserName, SeededMember M, string Pid)> _p = [];

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
        var a = InMemoryDb.SeedAccount(_db, login + "@example.com", name).UserName!;
        AddMembership(t, login, a, roles);
    }

    private void AddMembership(Tenant t, string key, string account, string[] roles)
    {
        var m = InMemoryDb.SeedMembership(_db, t.Id, account, MembershipStatus.Approved, roles);
        using var raw = InMemoryDb.Raw(_db);
        raw.SaveChanges();
        _p[key] = (account, m, m.UserName);
    }

    private void Pts(string login, int points)
    {
        using var raw = InMemoryDb.Raw(_db);
        raw.MissionActivities.Add(new MissionActivity { TenantId = _kids.Id, UserName = _p[login].Pid, MissionDate = new DateOnly(2027, 1, 3), MissionItem = "Word.Me", Points = points, PerformedAt = DateTime.UtcNow });
        raw.SaveChanges();
    }

    private FamilyJoinService As(string login, Tenant? t = null)
    {
        t ??= _kids;
        var (account, membership, _) = _p[login];
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        scope.SetUser(account, "id-" + account);
        var tc = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db)) tc.Set(raw.Tenants.Single(x => x.Id == t.Id), raw.DeptRoles.Single(d => d.Id == membership.Id));
        var teams = new FamilyTeamService(new InMemoryDb(_db, scope), scope, tc, new TenantClock(_time), InMemoryDb.Rules(_db));
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
    public async Task R204_이름이_비면_내_이름으로_짓고_쓰인_이름은_다시_못_쓴다()
    {
        Assert.Equal(FamilyError.None, await As("cheolsu").CreateAsync("  ", FamilyRelation.Student));
        Assert.Equal("김철수네 가족팀", (await As("cheolsu").StateAsync()).Mine!.Name);
        Assert.Equal(FamilyError.None, await As("dad").CreateAsync("같은이름", FamilyRelation.Father));
        Assert.Equal(FamilyError.NameTaken, await As("mom").CreateAsync("같은이름", FamilyRelation.Mother));
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
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(300, raw.MissionActivities.Where(l => l.UserName == _p["cheolsu"].Pid).Sum(l => l.Points));

        Assert.Equal(FamilyError.None, await As("cheolsu").RequestAsync(id, FamilyRelation.Student));   // 다시 돌아올 수 있다
        Assert.Equal(FamilyError.NotMember, await As("cheolsu").LeaveAsync());
    }

    [Fact]
    public async Task 시상_기록이_있는_팀은_나가기_내보내기_역할_바꾸기가_막힌다()
    {
        var id = await DadAndKid();
        using (var raw = InMemoryDb.Raw(_db))
        {
            var team = raw.FamilyTeams.Single(t => t.Id == id);
            raw.TeamAwards.Add(new TeamAward { TenantId = _kids.Id, Kind = TeamTypes.Family, AwardKey = team.AwardKey!, Threshold = 1000, AwardedAt = DateTime.UtcNow });
            raw.SaveChanges();
        }
        var kid = (await As("dad").StateAsync()).Mine!.Members.Single(m => !m.Me);
        Assert.Equal(FamilyError.AwardLocked, await As("cheolsu").LeaveAsync());
        Assert.Equal(FamilyError.AwardLocked, await As("dad").KickAsync(kid.MemberId));
        Assert.Equal(FamilyError.AwardLocked, await As("dad").SetRelationAsync(FamilyRelation.Mother));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(2, raw.FamilyMembers.Count());
    }

    [Fact]
    public async Task 박수_숨김_기록이_있는_팀도_잠긴다()
    {
        var id = await DadAndKid();
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.FamilyMilestonesHidden.Add(new FamilyMilestoneHidden { TenantId = _kids.Id, MilestoneConfigId = 1, FamilyTeamId = id, HiddenAt = DateTime.UtcNow });
            raw.SaveChanges();
        }
        Assert.Equal(FamilyError.AwardLocked, await As("cheolsu").LeaveAsync());
    }

    [Fact]
    public async Task 팀_규칙_학생은_학생만_학부모는_부모쪽만_교사는_불가()
    {
        Assert.Equal(FamilyError.WrongRelation, await As("cheolsu").CreateAsync("가", FamilyRelation.Father));
        Assert.Equal(FamilyError.WrongRelation, await As("mom").CreateAsync("나", FamilyRelation.Student));
        Assert.Equal(FamilyError.NotAllowed, await AsTeacher().CreateAsync("다", FamilyRelation.Student));
        Assert.Equal(FamilyError.None, await As("cheolsu").CreateAsync("라", FamilyRelation.Student));
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
    public async Task R208_마지막_사람이_나갈_때만_팀_삭제를_알린다()
    {
        Assert.Equal(FamilyError.None, await As("dad").CreateAsync("웃음폭탄네", FamilyRelation.Father));
        var id = await TeamOf("dad");
        Assert.Equal(FamilyError.None, await As("cheolsu").RequestAsync(id, FamilyRelation.Student));
        var request = (await As("dad").StateAsync()).Mine!.Requests.Single();
        Assert.Equal(FamilyError.None, await As("dad").AcceptAsync(request.RequestId));

        Assert.Equal((FamilyError.None, false), await As("cheolsu").LeaveReportAsync());
        Assert.Equal((FamilyError.None, true), await As("dad").LeaveReportAsync());
        Assert.Equal((FamilyError.NotMember, false), await As("dad").LeaveReportAsync());
        Assert.Contains("팀이 삭제됐어요", ChallengeBible.Web.Pages.Mission.FamilyTeamModel.OkText("leftgone"));
        Assert.DoesNotContain("삭제", ChallengeBible.Web.Pages.Mission.FamilyTeamModel.OkText("left"));
    }

    [Fact]
    public async Task R208_마지막_사람이_나가면_팀과_신청이_사라진다()
    {
        Assert.Equal(FamilyError.None, await As("dad").CreateAsync("웃음폭탄네", FamilyRelation.Father));
        var id = await TeamOf("dad");
        Assert.Equal(FamilyError.None, await As("cheolsu").RequestAsync(id, FamilyRelation.Student));
        Assert.Equal(FamilyError.None, await As("dad").LeaveAsync());
        using (var raw = InMemoryDb.Raw(_db))
        {
            Assert.Empty(raw.FamilyTeams);
            Assert.Empty(raw.FamilyMembers);
            Assert.Empty(raw.FamilyRequests);
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
        Assert.Equal("말씀 수호대", (await As("dad").StateAsync()).Mine!.Name);
        Assert.Equal(FamilyError.NotMember, await As("other").RenameAsync("가로채기"));

        Assert.Equal(FamilyError.None, await As("dad").SetRelationAsync(FamilyRelation.Grandfather));
        var mine = (await As("dad").StateAsync()).Mine!;
        Assert.Equal(FamilyRelation.Grandfather, mine.Members.Single(m => m.Name == "김민호").Relation);
        Assert.Equal(FamilyRelation.Student, mine.Members.Single(m => m.Name == "김철수").Relation);   // 남의 역할은 그대로
        Assert.Equal(FamilyError.BadRelation, await As("dad").SetRelationAsync("Uncle"));
        Assert.Equal(FamilyError.WrongRelation, await As("cheolsu").SetRelationAsync(FamilyRelation.Father));   // 학생은 '학생'만
    }

    [Fact]
    public async Task R210_한_사람은_한_부서의_가족팀에만_다른_부서_팀에_있으면_막힌다()
    {
        var dad = _p["dad"].UserName;
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
        AddMembership(lobby, "lobby-kid", InMemoryDb.SeedAccount(_db, "lk@example.com", "임시").UserName!, [Roles.Participant]);
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
                Values = RuleService.ToJson(RuleSet.Default with { Features = RuleSet.Default.Features with { FamilyTeam = false } }), ChangedByUserName = "t" });
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
        using (var raw = InMemoryDb.Raw(_db)) request = raw.FamilyRequests.Single().Id;
        r = await Post(dad, mine, "Accept", ("request", request.ToString()));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var after = await kid.GetStringAsync("/jamsil-kids/family-team");
        Assert.Contains("구성원 2명", after);
        Assert.Contains("800", after);   // 500 + 300

        // 잘못된 값이면 이유를 보여 준다
        r = await Post(kid, after, "Relation", ("relation", "Uncle"));
        Assert.Contains("나는 누구인지 골라 주세요", await r.Content.ReadAsStringAsync());
    }

    private async Task<int> DadInvites(string login, string relation = FamilyRelation.Student)
    {
        Assert.Equal(FamilyError.None, await As("dad").InviteAsync(_p[login].Pid, relation));
        return (await As(login).StateAsync()).Invites.Last().InviteId;
    }

    [Fact]
    public async Task R211_찾기는_팀이_없고_초대할_수_있는_같은_부서_사람의_이름만_보인다()
    {
        await DadAndKid();   // 아빠 + 철수
        Add(_kids, "ha2", "문하늘별", Roles.Participant);
        Add(_kids, "req", "신청하늘", Roles.Participant);
        Add(_kids, "inv", "초대하늘", Roles.Participant);
        Add(_kids, "gone", "다른부서하늘", Roles.Participant);
        AddMembership(_elem, "goneElem", _p["gone"].UserName, [Roles.Participant]);
        Assert.Equal(FamilyError.None, await As("goneElem", _elem).CreateAsync("저쪽 팀", FamilyRelation.Student));
        Assert.Equal(FamilyError.None, await As("req").RequestAsync(await TeamOf("dad"), FamilyRelation.Student));
        await DadInvites("inv");

        var found = await As("dad").CandidatesAsync(" 하늘 ");
        Assert.Equal(["문하늘", "문하늘별"], found.Select(c => c.Name));

        Assert.Empty(await As("dad").CandidatesAsync("김철수"));   // 이미 팀에 있다
        Assert.Empty(await As("dad").CandidatesAsync("윤지호"));   // 다른 부서
        Assert.Empty(await As("dad").CandidatesAsync("  "));
        Assert.Empty(await As("mom").CandidatesAsync("하늘"));     // 팀이 없으면 찾을 수 없다
    }

    [Fact]
    public async Task R211_구성원만_초대하고_상대는_수락_전까지_합류되지_않는다()
    {
        await DadAndKid();
        Assert.Equal(FamilyError.NotMember, await As("mom").InviteAsync(_p["other"].Pid, FamilyRelation.Student));
        Assert.Equal(FamilyError.BadRelation, await As("dad").InviteAsync(_p["other"].Pid, "Uncle"));
        Assert.Equal(FamilyError.TargetInTeam, await As("dad").InviteAsync(_p["cheolsu"].Pid, FamilyRelation.Student));
        Assert.Equal(FamilyError.NotFound, await As("dad").InviteAsync(_p["jiho"].Pid, FamilyRelation.Student));   // 다른 부서 참여자

        Assert.Equal(FamilyError.None, await As("cheolsu").InviteAsync(_p["other"].Pid, FamilyRelation.Student));   // 만든 사람이 아니어도 된다
        Assert.Equal(FamilyError.AlreadyInvited, await As("dad").InviteAsync(_p["other"].Pid, FamilyRelation.Student));

        var mine = (await As("dad").StateAsync()).Mine!;
        Assert.Equal(("문하늘", FamilyRelation.Student), (mine.Sent.Single().Name, mine.Sent.Single().Relation));
        Assert.Equal(2, mine.Members.Count);
        var got = await As("other").StateAsync();
        Assert.Null(got.Mine);
        var inv = got.Invites.Single();
        Assert.Equal(("웃음폭탄네", "김철수", 2), (inv.TeamName, inv.InvitedBy, inv.Members.Count));
    }

    [Fact]
    public async Task R211_우리_팀에_신청한_사람과_다른_부서_팀에_든_사람은_초대되지_않는다()
    {
        Assert.Equal(FamilyError.None, await As("dad").CreateAsync("웃음폭탄네", FamilyRelation.Father));
        Assert.Equal(FamilyError.None, await As("cheolsu").RequestAsync(await TeamOf("dad"), FamilyRelation.Student));
        Assert.Equal(FamilyError.TargetRequested, await As("dad").InviteAsync(_p["cheolsu"].Pid, FamilyRelation.Student));

        AddMembership(_elem, "otherElem", _p["other"].UserName, [Roles.Participant]);
        Assert.Equal(FamilyError.None, await As("otherElem", _elem).CreateAsync("저쪽 팀", FamilyRelation.Student));
        Assert.Equal(FamilyError.TargetOtherDept, await As("dad").InviteAsync(_p["other"].Pid, FamilyRelation.Student));
    }

    [Fact]
    public async Task R211_초대_취소는_우리_팀_구성원만_한다()
    {
        await DadAndKid();
        var invite = await DadInvites("other");
        Assert.Equal(FamilyError.None, await As("mom").CreateAsync("다른 팀", FamilyRelation.Mother));
        Assert.Equal(FamilyError.NotMember, await As("mom").CancelInviteAsync(invite));
        Assert.Equal(FamilyError.NotMember, await As("other").CancelInviteAsync(invite));
        Assert.Equal(FamilyError.None, await As("cheolsu").CancelInviteAsync(invite));   // 보낸 사람이 아니어도 된다
        Assert.Empty((await As("other").StateAsync()).Invites);
        Assert.Equal(FamilyError.NotFound, await As("dad").CancelInviteAsync(invite));
    }

    [Fact]
    public async Task R212_본인이_역할을_바꿔_수락하면_바로_구성원이_된다()
    {
        await DadAndKid();
        var invite = await DadInvites("mom", FamilyRelation.Grandmother);
        Assert.Equal(FamilyRelation.Grandmother, (await As("mom").StateAsync()).Invites.Single().Relation);

        Assert.Equal(FamilyError.NotFound, await As("other").AcceptInviteAsync(invite, FamilyRelation.Student));   // 다른 사람은 받을 수 없다
        Assert.Equal(FamilyError.NotFound, await As("dad").AcceptInviteAsync(invite, FamilyRelation.Student));
        Assert.Equal(FamilyError.BadRelation, await As("mom").AcceptInviteAsync(invite, "Uncle"));

        Assert.Equal(FamilyError.None, await As("mom").AcceptInviteAsync(invite, FamilyRelation.Mother));
        var mine = (await As("mom").StateAsync()).Mine!;
        Assert.Equal(3, mine.Members.Count);
        Assert.Equal(FamilyRelation.Mother, mine.Members.Single(m => m.Me).Relation);
        Assert.Empty(mine.Sent);
    }

    [Fact]
    public async Task R212_거절은_본인만_하고_기록이_남지_않아_다시_초대할_수_있다()
    {
        await DadAndKid();
        var invite = await DadInvites("other");
        Assert.Equal(FamilyError.NotFound, await As("dad").RejectInviteAsync(invite));
        Assert.Equal(FamilyError.None, await As("other").RejectInviteAsync(invite));
        Assert.Empty((await As("dad").StateAsync()).Mine!.Sent);
        await DadInvites("other");
    }

    [Fact]
    public async Task R213_수락하면_다른_신청과_초대가_함께_지워진다()
    {
        await DadAndKid();
        Assert.Equal(FamilyError.None, await As("mom").CreateAsync("다른 팀", FamilyRelation.Mother));
        Add(_kids, "third", "최셋째", Roles.Participant);
        Assert.Equal(FamilyError.None, await As("third").CreateAsync("셋째 팀", FamilyRelation.Student));

        var fromDad = await DadInvites("other");
        Assert.Equal(FamilyError.None, await As("mom").InviteAsync(_p["other"].Pid, FamilyRelation.Student));
        Assert.Equal(FamilyError.None, await As("other").RequestAsync(await TeamOf("third"), FamilyRelation.Student));   // 신청 중에도 초대를 받는다
        var s = await As("other").StateAsync();
        Assert.Equal(2, s.Invites.Count);
        Assert.NotNull(s.Pending);

        Assert.Equal(FamilyError.None, await As("other").AcceptInviteAsync(fromDad, FamilyRelation.Student));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.FamilyRequests);
        Assert.Empty(raw.FamilyRequests);
    }

    [Fact]
    public async Task R213_새_팀을_만들거나_신청이_수락돼도_받은_초대가_지워진다()
    {
        Assert.Equal(FamilyError.None, await As("dad").CreateAsync("웃음폭탄네", FamilyRelation.Father));
        await DadInvites("other");
        Assert.Equal(FamilyError.None, await As("other").CreateAsync("내 팀", FamilyRelation.Student));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Empty(raw.FamilyRequests);

        await DadInvites("cheolsu");
        Assert.Equal(FamilyError.None, await As("cheolsu").RequestAsync(await TeamOf("other"), FamilyRelation.Student));
        var r = (await As("other").StateAsync()).Mine!.Requests.Single();
        Assert.Equal(FamilyError.None, await As("other").AcceptAsync(r.RequestId));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Empty(raw.FamilyRequests);
    }

    [Fact]
    public async Task R213_마지막_사람이_나가면_보낸_초대도_사라진다()
    {
        Assert.Equal(FamilyError.None, await As("dad").CreateAsync("웃음폭탄네", FamilyRelation.Father));
        var invite = await DadInvites("other");
        Assert.Equal(FamilyError.None, await As("dad").LeaveAsync());
        using (var raw = InMemoryDb.Raw(_db)) Assert.Empty(raw.FamilyRequests);
        Assert.Empty((await As("other").StateAsync()).Invites);
        Assert.Equal(FamilyError.NotFound, await As("other").AcceptInviteAsync(invite, FamilyRelation.Student));
    }

    [Fact]
    public async Task R213_보낸_사람이_나가도_팀이_남으면_초대는_남는다()
    {
        await DadAndKid();
        var invite = await DadInvites("other");
        Assert.Equal(FamilyError.None, await As("dad").LeaveAsync());
        Assert.Equal("김민호", (await As("other").StateAsync()).Invites.Single().InvitedBy);
        Assert.Equal(FamilyError.None, await As("other").AcceptInviteAsync(invite, FamilyRelation.Student));
        Assert.Equal(2, (await As("other").StateAsync()).Mine!.Members.Count);
    }

    [Fact]
    public async Task R210_R212_다른_부서_팀에_들어가면_초대를_수락할_수_없다()
    {
        Assert.Equal(FamilyError.None, await As("dad").CreateAsync("웃음폭탄네", FamilyRelation.Father));
        var invite = await DadInvites("other");
        AddMembership(_elem, "otherElem", _p["other"].UserName, [Roles.Participant]);
        Assert.Equal(FamilyError.None, await As("otherElem", _elem).CreateAsync("저쪽 팀", FamilyRelation.Student));
        Assert.Equal(FamilyError.InOtherDept, await As("other").AcceptInviteAsync(invite, FamilyRelation.Student));
    }

    [Fact]
    public async Task 화면_흐름_초대하고_받아서_수락한다()
    {
        // 찾아서 초대
        var dad = await LoggedIn("dad@example.com");
        var page = await dad.GetStringAsync("/jamsil-kids/family-team?New=true");
        var r = await Post(dad, page, "Create", ("name", "웃음폭탄네"), ("relation", FamilyRelation.Father));
        var mine = await dad.GetStringAsync(r.Headers.Location!.OriginalString);
        Assert.Contains("href=\"/jamsil-kids/family-team?Add=true\"", mine);
        Assert.Contains("가족 초대하기", mine);

        var add = await dad.GetStringAsync("/jamsil-kids/family-team?Add=true&Q=%ED%95%98%EB%8A%98");
        Assert.Contains("문<mark>하늘</mark>", add);
        Assert.Contains("초대 보내기", add);
        r = await Post(dad, add, "Invite", ("participant", _p["other"].Pid.ToString()), ("relation", FamilyRelation.Student));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        mine = await dad.GetStringAsync(r.Headers.Location!.OriginalString);
        Assert.Contains("초대를 보냈어요", mine);
        Assert.Contains("학생으로 초대 · 수락을 기다리는 중", mine);

        // 잘못 보내면 초대 화면에 이유가 나온다
        r = await Post(dad, add, "Invite", ("participant", _p["other"].Pid.ToString()), ("relation", FamilyRelation.Student), ("Q", "하늘"));
        var again = await r.Content.ReadAsStringAsync();
        Assert.Contains("이미 초대를 보냈어요", again);
        Assert.Contains("가족 초대하기", again);

        // 부서 홈 말씀 찾기 위에 배너(미션 홈에는 없다), 찾기 화면 맨 위에서 수락
        var other = await LoggedIn("other@example.com");
        var home = await other.GetStringAsync("/jamsil-kids");
        Assert.Contains("가족팀 초대가 왔어요!", home);
        Assert.Contains("김민호님이 웃음폭탄네로 초대했어요", home);
        Assert.Contains("<a class=\"ivgo\" href=\"/jamsil-kids/family-team\">", home);
        Assert.Contains("다음에 할래요", home);
        Assert.True(home.IndexOf("hj-hi-top", StringComparison.Ordinal) < home.IndexOf("hj-inv", StringComparison.Ordinal));   // 인사 카드 다음
        Assert.True(home.IndexOf("hj-inv", StringComparison.Ordinal) < home.IndexOf("hj-card hj-sr", StringComparison.Ordinal));   // 말씀 찾기 바로 위
        var missionHome = await other.GetStringAsync("/jamsil-kids/word-mission");
        Assert.DoesNotContain("초대가 왔어요", missionHome);
        Assert.DoesNotContain("class=\"ct num\"", missionHome);                          // 입구 숫자에 초대를 세지 않는다
        Assert.Contains("우리 가족팀 만들기", missionHome);
        Assert.DoesNotContain("hj-inv", await dad.GetStringAsync("/jamsil-kids"));        // 보낸 사람 · 팀이 있는 사람에게는 없다
        var got = await other.GetStringAsync("/jamsil-kids/family-team");
        Assert.Contains("김민호님이 학생으로 초대했어요", got);
        Assert.Contains("handler=InviteReject", got);
        int invite;
        using (var raw = InMemoryDb.Raw(_db)) invite = raw.FamilyRequests.Single().Id;
        r = await Post(other, got, "InviteAccept", ("invite", invite.ToString()), ("relation", FamilyRelation.Student));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var after = await other.GetStringAsync(r.Headers.Location!.OriginalString);
        Assert.Contains("가족팀에 합류했어요", after);
        Assert.Contains("구성원 2명", after);

        // 보낸 초대가 사라지고 입구도 팀 점수로 바뀐다
        Assert.DoesNotContain("보낸 초대", await dad.GetStringAsync("/jamsil-kids/family-team"));
        Assert.DoesNotContain("가족팀 초대가 왔어요", await other.GetStringAsync("/jamsil-kids"));
    }

    [Fact]
    public async Task R212_다음에_할래요는_오늘만_배너에서_숨기고_초대는_남는다()
    {
        Assert.Equal(FamilyError.None, await As("dad").CreateAsync("웃음폭탄네", FamilyRelation.Father));
        await DadInvites("other");
        var c = await LoggedIn("other@example.com");
        var home = await c.GetStringAsync("/jamsil-kids");
        var token = TokenRx().Match(home).Groups[1].Value;
        var r = await c.PostAsync("/jamsil-kids?handler=SnoozeInvites", new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.DoesNotContain("초대가 왔어요", await c.GetStringAsync("/jamsil-kids"));

        // 초대는 그대로: 가족팀 화면에서 처리할 수 있다(다른 기기에서도 같은 기록)
        Assert.Contains("김민호님이 학생으로 초대했어요", await c.GetStringAsync("/jamsil-kids/family-team"));
        Assert.True((await As("other").StateAsync()).Invites.Single().SnoozedToday);

        // 내일이 되면 다시 보인다
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.UserSettings.Single(x => x.UserName == _p["other"].UserName).FamilyInvitesSnoozedAt = new DateTime(2027, 1, 3, 23, 0, 0, DateTimeKind.Utc);
            raw.SaveChanges();
        }
        Assert.Contains("초대가 왔어요", await c.GetStringAsync("/jamsil-kids"));
    }

    [Fact]
    public async Task R212_다음에_할래요_뒤에_새로_온_초대는_같은_날에도_보인다()
    {
        Assert.Equal(FamilyError.None, await As("dad").CreateAsync("웃음폭탄네", FamilyRelation.Father));
        Assert.Equal(FamilyError.None, await As("mom").CreateAsync("말씀 수호대", FamilyRelation.Mother));
        await DadInvites("other");
        Assert.Equal(FamilyError.None, await As("other").SnoozeInvitesAsync());
        using (var raw = InMemoryDb.Raw(_db))   // 시계가 멈춰 있어 "다음에 할래요"를 조금 앞으로 옮긴다
        {
            var setting = raw.UserSettings.Single(x => x.UserName == _p["other"].UserName);
            setting.FamilyInvitesSnoozedAt = setting.FamilyInvitesSnoozedAt!.Value.AddMinutes(-1);
            raw.SaveChanges();
        }
        Assert.Equal(FamilyError.None, await As("mom").InviteAsync(_p["other"].Pid, FamilyRelation.Student));
        using (var raw = InMemoryDb.Raw(_db))
        {
            foreach (var q in raw.FamilyRequests.Where(q => q.FromUserName == _p["dad"].UserName)) q.CreatedAt = q.CreatedAt.AddMinutes(-2);
            raw.SaveChanges();
        }
        var home = await (await LoggedIn("other@example.com")).GetStringAsync("/jamsil-kids");
        Assert.Contains("이수진님이 말씀 수호대로 초대했어요", home);
        Assert.DoesNotContain("웃음폭탄네", home);
    }

    [Fact]
    public async Task R212_다음에_할래요는_내_초대만_바꾼다()
    {
        Assert.Equal(FamilyError.None, await As("dad").CreateAsync("웃음폭탄네", FamilyRelation.Father));
        await DadInvites("other");
        await DadInvites("mom", FamilyRelation.Mother);
        Assert.Equal(FamilyError.None, await As("other").SnoozeInvitesAsync());
        Assert.False((await As("mom").StateAsync()).Invites.Single().SnoozedToday);
        Assert.Equal(FamilyError.None, await AsTeacher().SnoozeInvitesAsync());
    }

    private FamilyJoinService AsTeacher()
    {
        var a = InMemoryDb.SeedAccount(_db, "t2@example.com", "최선생").UserName!;
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, a, MembershipStatus.Approved, Roles.Teacher);
        _p["t2"] = (a, m, a);
        return As("t2");
    }

    [Fact]
    public async Task R212_여러_팀에서_초대받으면_배너는_먼저_온_팀과_나머지_수를_보인다()
    {
        Assert.Equal(FamilyError.None, await As("dad").CreateAsync("웃음폭탄네", FamilyRelation.Father));
        Assert.Equal(FamilyError.None, await As("mom").CreateAsync("말씀 수호대", FamilyRelation.Mother));
        await DadInvites("other");
        Assert.Equal(FamilyError.None, await As("mom").InviteAsync(_p["other"].Pid, FamilyRelation.Student));
        var c = await LoggedIn("other@example.com");
        Assert.Contains("웃음폭탄네 외 1팀에서 초대했어요", await c.GetStringAsync("/jamsil-kids"));
    }

    [Fact]
    public async Task 화면_교사만이면_만들_수_없다는_안내만_보인다()
    {
        var t = InMemoryDb.SeedAccount(_db, "teacher@example.com", "박선생");
        InMemoryDb.SeedMembership(_db, _kids.Id, t.UserName!, MembershipStatus.Approved, Roles.Teacher);
        var c = await LoggedIn("teacher@example.com");
        var page = await c.GetStringAsync("/jamsil-kids/family-team");
        Assert.Contains("참여자가 되면", page);
        Assert.DoesNotContain("새 가족팀 만들기", page);
    }
}
