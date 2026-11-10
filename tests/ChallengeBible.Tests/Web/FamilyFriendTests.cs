using System.Net;
using System.Text.RegularExpressions;
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

/// <summary>친구팀 · 방장 · 관리자 팀 만들기 · 들어감 알림 · 선물 받은 팀 잠금(ADR 0093, R-221 ~ R-225). 오늘 = 2027-01-05(서울).</summary>
public partial class FamilyFriendTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids;
    private readonly Dictionary<string, (string Account, SeededMember M)> _p = [];

    public FamilyFriendTests(WebApplicationFactory<Program> app)
    {
        _app = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddScoped<IAppDb>(sp => new InMemoryDb(_db, sp.GetRequiredService<RequestScope>()));
            s.AddSingleton<ISystemDb>(new InMemorySystemDb(_db));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(FixedTime.SeoulMorning());
        }));
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        Add("cheolsu", "김철수", Roles.Participant);
        Add("haneul", "문하늘", Roles.Participant);
        Add("minji", "박민지", Roles.Participant);
        Add("dad", "김민호", Roles.Participant, Roles.Guardian);
    }

    private void Add(string login, string name, params string[] roles)
    {
        var a = InMemoryDb.SeedAccount(_db, login + "@example.com", name).UserName!;
        _p[login] = (a, InMemoryDb.SeedMembership(_db, _kids.Id, a, MembershipStatus.Approved, roles));
    }

    private string U(string login) => _p[login].M.UserName;

    private FamilyJoinService As(string login)
    {
        var (account, membership) = _p[login];
        var scope = new RequestScope();
        scope.SetTenant(_kids.Id);
        scope.SetUser(account, "id-" + account);
        var tc = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db)) tc.Set(raw.Tenants.Single(x => x.Id == _kids.Id), raw.DeptRoles.Single(d => d.Id == membership.Id));
        var teams = new FamilyTeamService(new InMemoryDb(_db, scope), scope, tc, new TenantClock(_time), InMemoryDb.Rules(_db));
        return new FamilyJoinService(new InMemoryDb(_db, scope), new InMemorySystemDb(_db), scope, tc, teams, _time);
    }

    private FamilyTeamService Admin()
    {
        var scope = new RequestScope();
        scope.SetTenant(_kids.Id);
        var tc = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db)) tc.Set(raw.Tenants.Single(x => x.Id == _kids.Id), null);
        return new FamilyTeamService(new InMemoryDb(_db, scope), scope, tc, new TenantClock(_time), InMemoryDb.Rules(_db));
    }

    private async Task<FamilyMine> Mine(string login) => (await As(login).StateAsync()).Mine!;

    /// <summary>철수가 만든 친구팀에 하늘이가 신청해 수락된 팀.</summary>
    private async Task<int> FriendPair()
    {
        Assert.Equal(FamilyError.None, await As("cheolsu").CreateAsync(null, "", friend: true));
        var id = (await Mine("cheolsu")).TeamId;
        Assert.Equal(FamilyError.None, await As("haneul").RequestAsync(id, ""));
        Assert.Equal(FamilyError.None, await As("cheolsu").AcceptAsync((await Mine("cheolsu")).Requests.Single().RequestId));
        return id;
    }

    private void Award(int teamId)
    {
        using var raw = InMemoryDb.Raw(_db);
        var t = raw.FamilyTeams.Single(x => x.Id == teamId);
        raw.TeamAwards.Add(new TeamAward { TenantId = _kids.Id, Kind = t.TeamType, AwardKey = t.AwardKey ?? t.Name, Threshold = 1000, AwardedAt = DateTime.UtcNow });
        raw.SaveChanges();
    }

    // ── R-221 친구팀 ──

    [Fact]
    public async Task R221_친구팀은_학생만_만들고_관계는_학생이고_이름은_친구팀이다()
    {
        Assert.Equal(FamilyError.FriendStudentOnly, await As("dad").CreateAsync(null, "", friend: true));
        Assert.Equal(FamilyError.None, await As("cheolsu").CreateAsync(null, FamilyRelation.Father, friend: true));   // 관계를 보내도 학생으로
        var m = await Mine("cheolsu");
        Assert.Equal(("김철수네 친구팀", true, true), (m.Name, m.Friend, m.IAmCreator));
        Assert.Equal(FamilyRelation.Student, m.Members.Single().Relation);
        Assert.Equal(FamilyError.FriendNoRelation, await As("cheolsu").SetRelationAsync(FamilyRelation.Father));
        Assert.True((await As("cheolsu").StateAsync()).IsStudent);
        Assert.False((await As("dad").StateAsync()).IsStudent);
    }

    [Fact]
    public async Task R221_친구팀은_2명까지이고_차면_남은_신청_초대를_지우고_찾기에서_빠진다()
    {
        Assert.Equal(FamilyError.None, await As("cheolsu").CreateAsync("말씀 단짝", "", friend: true));
        var id = (await Mine("cheolsu")).TeamId;
        Assert.Equal(FamilyError.None, await As("cheolsu").InviteAsync(U("minji"), ""));
        Assert.Equal([1], (await As("haneul").SearchAsync("단짝", friend: true)).Select(h => h.Seats));
        Assert.Empty(await As("haneul").SearchAsync("단짝"));                                    // 가족팀 찾기에는 없다
        Assert.Equal(FamilyError.None, await As("haneul").RequestAsync(id, ""));
        Assert.Equal(FamilyError.None, await As("cheolsu").AcceptAsync((await Mine("cheolsu")).Requests.Single().RequestId));

        var m = await Mine("cheolsu");
        Assert.True(m.Full);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Empty(raw.FamilyRequests);                 // 민지 초대도 지워졌다
        Assert.Empty(await As("minji").SearchAsync("단짝", friend: true));
        Assert.Equal(FamilyError.FriendFull, await As("minji").RequestAsync(id, ""));
        Assert.Equal(FamilyError.FriendFull, await As("cheolsu").InviteAsync(U("minji"), ""));
        Assert.Empty(await As("cheolsu").CandidatesAsync("민지"));
    }

    [Fact]
    public async Task R221_친구팀에는_학생만_초대하고_한_사람은_한_팀에만()
    {
        Assert.Equal(FamilyError.None, await As("cheolsu").CreateAsync(null, "", friend: true));
        Assert.Equal(FamilyError.TargetNotStudent, await As("cheolsu").InviteAsync(U("dad"), ""));
        Assert.Empty(await As("cheolsu").CandidatesAsync("김민호"));
        Assert.Equal(FamilyError.AlreadyInTeam, await As("cheolsu").CreateAsync(null, FamilyRelation.Student));   // 가족팀도 못 만든다
        Assert.Equal(FamilyError.None, await As("dad").CreateAsync(null, FamilyRelation.Father));
        Assert.Equal(FamilyError.AlreadyInTeam, await As("cheolsu").RequestAsync((await Mine("dad")).TeamId, FamilyRelation.Student));
    }

    // ── R-222 방장 ──

    [Fact]
    public async Task R222_내보내기는_방장만이고_방장은_넘긴_뒤에_나간다()
    {
        await FriendPair();
        var me = (await Mine("haneul")).Members;
        Assert.Equal(FamilyError.NotCreator, await As("haneul").KickAsync(me.Single(x => !x.Me).MemberId));
        Assert.Equal(FamilyError.NotCreator, await As("haneul").TransferAsync(me.Single(x => !x.Me).MemberId));
        Assert.Equal(FamilyError.CreatorStays, await As("cheolsu").LeaveAsync());

        var haneul = (await Mine("cheolsu")).Members.Single(x => !x.Me).MemberId;
        Assert.Equal(FamilyError.None, await As("cheolsu").TransferAsync(haneul));
        Assert.True((await Mine("haneul")).IAmCreator);
        Assert.Equal(FamilyError.None, await As("cheolsu").LeaveAsync());
        Assert.Single((await Mine("haneul")).Members);
        Assert.Equal(FamilyError.None, await As("haneul").LeaveAsync());                       // 혼자인 방장은 나갈 수 있고 팀이 사라진다
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.FamilyTeams);
    }

    [Fact]
    public async Task R222_방장이_내보낼_수_있고_방장이_다른_이유로_빠지면_다음_사람이_방장이다()
    {
        var id = await FriendPair();
        var haneul = (await Mine("cheolsu")).Members.Single(x => !x.Me).MemberId;
        Assert.Equal(FamilyError.None, await As("cheolsu").KickAsync(haneul));
        Assert.Single((await Mine("cheolsu")).Members);

        Assert.Equal(FamilyError.None, await As("haneul").RequestAsync(id, ""));
        Assert.Equal(FamilyError.None, await As("cheolsu").AcceptAsync((await Mine("cheolsu")).Requests.Single().RequestId));
        using (var raw = InMemoryDb.Raw(_db))
        {
            var t = raw.FamilyTeams.Single();
            var seat = raw.FamilyMembers.Single(m => m.UserName == U("cheolsu"));
            await FamilyTeamService.RemovePersonAsync(raw, _kids.Id, U("cheolsu"), default);   // 부서를 옮겨 빠짐
            raw.SaveChanges();
        }
        Assert.True((await Mine("haneul")).IAmCreator);
    }

    // ── R-223 관리자 ──

    [Fact]
    public async Task R223_관리자가_팀을_만들고_바로_넣고_방장을_바꾸고_지운다()
    {
        var admin = Admin();
        Assert.Equal(FamilyError.BadType, (await admin.CreateTeamAsync("TEAM", null, U("cheolsu"), null)).Error);
        Assert.Equal(FamilyError.TargetNotStudent, (await admin.CreateTeamAsync(TeamTypes.Friend, null, U("dad"), null)).Error);
        Assert.Equal(FamilyError.WrongRelation, (await admin.CreateTeamAsync(TeamTypes.Family, null, U("cheolsu"), FamilyRelation.Father)).Error);
        var (e, id) = await admin.CreateTeamAsync(TeamTypes.Family, null, U("dad"), FamilyRelation.Father);
        Assert.Equal(FamilyError.None, e);
        Assert.Equal(FamilyError.TargetInTeam, (await admin.CreateTeamAsync(TeamTypes.Family, null, U("dad"), FamilyRelation.Father)).Error);

        Assert.Equal(FamilyError.None, await admin.AddMemberAsync(id, U("cheolsu"), FamilyRelation.Student));
        var view = (await admin.GetAsync()).Teams.Single(t => t.Id == id);
        Assert.Equal(("김민호네 가족팀", 2), (view.DisplayName, view.Members.Count));
        Assert.True(view.Members.Single(m => m.Name == "김민호").IsCreator);
        Assert.Equal(FamilyError.CreatorKeep, await admin.RemoveMemberAsync(id, view.Members.Single(m => m.IsCreator).MemberId));
        Assert.Equal(FamilyError.None, await admin.SetCreatorAsync(id, view.Members.Single(m => m.Name == "김철수").MemberId));
        Assert.True((await Mine("cheolsu")).IAmCreator);

        Assert.Equal(FamilyError.None, await admin.DeleteTeamAsync(id));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.FamilyTeams);
        Assert.Empty(raw.FamilyMembers);
        Assert.Empty(raw.FamilyNotices);
    }

    [Fact]
    public async Task R223_관리자도_다_찬_친구팀에는_넣을_수_없고_팀_없는_사람만_넣는다()
    {
        var id = await FriendPair();
        Assert.Equal(FamilyError.FriendFull, await Admin().AddMemberAsync(id, U("minji"), null));
        var (_, other) = await Admin().CreateTeamAsync(TeamTypes.Family, "민지네", U("minji"), FamilyRelation.Student);
        Assert.Equal(FamilyError.TargetInTeam, await Admin().AddMemberAsync(other, U("cheolsu"), FamilyRelation.Student));
        var solo = (await Admin().GetAsync()).Solo;
        Assert.Equal(["김민호"], solo.Select(s => s.Name));
        Assert.False(solo.Single().IsStudent);
    }

    // ── R-224 들어감 알림 ──

    [Fact]
    public async Task R224_신청이_받아들여지거나_관리자가_넣으면_한_번_알린다()
    {
        await FriendPair();
        Assert.Equal("김철수네 친구팀", await As("haneul").TakeNoticeAsync());
        Assert.Null(await As("haneul").TakeNoticeAsync());                                       // 한 번만
        Assert.Null(await As("cheolsu").TakeNoticeAsync());                                      // 스스로 만든 사람에게는 없다

        var (_, id) = await Admin().CreateTeamAsync(TeamTypes.Family, "민호네", U("dad"), FamilyRelation.Father);
        Assert.Equal(FamilyError.None, await Admin().AddMemberAsync(id, U("minji"), FamilyRelation.Student));
        Assert.Equal("민호네", await As("minji").TakeNoticeAsync());
        Assert.Equal("민호네", await As("dad").TakeNoticeAsync());
    }

    // ── R-225 선물 받은 팀 잠금 ──

    [Fact]
    public async Task R225_선물을_받은_팀은_누구도_넣거나_빼거나_지울_수_없다()
    {
        Assert.Equal(FamilyError.None, await As("dad").CreateAsync("민호네", FamilyRelation.Father));
        var id = (await Mine("dad")).TeamId;
        Assert.Equal(FamilyError.None, await As("dad").InviteAsync(U("cheolsu"), FamilyRelation.Student));
        Assert.Equal(FamilyError.None, await As("cheolsu").AcceptInviteAsync((await As("cheolsu").StateAsync()).Invites.Single().InviteId, FamilyRelation.Student));
        Award(id);

        Assert.True((await Mine("dad")).Locked);
        Assert.Empty(await As("haneul").SearchAsync("민호"));
        Assert.Equal(FamilyError.TeamLocked, await As("haneul").RequestAsync(id, FamilyRelation.Student));
        Assert.Equal(FamilyError.TeamLocked, await As("dad").InviteAsync(U("haneul"), FamilyRelation.Student));
        Assert.Equal(FamilyError.AwardLocked, await As("dad").KickAsync((await Mine("dad")).Members.Single(x => !x.Me).MemberId));
        Assert.Equal(FamilyError.AwardLocked, await As("cheolsu").LeaveAsync());

        var admin = Admin();
        var view = (await admin.GetAsync()).Teams.Single();
        Assert.Equal((true, 1000), (view.Locked, view.LockedStage!.Value));
        Assert.Equal(FamilyError.TeamLocked, await admin.AddMemberAsync(id, U("haneul"), FamilyRelation.Student));
        Assert.Equal(FamilyError.AwardLocked, await admin.RemoveMemberAsync(id, view.Members.Single(m => !m.IsCreator).MemberId));
        Assert.Equal(FamilyError.AwardLocked, await admin.DeleteTeamAsync(id));
        Assert.Equal(FamilyError.None, await admin.SetCreatorAsync(id, view.Members.Single(m => !m.IsCreator).MemberId));   // 방장은 바꿀 수 있다
    }

    // ── 화면 ──

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
    public async Task 화면_흐름_학생은_친구팀을_고르고_만들며_들어감_알림은_한_번_보인다()
    {
        var kid = await LoggedIn("cheolsu@example.com");
        var find = await kid.GetStringAsync("/jamsil-kids/family-team");
        Assert.Contains("href=\"/jamsil-kids/family-team?Kind=friend\"", find);
        Assert.DoesNotContain("Kind=friend", await (await LoggedIn("dad@example.com")).GetStringAsync("/jamsil-kids/family-team"));   // 학부모에게는 친구팀이 없다

        var page = await kid.GetStringAsync("/jamsil-kids/family-team?Kind=friend&New=true");
        Assert.Contains("새 친구팀 만들기", page);
        var r = await kid.PostAsync("/jamsil-kids/family-team?handler=Create&Kind=friend", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value, ["name"] = "말씀 단짝",
        }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var mine = await kid.GetStringAsync(r.Headers.Location!.OriginalString);
        Assert.Contains("친구팀을 만들었어요", mine);
        Assert.Matches("class=\"fmt-type\">[^<]* 친구팀<", mine);   // 이모지는 문자 코드로 나온다
        Assert.Matches("class=\"fmt-crown\">[^<]* 방장<", mine);
        Assert.Contains("친구 초대하기", mine);

        Assert.Equal(FamilyError.None, await As("haneul").RequestAsync((await Mine("cheolsu")).TeamId, ""));
        Assert.Equal(FamilyError.None, await As("cheolsu").AcceptAsync((await Mine("cheolsu")).Requests.Single().RequestId));
        var friend = await LoggedIn("haneul@example.com");
        Assert.Contains("말씀 단짝에 들어갔어요", await friend.GetStringAsync("/jamsil-kids/family-team"));
        var again = await friend.GetStringAsync("/jamsil-kids/family-team");
        Assert.DoesNotContain("들어갔어요", again);
        Assert.Contains("자리가 다 찼어요", again);
        Assert.DoesNotContain("handler=Kick", again);   // 방장이 아니면 내보내기가 없다
    }

    [Fact]
    public async Task 화면_흐름_관리자가_새_팀을_만들고_넣는다()
    {
        var a = InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜");
        InMemoryDb.SeedMembership(_db, _kids.Id, a.UserName!, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        var c = await LoggedIn("eunhye@example.com");
        var page = await c.GetStringAsync("/jamsil-kids/admin/teams?New=true");
        Assert.Contains("새 팀 만들기", page);
        Assert.DoesNotContain("이은혜 · ", page);   // 교사는 팀에 들지 않는다
        var r = await c.PostAsync("/jamsil-kids/admin/teams?handler=Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(page).Groups[1].Value, ["type"] = TeamTypes.Friend, ["creator"] = U("minji"),
        }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var list = await c.GetStringAsync(r.Headers.Location!.OriginalString);
        Assert.Contains("박민지네 친구팀", list);
        Assert.Contains("ft-ty fr", list);
        int id;
        using (var raw = InMemoryDb.Raw(_db)) id = raw.FamilyTeams.Single().Id;
        var add = await c.PostAsync("/jamsil-kids/admin/teams?handler=Add", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRx().Match(list).Groups[1].Value, ["id"] = id.ToString(), ["person"] = U("haneul"),
        }));
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        Assert.Contains("2명이 다 찼어요", await c.GetStringAsync(add.Headers.Location!.OriginalString));
    }
}
