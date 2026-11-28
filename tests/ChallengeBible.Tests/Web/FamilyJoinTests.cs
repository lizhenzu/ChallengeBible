using System.Net;
using Microsoft.EntityFrameworkCore;
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

    [Fact]   // 팀보기(2026-11-27 사용자 민트 시안 TeamB · TeamOthers)
    public async Task 팀보기는_내_팀만_보이고_응원은_하루_한_번이며_다른_팀_랭킹은_따로_본다()
    {
        await DadAndKid();
        Assert.Equal(FamilyError.None, await As("mom").CreateAsync("햇살가족", FamilyRelation.Mother));
        var dad = await LoggedIn("dad@example.com");
        var page = await dad.GetStringAsync("/jamsil-kids/family-team");
        Assert.Contains("<b>팀보기</b>", page);
        Assert.Contains("웃음폭탄네", page);
        Assert.Contains("팀 랭킹 <b>1위</b>", page);
        Assert.Contains("멤버별 기록", page);
        Assert.Contains("연속 0일", page);
        Assert.Contains("우리 팀 선물함", page);
        Assert.Contains("href=\"/jamsil-kids/family-team/others\"", page);
        Assert.Contains("href=\"/jamsil-kids/family-team/manage\"", page);        // 팀 관리
        Assert.DoesNotContain("햇살가족", page);                                       // 다른 팀은 별도 화면
        Assert.Contains("마지막 수행 1월 3일", page);                                    // 응원하기: 오늘 아직 안 한 팀원(철수)

        // 응원: 쪽지 한 통, 같은 사람에게 하루에 한 번
        var kidAccount = _p["cheolsu"].UserName;
        var r = await Post(dad, page, "Cheer", ("target", kidAccount));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var after = await dad.GetStringAsync(r.Headers.Location!.OriginalString);
        Assert.Contains("응원을 보냈어요", after);
        Assert.Contains("응원 보냈어요 ✓", after);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(1, raw.Messages.Count(m => m.RecipientUserName == kidAccount && m.Body == TeamCheerService.Body));
        var r2 = await Post(dad, after, "Cheer", ("target", kidAccount));
        Assert.Contains("오늘은 이미 응원을 보냈어요", await dad.GetStringAsync(r2.Headers.Location!.OriginalString));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(1, raw.Messages.Count(m => m.RecipientUserName == kidAccount && m.Body == TeamCheerService.Body));
        var self = await Post(dad, after, "Cheer", ("target", _p["dad"].UserName));   // 나에게는 보내지 않는다
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(0, raw.Messages.Count(m => m.RecipientUserName == _p["dad"].UserName && m.Body == TeamCheerService.Body));

        // 다른 팀 랭킹: 우리 팀(순위)은 맨 위, 정렬 · 방향
        var others = await dad.GetStringAsync("/jamsil-kids/family-team/others");
        Assert.Contains("다른 팀 랭킹", others);
        Assert.Contains("햇살가족", others);
        Assert.Contains("우리 팀", others);
        Assert.Contains("점수순 ↓", others);
        Assert.Contains("href=\"/jamsil-kids/family-team/others?o=asc\"", others);   // 같은 정렬을 다시 누르면 반대
        Assert.Contains("팀명순", await dad.GetStringAsync("/jamsil-kids/family-team/others?s=name"));
        Assert.Contains("수행날짜순 ↓", await dad.GetStringAsync("/jamsil-kids/family-team/others?s=date"));
    }

    [Fact]   // 팀 초대 · 참여하기(2026-11-27 민트 시안)
    public async Task 초대_화면에서_참여하면_팀에_합류하고_다음에_할게요는_오늘_숨긴다()
    {
        await DadAndKid();
        await DadInvites("other");
        var inv = (await As("other").StateAsync()).Invites.Single();
        var c = await LoggedIn("other@example.com");
        var page = await c.GetStringAsync($"/jamsil-kids/family-invite/{inv.InviteId}");
        Assert.Contains("<b>가족팀 초대</b>", page);   // 세부 화면 머리줄(← 제목)
        Assert.Contains("<b>김민호</b>님이 초대했어요", page);
        Assert.Contains("웃음폭탄네", page);
        Assert.Contains("함께하는 팀원", page);
        Assert.Contains("팀은 한 곳만 참여할 수 있어요", page);
        Assert.Contains(">참여하기</button>", page);
        Assert.Contains(">다음에 할게요</button>", page);

        // 다음에 할게요 → 「나중에 볼게요」 → 홈 배너는 오늘 숨는다
        var tok = TokenRx().Match(page).Groups[1].Value;
        var later = await c.PostAsync($"/jamsil-kids/family-invite/{inv.InviteId}?handler=Later", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = tok }));
        Assert.Equal(HttpStatusCode.Redirect, later.StatusCode);
        Assert.Contains("초대를 나중에 볼게요", await c.GetStringAsync(later.Headers.Location!.OriginalString));
        Assert.DoesNotContain("가족팀 초대가 왔어요", await c.GetStringAsync("/jamsil-kids/word-mission"));

        // 참여하기 → 「참여했어요」 → 팀 카드
        var again = await c.GetStringAsync($"/jamsil-kids/family-invite/{inv.InviteId}");
        var tok2 = TokenRx().Match(again).Groups[1].Value;
        var join = await c.PostAsync($"/jamsil-kids/family-invite/{inv.InviteId}?handler=Accept", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = tok2, ["relation"] = inv.Relation }));
        Assert.Equal(HttpStatusCode.Redirect, join.StatusCode);
        var done = await c.GetStringAsync(join.Headers.Location!.OriginalString);
        Assert.Contains("웃음폭탄네에 참여했어요", done);
        Assert.Contains("홈으로 가기", done);
        Assert.NotNull((await As("other").StateAsync()).Mine);
        Assert.Contains("우리 가족팀", await c.GetStringAsync("/jamsil-kids/word-mission"));
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

    /// <summary>알림 띠(2026-11-28)의 수락 · 거절 · 확인 · 다음에 할게요 · 신청 취소는 합류 흐름 화면으로 보낸다.</summary>
    private static Task<HttpResponseMessage> PostJoin(HttpClient c, string page, string handler, params (string K, string V)[] form)
    {
        var fields = form.Select(f => new KeyValuePair<string, string>(f.K, f.V)).Prepend(new("__RequestVerificationToken", TokenRx().Match(page).Groups[1].Value));
        return c.PostAsync($"/jamsil-kids/family-join/result?handler={handler}", new FormUrlEncodedContent(fields));
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
        Assert.Contains("가족·친구팀을 구성해 보세요", home);   // 팀이 없으면 만들기 안내 카드(2026-11-27 민트 시안)
        Assert.Contains(">가족팀 만들기</a>", home);

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
        Assert.EndsWith("/jamsil-kids/family-join/sent", r.Headers.Location!.OriginalString);   // 「신청을 보냈어요」 확인 화면(시안 JoinA9)
        var sent = await kid.GetStringAsync("/jamsil-kids/family-join/sent");
        Assert.Contains("신청을 보냈어요", sent);
        Assert.Contains("팀원 누구나 수락할 수 있어요", sent);
        var kidHome = await kid.GetStringAsync("/jamsil-kids/word-mission");
        Assert.Contains("수락을 기다리는 중이에요", kidHome);   // 홈 맨 위 대기 띠(시안 JoinA4)
        Assert.Contains("신청 취소", kidHome);

        // 알림 띠(숫자 배지 없음): 홈과 팀보기 맨 위에서 바로 수락
        var dadHome = await dad.GetStringAsync("/jamsil-kids/word-mission");
        Assert.Contains("합류 신청이 1건 와 있어요", dadHome);
        Assert.Contains("학생으로 신청", dadHome);
        Assert.Contains("방금", dadHome);
        Assert.DoesNotContain("mh-team-badge", dadHome);
        mine = await dad.GetStringAsync("/jamsil-kids/family-team");
        Assert.Contains("합류 신청이 1건 와 있어요", mine);
        int request;
        using (var raw = InMemoryDb.Raw(_db)) request = raw.FamilyRequests.Single().Id;
        r = await PostJoin(dad, dadHome, "Accept", ("request", request.ToString()), ("ret", "home"));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.DoesNotContain("합류 신청이 1건", await dad.GetStringAsync("/jamsil-kids/word-mission"));   // 처리하면 띠가 사라진다
        Assert.Contains("합류했어요! 환영해요", await kid.GetStringAsync("/jamsil-kids/word-mission"));   // 신청자 홈 맨 위 결과 띠
        var celebrate = await kid.GetStringAsync("/jamsil-kids/family-join/result");
        Assert.Contains("합류했어요! 환영해요", celebrate);
        Assert.Contains("우리 팀 보러 가기", celebrate);
        Assert.DoesNotContain("합류했어요! 환영해요", await kid.GetStringAsync("/jamsil-kids/word-mission"));   // 한 번 보면 끝
        var after = await kid.GetStringAsync("/jamsil-kids/family-team/manage");
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
        Assert.Equal(FamilyError.CreatorStays, await As("dad").LeaveAsync());   // R-222: 방장은 넘긴 뒤 나간다
        var kid = (await As("dad").StateAsync()).Mine!.Members.Single(m => !m.Me).MemberId;
        Assert.Equal(FamilyError.None, await As("dad").TransferAsync(kid));
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
        Assert.Contains("href=\"/jamsil-kids/family-team?Add=true\"", mine);   // 팀보기의 「팀원 초대하기 ›」 → 이름으로 찾기 화면
        Assert.Contains("팀원 초대하기", mine);

        var add = await dad.GetStringAsync("/jamsil-kids/family-team?Add=true&Q=%ED%95%98%EB%8A%98");
        Assert.Contains("문<mark>하늘</mark>", add);
        Assert.Contains("초대 보내기", add);
        r = await Post(dad, add, "Invite", ("participant", _p["other"].Pid.ToString()), ("relation", FamilyRelation.Student));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        mine = await dad.GetStringAsync(r.Headers.Location!.OriginalString);
        Assert.Contains("초대를 보냈어요", mine);
        Assert.Contains("학생으로 초대 · 수락을 기다리는 중", await dad.GetStringAsync("/jamsil-kids/family-team/manage"));   // 보낸 초대는 팀 관리 화면

        // 잘못 보내면 초대 화면에 이유가 나온다
        r = await Post(dad, add, "Invite", ("participant", _p["other"].Pid.ToString()), ("relation", FamilyRelation.Student), ("Q", "하늘"));
        var again = await r.Content.ReadAsStringAsync();
        Assert.Contains("이미 초대를 보냈어요", again);
        Assert.Contains("가족 초대하기", again);

        // 초대 배너는 부서 홈이 아니라 성경미션 홈 맨 위에 있다(2026-11-27 사용자 · 민트 시안)
        var other = await LoggedIn("other@example.com");
        var home = await other.GetStringAsync("/jamsil-kids");
        Assert.DoesNotContain("가족팀 초대가 왔어요", home);
        var missionHome = await other.GetStringAsync("/jamsil-kids/word-mission");
        Assert.Contains("초대가 왔어요", missionHome);
        Assert.Contains("(가족팀)에 초대했어요", missionHome);
        Assert.Contains("다음에 할게요", missionHome);
        Assert.Contains("href=\"/jamsil-kids/family-team\"", missionHome);
        Assert.Contains("가족·친구팀을 구성해 보세요", missionHome);
        var got = await other.GetStringAsync("/jamsil-kids/family-team");
        Assert.Contains("김민호님이 학생으로 초대했어요", got);
        Assert.Contains("handler=InviteReject", got);
        int invite;
        using (var raw = InMemoryDb.Raw(_db)) invite = raw.FamilyRequests.Single().Id;
        r = await Post(other, got, "InviteAccept", ("invite", invite.ToString()), ("relation", FamilyRelation.Student));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var after = await other.GetStringAsync(r.Headers.Location!.OriginalString);
        Assert.Contains("팀에 합류했어요", after);
        Assert.Contains("구성원 2명", await other.GetStringAsync("/jamsil-kids/family-team/manage"));   // 구성원 수는 팀 관리 화면

        // 보낸 초대가 사라지고 입구도 팀 점수로 바뀐다
        Assert.DoesNotContain("보낸 초대", await dad.GetStringAsync("/jamsil-kids/family-team/manage"));
        var joined = await other.GetStringAsync("/jamsil-kids");
        Assert.DoesNotContain("가족팀 초대가 왔어요", joined);
        Assert.DoesNotContain("우리 가족팀", joined);                              // 팀에 들어와도 홈 카드는 없다(2026-11-23 · ADR 0139)
        Assert.DoesNotContain("가족팀 · 친구팀을 만들어 보세요", joined);            // 팀이 있으니 만들기 유도도 없다
        Assert.Contains("구성원 2명", await other.GetStringAsync("/jamsil-kids/family-team/manage"));
    }

    [Fact]
    public async Task R212_홈_초대_배너_다음에_할래요는_오늘_숨긴다()
    {
        await DadAndKid();
        await DadInvites("other");
        var c = await LoggedIn("other@example.com");
        Assert.DoesNotContain("초대가 왔어요", await c.GetStringAsync("/jamsil-kids"));   // 부서 홈에서는 뺐다(2026-11-27)
        Assert.Contains("초대가 왔어요", await c.GetStringAsync("/jamsil-kids/word-mission"));   // 성경미션 홈 맨 위 알림 띠

        var tok = TokenRx().Match(await c.GetStringAsync("/jamsil-kids/family-team")).Groups[1].Value;   // 홈에는 폼이 없으니 다른 화면의 토큰을 쓴다
        var r = await c.PostAsync("/jamsil-kids?handler=SnoozeInvites", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = tok }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);

        var after = await c.GetStringAsync("/jamsil-kids/word-mission");
        Assert.DoesNotContain("초대가 왔어요", after);   // 오늘은 숨는다
        Assert.Contains("가족·친구팀을 구성해 보세요", after);   // 초대는 숨고 만들기 카드
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

    [Fact]
    public async Task 알림띠_신청은_거절하면_신청자_홈에_한번만_보이고_다시_신청할_수_있다()
    {
        await DadAndKid();
        Add(_kids, "ha3", "문하나", Roles.Participant);
        Assert.Equal(FamilyError.None, await As("ha3").RequestAsync(await TeamOf("dad"), FamilyRelation.Student));
        var band = await As("dad").BandAsync();
        var row = Assert.Single(band.Requests);
        Assert.Equal("문하나", row.Name);
        Assert.Equal("방금", row.When);
        Assert.Equal(JoinBlock.None, row.Block);
        Assert.NotNull((await As("ha3").BandAsync()).Pending);
        Assert.Empty((await As("mom").BandAsync()).Requests);   // 팀에 없는 사람은 신청을 받을 수 없다

        Assert.Equal(FamilyError.None, await As("dad").RejectAsync(row.RequestId));
        Assert.Empty((await As("dad").BandAsync()).Requests);   // 0건이면 띠가 없다
        var back = await As("ha3").BandAsync();
        Assert.Equal("REJECTED", back.Result!.Kind);   // 신청자 홈 결과 띠(사유는 없다)
        Assert.Null(back.Pending);
        Assert.Equal("REJECTED", (await As("ha3").TakeResultAsync())!.Kind);
        Assert.Null((await As("ha3").BandAsync()).Result);   // 한 번 보면 끝
        Assert.Equal(FamilyError.None, await As("ha3").RequestAsync(await TeamOf("dad"), FamilyRelation.Student));   // 거절 뒤에도 바로 다시 신청
    }

    [Fact]
    public async Task 알림띠_수락하면_신청자에게_합류_결과가_남고_이미_다른_팀이면_확인으로_치운다()
    {
        await DadAndKid();
        Add(_kids, "ha4", "문하라", Roles.Participant);
        Assert.Equal(FamilyError.None, await As("ha4").RequestAsync(await TeamOf("dad"), FamilyRelation.Student));
        var id = (await As("dad").BandAsync()).Requests.Single().RequestId;
        // 그 사이 신청자가 다른 팀에 들어갔다(초대 수락 등) — 띠는 수락 대신 「확인」
        using (var raw = InMemoryDb.Raw(_db))
        {
            var t = new FamilyTeam { TenantId = _kids.Id, Name = "저쪽 팀", AwardKey = "저쪽 팀", CreatorUserName = _p["ha4"].Pid, InsertDT = DateTime.UtcNow, UpdateDT = DateTime.UtcNow };
            t.Members.Add(new FamilyMember { TenantId = _kids.Id, UserName = _p["ha4"].Pid, Relation = FamilyRelation.Student, IsActive = true, InsertDT = DateTime.UtcNow, UpdateDT = DateTime.UtcNow });
            raw.FamilyTeams.Add(t);
            raw.SaveChanges();
        }
        Assert.Equal(JoinBlock.OtherTeam, (await As("dad").BandAsync()).Requests.Single().Block);
        Assert.Equal(FamilyError.None, await As("dad").RejectAsync(id, false));   // 확인 = 알림 없이 치움
        Assert.Empty((await As("dad").BandAsync()).Requests);
        Assert.Null((await As("ha4").BandAsync()).Result);
    }

    [Fact]
    public async Task 알림띠_신청_취소와_시간_표시()
    {
        await DadAndKid();
        Assert.Equal(FamilyError.None, await As("other").RequestAsync(await TeamOf("dad"), FamilyRelation.Student));
        var c = await LoggedIn("other@example.com");
        var home = await c.GetStringAsync("/jamsil-kids/word-mission");
        var r = await PostJoin(c, home, "Cancel");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var page = await c.GetStringAsync(r.Headers.Location!.OriginalString);
        Assert.Contains("신청을 취소했어요", page);
        Assert.Contains("다시 신청하기", page);
        Assert.Empty((await As("dad").BandAsync()).Requests);

        var now = new DateTimeOffset(2026, 11, 28, 3, 0, 0, TimeSpan.Zero);
        const string tz = "Asia/Seoul";
        Assert.Equal("방금", FamilyJoinService.RelTime(now.UtcDateTime.AddSeconds(-20), now, tz));
        Assert.Equal("5분 전", FamilyJoinService.RelTime(now.UtcDateTime.AddMinutes(-5), now, tz));
        Assert.Equal("2시간 전", FamilyJoinService.RelTime(now.UtcDateTime.AddHours(-2), now, tz));
        Assert.Equal("어제", FamilyJoinService.RelTime(now.UtcDateTime.AddHours(-14), now, tz));   // 한국 시각으로 어제
        Assert.Equal("11월 20일", FamilyJoinService.RelTime(now.UtcDateTime.AddDays(-8), now, tz));
    }

    [Fact]
    public async Task 알림띠_친구팀이_가득_차면_수락은_막고_거절만_된다()
    {
        // 친구팀(학생 2명까지): 신청이 온 뒤에 팀이 가득 찬 경우
        Add(_kids, "s3", "문하윤", Roles.Participant);
        Assert.Equal(FamilyError.None, await As("cheolsu").CreateAsync("친구팀", "", friend: true));
        var team = await TeamOf("cheolsu");
        Assert.Equal(FamilyError.None, await As("other").RequestAsync(team, FamilyRelation.Student));
        var rid = (await As("cheolsu").BandAsync()).Requests.Single().RequestId;
        using (var raw = InMemoryDb.Raw(_db))
        {
            var t = raw.FamilyTeams.Include(x => x.Members).Single(x => x.Id == team);
            t.Members.Add(new FamilyMember { TenantId = _kids.Id, UserName = _p["s3"].Pid, Relation = FamilyRelation.Student, IsActive = true, InsertDT = DateTime.UtcNow, UpdateDT = DateTime.UtcNow });
            raw.SaveChanges();
        }
        Assert.Equal(JoinBlock.Full, (await As("cheolsu").BandAsync()).Requests.Single().Block);
        Assert.Equal(FamilyError.FriendFull, await As("cheolsu").AcceptAsync(rid));
    }

    [Fact]   // 2026-11-28: 주요 화면 = ☰ 머리줄 + 하단 탭, 세부 화면 = ← 제목 · 미션홈 바로가기, 하단 탭 없음
    public async Task 주요_화면은_탭이_있고_세부_화면은_머리줄이_다르고_탭이_없다()
    {
        await DadAndKid();
        var dad = await LoggedIn("dad@example.com");
        foreach (var main in new[] { "/jamsil-kids/word-mission", "/jamsil-kids/gift-box", "/jamsil-kids/family-team", "/jamsil-kids/roster" })
        {
            var h = await dad.GetStringAsync(main);
            Assert.Contains("class=\"mh-tab\"", h);
            Assert.DoesNotContain("class=\"sbar\"", h);
            Assert.Contains("class=\"mh-home\" href=\"/\"", h);   // 「홈으로」 = 첫 화면(/)
        }
        foreach (var sub in new[] { "/jamsil-kids/family-team/manage", "/jamsil-kids/family-team/others", "/jamsil-kids/family-team?Add=true" })
        {
            var h = await dad.GetStringAsync(sub);
            Assert.DoesNotContain("class=\"mh-tab\"", h);
            Assert.Contains("class=\"sbar\"", h);
            Assert.Contains("class=\"sbar-book\" href=\"/jamsil-kids/word-mission\"", h);   // 「미션홈」 바로가기
            Assert.Contains("class=\"sbar-back\" href=\"/jamsil-kids/family-team\"", h);
        }
        var team = await dad.GetStringAsync("/jamsil-kids/family-team");
        Assert.True(team.IndexOf("class=\"tv-sum\"", StringComparison.Ordinal) < team.IndexOf("초대 · 구성원 관리", StringComparison.Ordinal));
        Assert.Contains("팀원 초대하기", team);
        Assert.Contains("구성원 관리", team);
        Assert.DoesNotContain("팀 관리", team);
        Assert.DoesNotContain("링크 복사", team);
        Assert.Contains("미션홈으로", await (await LoggedIn("cheolsu@example.com")).GetStringAsync("/jamsil-kids/family-join/result?k=cancel"));
    }

    [Fact]   // 신청자 홈 결과 띠: 거절 [다른 팀 찾기][확인] · 취소 [다시 신청하기][확인]
    public async Task 신청자_홈_결과_띠는_확인으로_숨기고_취소_띠도_남는다()
    {
        await DadAndKid();
        Add(_kids, "ha6", "문하솔", Roles.Participant);
        Assert.Equal(FamilyError.None, await As("ha6").RequestAsync(await TeamOf("dad"), FamilyRelation.Student));
        var c = await LoggedIn("ha6@example.com");
        var home = await c.GetStringAsync("/jamsil-kids/word-mission");
        var cancel = await PostJoin(c, home, "Cancel");
        Assert.Equal(HttpStatusCode.Redirect, cancel.StatusCode);
        home = await c.GetStringAsync("/jamsil-kids/word-mission");
        Assert.Contains("신청을 취소했어요", home);
        Assert.Contains("다시 신청하기", home);
        var ok = await PostJoin(c, home, "DismissResult", ("ret", "home"));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.DoesNotContain("신청을 취소했어요", await c.GetStringAsync("/jamsil-kids/word-mission"));

        Assert.Equal(FamilyError.None, await As("ha6").RequestAsync(await TeamOf("dad"), FamilyRelation.Student));
        Assert.Equal(FamilyError.None, await As("dad").RejectAsync((await As("dad").BandAsync()).Requests.Single().RequestId));
        home = await c.GetStringAsync("/jamsil-kids/word-mission");
        Assert.Contains("이번에는 합류하지 못했어요", home);
        Assert.Contains("다른 팀 찾기", home);
        await PostJoin(c, home, "DismissResult", ("ret", "home"));
        Assert.DoesNotContain("이번에는 합류하지 못했어요", await c.GetStringAsync("/jamsil-kids/word-mission"));
    }

    private FamilyJoinService AsTeacher()
    {
        var a = InMemoryDb.SeedAccount(_db, "t2@example.com", "최선생").UserName!;
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, a, MembershipStatus.Approved, Roles.Teacher);
        _p["t2"] = (a, m, a);
        return As("t2");
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
