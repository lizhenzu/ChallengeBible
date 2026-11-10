using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;

namespace ChallengeBible.Tests.Web;

/// <summary>소속 · 역할 · 진급 · 임시 부서 규칙(ADR 0090: 한 사람 한 부서, DeptRoleUserAssoc 한 줄).</summary>
public class MembershipRulesTests
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids, _elem, _lobby;
    private readonly AppUser _admin, _elemAdmin;

    public MembershipRulesTests()
    {
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        _lobby = InMemoryDb.SeedTenant(_db, "lobby", "LOBBY1");
        using (var raw = InMemoryDb.Raw(_db)) { raw.Tenants.Single(t => t.Id == _lobby.Id).IsLobby = true; raw.SaveChanges(); }
        _admin = InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜");
        _elemAdmin = InMemoryDb.SeedAccount(_db, "minsu@example.com", "박민수");
        InMemoryDb.SeedMembership(_db, _kids.Id, _admin.UserName!, MembershipStatus.Approved, Roles.TenantAdmin);
        InMemoryDb.SeedMembership(_db, _elem.Id, _elemAdmin.UserName!, MembershipStatus.Approved, Roles.TenantAdmin);
    }

    private (RequestScope Scope, TenantContext Ctx) In(Tenant t, AppUser? me = null)
    {
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        if (me is not null) scope.SetUser(me.UserName!, me.Id);
        var ctx = new TenantContext();
        ctx.Set(t, null);
        return (scope, ctx);
    }

    private MemberAdminService Members(Tenant t)
    {
        var (scope, ctx) = In(t);
        return new MemberAdminService(new InMemoryDb(_db, scope), new InMemorySystemDb(_db), ctx, _time);
    }

    private PromotionService Promo(Tenant t)
    {
        var (scope, ctx) = In(t);
        return new PromotionService(new InMemoryDb(_db, scope), ctx, _time);
    }

    private PromotionApprover Approver(Tenant t)
    {
        var (scope, ctx) = In(t);
        return new PromotionApprover(new InMemoryDb(_db, scope), new InMemorySystemDb(_db), ctx, _time);
    }

    private AppUser Person(string login, string name) => InMemoryDb.SeedAccount(_db, login + "@example.com", name);

    private void Act(Tenant t, AppUser u, DateOnly d, string item, int points)
    {
        using var raw = InMemoryDb.Raw(_db);
        raw.MissionActivities.Add(new MissionActivity { TenantId = t.Id, UserName = u.UserName!, MissionDate = d, MissionItem = item, Points = points, PerformedAt = DateTime.UtcNow });
        raw.SaveChanges();
    }

    private int Team(Tenant t, AppUser u, string relation = FamilyRelation.Student, string name = "웃음팀")
    {
        using var raw = InMemoryDb.Raw(_db);
        var team = new FamilyTeam { TenantId = t.Id, Name = name, AwardKey = name };
        team.Members.Add(new FamilyMember { TenantId = t.Id, UserName = u.UserName!, Relation = relation });
        raw.FamilyTeams.Add(team);
        raw.SaveChanges();
        return team.Id;
    }

    private void Award(Tenant t, string name = "웃음팀")
    {
        using var raw = InMemoryDb.Raw(_db);
        raw.TeamAwards.Add(new TeamAward { TenantId = t.Id, Kind = TeamTypes.Family, AwardKey = name, Threshold = 1000 });
        raw.SaveChanges();
    }

    private int PendingPromotion(AppUser kid, Tenant from, Tenant to)
    {
        using var raw = InMemoryDb.Raw(_db);
        var r = new PromotionRequest { UserName = kid.UserName!, SourceTenantId = from.Id, TargetTenantId = to.Id, RequestedByUserName = "t" };
        raw.PromotionRequests.Add(r);
        raw.SaveChanges();
        return r.Id;
    }

    // ── 역할 기준 ──

    [Theory]
    [InlineData("T1001", UserRoleKind.Student)]
    [InlineData("T1002", UserRoleKind.Teacher)]
    [InlineData("T1003", UserRoleKind.Teacher)]
    [InlineData("T1099", UserRoleKind.Etc)]
    [InlineData("T1005", UserRoleKind.Officer)]
    [InlineData(null, UserRoleKind.Parent)]
    [InlineData("  ", UserRoleKind.Parent)]
    public void 역할_코드로_사람_종류를_정한다(string? code, UserRoleKind kind) => Assert.Equal(kind, UserRole.KindOf(code));

    [Fact]
    public void 가족팀_관계_규칙_학생은_학생만_학부모와_기타는_부모쪽만_교사와_임원은_불가()
    {
        Assert.True(UserRole.RelationAllowed(UserRoleKind.Student, 0));
        Assert.False(UserRole.RelationAllowed(UserRoleKind.Student, 1));
        Assert.True(UserRole.RelationAllowed(UserRoleKind.Parent, 1));
        Assert.False(UserRole.RelationAllowed(UserRoleKind.Parent, 0));
        Assert.True(UserRole.RelationAllowed(UserRoleKind.Etc, 4));
        Assert.False(UserRole.CanJoinFamily(UserRoleKind.Teacher));
        Assert.False(UserRole.CanJoinFamily(UserRoleKind.Officer));
        Assert.True(UserRole.CanJoinFriend(UserRoleKind.Student));
        Assert.False(UserRole.CanJoinFriend(UserRoleKind.Parent));
    }

    [Fact]
    public void 화면_권한은_역할_코드와_관리자_지정에서_나온다()
    {
        Assert.Equal([Roles.Participant], UserRole.ScreenRoles("T1001", false));
        Assert.Equal([Roles.Participant, Roles.Guardian], UserRole.ScreenRoles(null, false));
        Assert.Equal([Roles.Participant, Roles.Teacher, Roles.TenantAdmin], UserRole.ScreenRoles("T1005", true));
    }

    // ── 승인 ──

    [Fact]
    public async Task 승인하면_소속_한_줄이_생기고_신청이_닫힌다()
    {
        var kid = Person("cheolsu", "김철수");
        var a = InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Requested, Roles.Participant);

        Assert.Equal(MemberError.None, await Members(_kids).ApproveAsync(_admin.UserName!, a.Id, "T1001"));

        using var raw = InMemoryDb.Raw(_db);
        var d = raw.DeptRoles.Single(x => x.UserId == kid.Id);
        Assert.Equal((_kids.Id, "T1001"), (d.TenantId, d.RoleCode));
        var app = raw.SignupApplications.Single(x => x.Id == a.Id);
        Assert.Equal((SignupStatus.Approved, _admin.UserName), (app.Status, app.DecidedBy));
    }

    [Fact]
    public async Task 승인은_신청_상태만_다른_부서_신청은_찾을_수_없다()
    {
        var kid = Person("cheolsu", "김철수");
        var a = InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Requested, Roles.Participant);
        Assert.Equal(MemberError.NotFound, await Members(_elem).ApproveAsync(_elemAdmin.UserName!, a.Id, "T1001"));
        Assert.Equal(MemberError.None, await Members(_kids).ApproveAsync(_admin.UserName!, a.Id, "T1001"));
        Assert.Equal(MemberError.BadState, await Members(_kids).ApproveAsync(_admin.UserName!, a.Id, "T1001"));
    }

    [Fact]
    public async Task 거절하면_소속이_생기지_않는다()
    {
        var kid = Person("cheolsu", "김철수");
        var a = InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Requested, Roles.Participant);
        Assert.Equal(MemberError.None, await Members(_kids).RejectAsync(_admin.UserName!, a.Id));
        Assert.Equal(MemberError.BadState, await Members(_kids).RejectAsync(_admin.UserName!, a.Id));
        using var raw = InMemoryDb.Raw(_db);
        Assert.False(raw.DeptRoles.Any(x => x.UserId == kid.Id));
        Assert.Equal(SignupStatus.Rejected, raw.SignupApplications.Single(x => x.Id == a.Id).Status);
    }

    [Fact]
    public async Task 목록은_상태와_이름과_역할로_거른다()
    {
        var kid = Person("cheolsu", "김철수");
        var mom = Person("younghee", "김영희");
        var wait = Person("haneul", "이하늘");
        InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        InMemoryDb.SeedMembership(_db, _kids.Id, mom.UserName!, MembershipStatus.Approved, Roles.Guardian);
        InMemoryDb.SeedMembership(_db, _kids.Id, wait.UserName!, MembershipStatus.Requested, Roles.Participant);
        var svc = Members(_kids);

        Assert.Equal(["이하늘"], (await svc.ListAsync(new MemberFilter(MembershipStatus.Requested, null, null))).Select(r => r.DisplayName));
        Assert.Equal(["김철수"], (await svc.ListAsync(new MemberFilter(MembershipStatus.Approved, "철수", null))).Select(r => r.DisplayName));
        Assert.Equal(["김영희"], (await svc.ListAsync(new MemberFilter(MembershipStatus.Approved, null, "-"))).Select(r => r.DisplayName));
        Assert.Equal(["김철수"], (await svc.ListAsync(new MemberFilter(MembershipStatus.Approved, null, "T1001"))).Select(r => r.DisplayName));
        var counts = await svc.CountsAsync();
        Assert.Equal((1, 3), (counts[MembershipStatus.Requested], counts[MembershipStatus.Approved]));
    }

    // ── 역할 바꾸기 ──

    [Fact]
    public async Task 역할이_바뀌면_맞지_않는_팀에서_빠진다()
    {
        var kid = Person("cheolsu", "김철수");
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        Team(_kids, kid);

        Assert.Equal(MemberError.None, await Members(_kids).ChangeRoleAsync(m.Id, "T1002"));

        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal("T1002", raw.DeptRoles.Single(x => x.Id == m.Id).RoleCode);
        Assert.Empty(raw.FamilyMembers);
        Assert.Empty(raw.FamilyTeams);
    }

    [Fact]
    public async Task 시상_기록이_있는_팀이면_역할을_바꾸지_않는다()
    {
        var kid = Person("cheolsu", "김철수");
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        Team(_kids, kid);
        Award(_kids);

        Assert.Equal(MemberError.TeamLocked, await Members(_kids).ChangeRoleAsync(m.Id, "T1002"));

        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal("T1001", raw.DeptRoles.Single(x => x.Id == m.Id).RoleCode);
        Assert.Single(raw.FamilyMembers);
    }

    [Fact]
    public async Task 관리자_지정과_해제_내_관리자_역할은_뺄_수_없다()
    {
        var teacher = Person("sunmi", "최선미");
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, teacher.UserName!, MembershipStatus.Approved, Roles.Teacher);
        var svc = Members(_kids);

        Assert.Equal(MemberError.None, await svc.SetAdminAsync(_admin.UserName!, m.Id, true));
        Assert.True((await svc.ListAsync(new MemberFilter(MembershipStatus.Approved, "선미", null))).Single().IsAdmin);
        Assert.Equal(MemberError.None, await svc.SetAdminAsync(_admin.UserName!, m.Id, false));
        Assert.False((await svc.ListAsync(new MemberFilter(MembershipStatus.Approved, "선미", null))).Single().IsAdmin);

        int mine;
        using (var raw = InMemoryDb.Raw(_db)) mine = raw.DeptRoles.Single(d => d.UserId == _admin.Id).Id;
        Assert.Equal(MemberError.SelfAdminRemove, await svc.SetAdminAsync(_admin.UserName!, mine, false));
    }

    // ── 임시 부서 ──

    [Fact]
    public async Task R171_가입하면_임시_부서에_들어가고_이미_소속이_있으면_그대로다()
    {
        var kid = Person("cheolsu", "김철수");
        var lobby = new LobbyService(new InMemorySystemDb(_db), _time);
        Assert.True(await lobby.EnrollAsync(kid.UserName!, kid.Id));
        Assert.True(await lobby.EnrollAsync(kid.UserName!, kid.Id));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(_lobby.Id, raw.DeptRoles.Single(d => d.UserId == kid.Id).TenantId);
    }

    [Fact]
    public async Task R173_R174_진짜_부서에_승인되면_소속과_기록이_옮겨지고_임시_소속은_없어진다()
    {
        var kid = Person("cheolsu", "김철수");
        InMemoryDb.SeedMembership(_db, _lobby.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        Act(_lobby, kid, Jan5, MissionItems.WordMe, 1);
        Act(_lobby, kid, Jan5.AddDays(-1), MissionItems.PrayerMe, 1);
        var a = InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Requested, Roles.Participant);

        Assert.Equal(MemberError.None, await Members(_kids).ApproveAsync(_admin.UserName!, a.Id, "T1001"));

        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(_kids.Id, raw.DeptRoles.Single(d => d.UserId == kid.Id).TenantId);
        Assert.Equal(2, raw.MissionActivities.Count(x => x.TenantId == _kids.Id && x.UserName == kid.UserName));
        Assert.False(raw.MissionActivities.Any(x => x.TenantId == _lobby.Id));
        Assert.Equal(LobbyService.MovedAction, raw.AuditLogs.Single().Action);
    }

    [Fact]
    public async Task R174_같은_날_같은_미션은_받는_부서_것을_남긴다()
    {
        var kid = Person("cheolsu", "김철수");
        InMemoryDb.SeedMembership(_db, _lobby.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        Act(_lobby, kid, Jan5, MissionItems.WordMe, 1);
        Act(_kids, kid, Jan5, MissionItems.WordMe, 3);
        var a = InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Requested, Roles.Participant);

        await Members(_kids).ApproveAsync(_admin.UserName!, a.Id, "T1001");

        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(3, raw.MissionActivities.Single().Points);
    }

    // ── 한 사람 한 부서 ──

    private JoinService Join() => new(new InMemoryDb(_db, new RequestScope()), new RequestScope(), _time);

    [Fact]
    public async Task 임시_부서_사람은_신청할_수_있고_진짜_부서_소속이나_기다리는_신청이_있으면_못_한다()
    {
        var kid = Person("cheolsu", "김철수");
        InMemoryDb.SeedMembership(_db, _lobby.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        var scope = new RequestScope();
        var join = new JoinService(new InMemoryDb(_db, scope), scope, _time);
        Assert.Equal(JoinError.None, (await join.ApplyAsync(kid.UserName!, "JSKIDS", SignupRoleKind.Student)).Error);

        var scope2 = new RequestScope();
        var again = new JoinService(new InMemoryDb(_db, scope2), scope2, _time);
        Assert.Equal(JoinError.AlreadyMember, (await again.ApplyAsync(kid.UserName!, "JSELEM", SignupRoleKind.Student)).Error);

        var scope3 = new RequestScope();
        var admin = new JoinService(new InMemoryDb(_db, scope3), scope3, _time);
        Assert.Equal(JoinError.AlreadyMember, (await admin.ApplyAsync(_admin.UserName!, "JSELEM", SignupRoleKind.Teacher)).Error);
    }

    [Fact]
    public async Task 신청_역할은_네_가지뿐이다()
    {
        var kid = Person("cheolsu", "김철수");
        Assert.Equal(JoinError.NoRole, (await Join().ApplyAsync(kid.UserName!, "JSKIDS", "")).Error);
        Assert.Equal(JoinError.BadRole, (await Join().ApplyAsync(kid.UserName!, "JSKIDS", "X")).Error);
    }

    // ── 진급 ──

    [Fact]
    public async Task R139_승인하면_소속과_기록이_받는_부서로_옮겨지고_통독_기록은_그대로다()
    {
        var kid = Person("cheolsu", "김철수");
        InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        Act(_kids, kid, Jan5, MissionItems.WordMe, 1);
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.ReadingRounds.Add(new ReadingRound { UserName = kid.UserName!, RoundNo = 1 });
            raw.QuizAttempts.Add(new QuizAttempt { TenantId = _kids.Id, UserName = kid.UserName!, MissionDate = Jan5, QuestionId = 9, AttemptNo = 1, IsCorrect = false });
            raw.SaveChanges();
        }
        var id = PendingPromotion(kid, _kids, _elem);

        Assert.Equal(PromotionError.None, await Approver(_elem).ApproveAsync(_elemAdmin.UserName!, id, "T1001"));

        using var after = InMemoryDb.Raw(_db);
        Assert.Equal(_elem.Id, after.DeptRoles.Single(d => d.UserId == kid.Id).TenantId);
        Assert.Equal(_elem.Id, after.MissionActivities.Single().TenantId);
        Assert.Equal(_elem.Id, after.QuizAttempts.Single().TenantId);
        Assert.Single(after.ReadingRounds);                                            // R-158
        Assert.Equal(PromotionStatus.Approved, after.PromotionRequests.Single().Status);
    }

    [Fact]
    public async Task R208_R213_승인하면_이전_부서의_팀_자리와_신청과_보호_관계가_정리되고_마지막_사람이면_팀도_지운다()
    {
        var kid = Person("cheolsu", "김철수");
        var mom = Person("younghee", "김영희");
        InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        InMemoryDb.SeedMembership(_db, _kids.Id, mom.UserName!, MembershipStatus.Approved, Roles.Guardian);
        var team = Team(_kids, kid);
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.GuardianLinks.Add(new GuardianLink { TenantId = _kids.Id, GuardianUserName = mom.UserName!, StudentUserName = kid.UserName!, CreatedByUserName = "t" });
            raw.FamilyRequests.Add(new FamilyRequest { TenantId = _kids.Id, FamilyTeamId = team, Kind = FamilyRequest.Invite, TargetUserName = kid.UserName!, FromUserName = mom.UserName!, RelationKey = "child" });
            raw.SaveChanges();
        }
        var id = PendingPromotion(kid, _kids, _elem);

        Assert.Equal(PromotionError.None, await Approver(_elem).ApproveAsync(_elemAdmin.UserName!, id, "T1001"));

        using var after = InMemoryDb.Raw(_db);
        Assert.Empty(after.FamilyMembers);
        Assert.Empty(after.FamilyTeams);
        Assert.Empty(after.FamilyRequests);
        Assert.Empty(after.GuardianLinks);
    }

    [Fact]
    public async Task 시상_기록이_있는_팀에_있으면_진급을_승인할_수_없다()
    {
        var kid = Person("cheolsu", "김철수");
        InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        Team(_kids, kid);
        Award(_kids);
        var id = PendingPromotion(kid, _kids, _elem);

        Assert.Equal(PromotionError.TeamLocked, await Approver(_elem).ApproveAsync(_elemAdmin.UserName!, id, "T1001"));

        using var after = InMemoryDb.Raw(_db);
        Assert.Equal(_kids.Id, after.DeptRoles.Single(d => d.UserId == kid.Id).TenantId);
        Assert.Equal(PromotionStatus.Pending, after.PromotionRequests.Single().Status);
    }

    [Fact]
    public async Task 거절하거나_취소하면_아무것도_옮기지_않는다()
    {
        var kid = Person("cheolsu", "김철수");
        InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        var id = PendingPromotion(kid, _kids, _elem);
        Assert.Equal(PromotionError.None, await Promo(_elem).RejectAsync(_elemAdmin.UserName!, id));
        var id2 = PendingPromotion(kid, _kids, _elem);
        Assert.Equal(PromotionError.None, await Promo(_kids).CancelAsync(_admin.UserName!, id2));

        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(_kids.Id, raw.DeptRoles.Single(d => d.UserId == kid.Id).TenantId);
        Assert.Equal([PromotionStatus.Rejected, PromotionStatus.Cancelled], raw.PromotionRequests.OrderBy(r => r.Id).Select(r => r.Status));
    }

    [Fact]
    public async Task R131_다른_부서의_진급_요청은_보이지도_처리되지도_않는다()
    {
        var other = InMemoryDb.SeedTenant(_db, "other", "OTHER1");
        var kid = Person("cheolsu", "김철수");
        InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        var id = PendingPromotion(kid, _kids, _elem);

        Assert.Empty(await Promo(other).ReceivedAsync());
        Assert.Empty(await Promo(other).SentAsync());
        Assert.Equal(PromotionError.NotFound, await Approver(other).ApproveAsync(_admin.UserName!, id, "T1001"));
        Assert.Equal(PromotionError.NotFound, await Promo(other).RejectAsync(_admin.UserName!, id));
    }

    [Fact]
    public async Task 보내기는_받을_부서를_확인하고_진행_중인_요청은_한_아이에_하나()
    {
        var kid = Person("cheolsu", "김철수");
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        var svc = Promo(_kids);

        Assert.Equal(PromotionError.TargetNotFound, (await svc.SendAsync(_admin.UserName!, [m.Id], "NOPE12")).Error);
        Assert.Equal(PromotionError.SameTenant, (await svc.SendAsync(_admin.UserName!, [m.Id], "JSKIDS")).Error);
        Assert.Equal(PromotionError.NotFound, (await svc.SendAsync(_admin.UserName!, [9999], "JSELEM")).Error);
        Assert.Equal((PromotionError.None, 1), Pick(await svc.SendAsync(_admin.UserName!, [m.Id], "JSELEM")));
        var again = await svc.SendAsync(_admin.UserName!, [m.Id], "JSELEM");
        Assert.Equal(PromotionError.Pending, again.Error);
        Assert.Equal(["김철수"], again.Skipped);

        static (PromotionError, int) Pick((PromotionError Error, int Sent, List<string> Skipped) r) => (r.Error, r.Sent);
    }

    [Fact]
    public async Task 받는_부서에_이미_소속이면_승인을_멈춘다()
    {
        var kid = Person("cheolsu", "김철수");
        InMemoryDb.SeedMembership(_db, _elem.Id, kid.UserName!, MembershipStatus.Approved, Roles.Participant);
        var id = PendingPromotion(kid, _kids, _elem);
        Assert.Equal(PromotionError.AlreadyMember, await Approver(_elem).ApproveAsync(_elemAdmin.UserName!, id, "T1001"));
    }

    // ── 로그인 ──

    private AccountService Accounts() => new(new InMemoryDb(_db, new RequestScope()), _time);

    [Fact]
    public async Task 가입_아이디는_이메일_앞부분이고_겹치면_가입할_수_없다()
    {
        var (a, e) = await Accounts().SignUpAsync(new SignUpInput("김철수", "Cheolsu@Example.com", "1234", "1234"));
        Assert.True(e.Ok);
        Assert.Equal(("cheolsu", "김철수"), (a!.UserName, a.DisplayName));

        var (b, e2) = await Accounts().SignUpAsync(new SignUpInput("다른사람", "cheolsu@other.org", "1234", "1234"));
        Assert.Null(b);
        Assert.Contains("아이디", e2["Email"]);
    }

    [Fact]
    public async Task 로그인_칸_하나에_아이디_또는_이메일()
    {
        await Accounts().SignUpAsync(new SignUpInput("김철수", "cheolsu@example.com", "1234", "1234"));
        Assert.NotNull((await Accounts().LoginAsync("CHEOLSU", "1234")).Account);
        Assert.NotNull((await Accounts().LoginAsync("cheolsu@example.com", "1234")).Account);
        Assert.Null((await Accounts().LoginAsync("cheolsu", "틀림")).Account);
        Assert.Null((await Accounts().LoginAsync("nobody", "1234")).Account);
    }

    [Fact]
    public async Task 사용_중지한_계정은_로그인할_수_없다()
    {
        await Accounts().SignUpAsync(new SignUpInput("김철수", "cheolsu@example.com", "1234", "1234"));
        using (var raw = InMemoryDb.Raw(_db)) { raw.Users.Single(u => u.UserName == "cheolsu").LockoutEnd = DateTimeOffset.MaxValue; raw.SaveChanges(); }
        Assert.Null((await Accounts().LoginAsync("cheolsu", "1234")).Account);
    }
}
