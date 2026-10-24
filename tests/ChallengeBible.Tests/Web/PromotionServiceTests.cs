using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Tests.Web;

/// <summary>진급(0026 · 0030 · 0058, R-139). 유년부(kids) → 초등부(elem).</summary>
public class PromotionServiceTests
{
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids, _elem, _other;
    private readonly Account _kidsAdmin, _elemAdmin;

    public PromotionServiceTests()
    {
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        _other = InMemoryDb.SeedTenant(_db, "other-kids", "OTHERK");
        _kidsAdmin = InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜");
        _elemAdmin = InMemoryDb.SeedAccount(_db, "jieun@example.com", "박지은");
        InMemoryDb.SeedMembership(_db, _kids.Id, _kidsAdmin.Id, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
        InMemoryDb.SeedMembership(_db, _elem.Id, _elemAdmin.Id, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
    }

    private (PromotionService Svc, PromotionApprover Approver) In(Tenant t)
    {
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        var ctx = new TenantContext();
        ctx.Set(t, null);
        var appDb = new InMemoryDb(_db, scope);
        return (new PromotionService(appDb, ctx, TimeProvider.System), new PromotionApprover(appDb, new InMemorySystemDb(_db), ctx, TimeProvider.System));
    }

    /// <summary>유년부 참여자 + 미션 기록 · 점수 · 축하 · 보호 관계 · 성경 읽음 표시.</summary>
    private (Account Account, Membership Membership, int Pid) Kid(string email = "cheolsu@example.com", string name = "김철수")
    {
        var a = InMemoryDb.SeedAccount(_db, email, name);
        var m = InMemoryDb.SeedMembership(_db, _kids.Id, a.Id, MembershipStatus.Approved, Roles.Participant);
        var mom = InMemoryDb.SeedMembership(_db, _kids.Id, InMemoryDb.SeedAccount(_db, email + ".mom", "김영희").Id, MembershipStatus.Approved, Roles.Guardian);
        using var raw = InMemoryDb.Raw(_db);
        var p = new Participant { TenantId = _kids.Id, MembershipId = m.Id };
        raw.Participants.Add(p);
        raw.SaveChanges();
        var act = new Activity { TenantId = _kids.Id, ParticipantId = p.Id, MissionItem = "Word.Me", MissionDate = new(2027, 1, 4), Points = 1 };
        raw.Activities.Add(act);
        raw.SaveChanges();
        raw.PointLedgers.Add(new PointLedger { TenantId = _kids.Id, ParticipantId = p.Id, MissionDate = act.MissionDate, MissionItem = act.MissionItem, Points = 1, ActivityId = act.Id });
        raw.QuizAttempts.Add(new QuizAttempt { TenantId = _kids.Id, ParticipantId = p.Id, MissionDate = act.MissionDate, AttemptNo = 1, DailyQuizId = 1 });
        raw.MilestonesReached.Add(new MilestoneReached { TenantId = _kids.Id, ParticipantId = p.Id, Year = 2027, Threshold = 3000 });
        raw.GuardianLinks.Add(new GuardianLink { TenantId = _kids.Id, GuardianMembershipId = mom.Id, ParticipantId = p.Id, CreatedByAccountId = _kidsAdmin.Id });
        raw.VerseReadMarks.Add(new VerseReadMark { AccountId = a.Id, BookId = 43, Chapter = 3, Verse = 16 });
        raw.Accounts.Single(x => x.Id == a.Id).LastTenantId = _kids.Id;
        raw.SaveChanges();
        return (a, m, p.Id);
    }

    private async Task<int> SendOne(Membership m)
    {
        var (e, sent, _) = await In(_kids).Svc.SendAsync(_kidsAdmin.Id, [m.Id], "jselem");
        Assert.Equal((PromotionError.None, 1), (e, sent));
        using var raw = InMemoryDb.Raw(_db);
        return raw.PromotionRequests.Single(r => r.AccountId == m.AccountId && r.Status == PromotionStatus.Pending).Id;
    }

    [Fact]
    public async Task 보내면_받는_부서에_요청이_보이고_보낸_부서_기록은_그대로다()
    {
        var (_, m, pid) = Kid();
        await SendOne(m);

        var got = Assert.Single(await In(_elem).Svc.ReceivedAsync());
        Assert.Equal(("김철수", "cheolsu@example.com", "잠실새내교회 유년부", PromotionStatus.Pending), (got.Name, got.Email, got.OtherName, got.Status));
        Assert.Equal(1, await In(_elem).Svc.PendingReceivedCountAsync());
        Assert.Equal("jamsil-elem 부서", Assert.Single(await In(_kids).Svc.SentAsync()).OtherName);

        using var raw = InMemoryDb.Raw(_db);
        Assert.True(raw.Memberships.Any(x => x.Id == m.Id && x.Status == MembershipStatus.Approved));   // 0026 결정 4
        Assert.True(raw.Activities.Any(x => x.ParticipantId == pid));
        Assert.False(raw.Memberships.Any(x => x.TenantId == _elem.Id && x.AccountId == m.AccountId));    // 0058: 승인 때 만든다
    }

    [Fact]
    public async Task R139_승인하면_새_부서_소속이_되고_이전_부서_기록은_모두_지워진다()
    {
        var (a, m, pid) = Kid();
        var id = await SendOne(m);

        Assert.Equal(PromotionError.None, await In(_elem).Approver.ApproveAsync(_elemAdmin.Id, id, [Roles.Participant], " 사랑반 "));

        using var raw = InMemoryDb.Raw(_db);
        var now = raw.Memberships.Include(x => x.Roles).Single(x => x.TenantId == _elem.Id && x.AccountId == a.Id);
        Assert.Equal((MembershipStatus.Approved, "사랑반"), (now.Status, now.GroupLabel));
        Assert.Equal([Roles.Participant], now.Roles.Select(r => r.Role));
        Assert.True(raw.Participants.Any(p => p.TenantId == _elem.Id && p.MembershipId == now.Id && p.IsActive));

        Assert.False(raw.Memberships.Any(x => x.Id == m.Id));
        Assert.False(raw.MembershipRoles.Any(x => x.MembershipId == m.Id));
        Assert.False(raw.Participants.Any(x => x.Id == pid));
        Assert.False(raw.Activities.Any(x => x.ParticipantId == pid));
        Assert.False(raw.PointLedgers.Any(x => x.ParticipantId == pid));
        Assert.False(raw.QuizAttempts.Any(x => x.ParticipantId == pid));
        Assert.False(raw.MilestonesReached.Any(x => x.ParticipantId == pid));
        Assert.False(raw.GuardianLinks.Any(x => x.ParticipantId == pid));
        Assert.True(raw.VerseReadMarks.Any(x => x.AccountId == a.Id));                       // 계정 범위는 그대로(R-158)
        Assert.Equal(_elem.Id, raw.Accounts.Single(x => x.Id == a.Id).LastTenantId);
        Assert.Equal(PromotionStatus.Approved, raw.PromotionRequests.Single().Status);
        var log = Assert.Single(raw.AuditLogs);
        Assert.Equal(("PromotionApproved", _elemAdmin.Id, (int?)_elem.Id), (log.Action, log.ActorAccountId, log.TenantId));
    }

    [Fact]
    public async Task 거절하거나_취소하면_아무것도_지우지_않는다()
    {
        var (_, m, pid) = Kid();
        var (_, m2, pid2) = Kid("younghee@example.com", "최민준");
        var id = await SendOne(m);
        var id2 = await SendOne(m2);

        Assert.Equal(PromotionError.None, await In(_elem).Svc.RejectAsync(_elemAdmin.Id, id));
        Assert.Equal(PromotionError.None, await In(_kids).Svc.CancelAsync(_kidsAdmin.Id, id2));
        Assert.Equal(PromotionError.BadState, await In(_elem).Approver.ApproveAsync(_elemAdmin.Id, id, [Roles.Participant], null));
        Assert.Equal(PromotionError.BadState, await In(_elem).Svc.RejectAsync(_elemAdmin.Id, id2));

        using var raw = InMemoryDb.Raw(_db);
        Assert.True(raw.Memberships.Any(x => x.Id == m.Id && x.Status == MembershipStatus.Approved));
        Assert.True(raw.Activities.Any(x => x.ParticipantId == pid) && raw.Activities.Any(x => x.ParticipantId == pid2));
        Assert.False(raw.Memberships.Any(x => x.TenantId == _elem.Id && x.AccountId != _elemAdmin.Id));
        Assert.Equal([PromotionStatus.Rejected, PromotionStatus.Cancelled], raw.PromotionRequests.OrderBy(r => r.Id).Select(r => r.Status));
        Assert.Empty(raw.AuditLogs);
    }

    [Fact]
    public async Task R131_다른_부서의_진급_요청은_보이지도_처리되지도_않는다()
    {
        var (_, m, _) = Kid();
        var id = await SendOne(m);
        var other = In(_other);
        Assert.Empty(await other.Svc.ReceivedAsync());
        Assert.Empty(await other.Svc.SentAsync());
        Assert.Equal(PromotionError.NotFound, await other.Approver.ApproveAsync(_elemAdmin.Id, id, [Roles.Participant], null));
        Assert.Equal(PromotionError.NotFound, await other.Svc.RejectAsync(_elemAdmin.Id, id));
        Assert.Equal(PromotionError.NotFound, await other.Svc.CancelAsync(_kidsAdmin.Id, id));
        // 받는 쪽은 취소를, 보낸 쪽은 승인 · 거절을 못 한다
        Assert.Equal(PromotionError.NotFound, await In(_elem).Svc.CancelAsync(_elemAdmin.Id, id));
        Assert.Equal(PromotionError.NotFound, await In(_kids).Svc.RejectAsync(_kidsAdmin.Id, id));
        Assert.Equal(PromotionError.NotFound, await In(_kids).Approver.ApproveAsync(_kidsAdmin.Id, id, [Roles.Participant], null));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Equal(PromotionStatus.Pending, raw.PromotionRequests.Single().Status);
    }

    [Fact]
    public async Task 진행_중인_요청은_한_아이에_하나()
    {
        var (_, m, _) = Kid();
        var (_, m2, _) = Kid("minjun@example.com", "최민준");
        await SendOne(m);
        var (e, sent, skipped) = await In(_kids).Svc.SendAsync(_kidsAdmin.Id, [m.Id, m2.Id], "JSELEM");
        Assert.Equal((PromotionError.None, 1), (e, sent));
        Assert.Equal(["김철수"], skipped);
        Assert.Equal(PromotionError.Pending, (await In(_kids).Svc.SendAsync(_kidsAdmin.Id, [m.Id], "JSELEM")).Error);
    }

    [Fact]
    public async Task 보내기는_받을_부서와_승인된_참여자를_확인한다()
    {
        var (_, m, _) = Kid();
        var svc = In(_kids).Svc;
        Assert.Equal(PromotionError.TargetNotFound, (await svc.SendAsync(_kidsAdmin.Id, [m.Id], "NOPE00")).Error);
        Assert.Equal(PromotionError.SameTenant, (await svc.SendAsync(_kidsAdmin.Id, [m.Id], "JSKIDS")).Error);
        var teacher = InMemoryDb.SeedMembership(_db, _kids.Id, InMemoryDb.SeedAccount(_db, "t@example.com", "정하늘").Id, MembershipStatus.Approved, Roles.Teacher);
        Assert.Equal(PromotionError.NotParticipant, (await svc.SendAsync(_kidsAdmin.Id, [teacher.Id], "JSELEM")).Error);
        var elemKid = InMemoryDb.SeedMembership(_db, _elem.Id, InMemoryDb.SeedAccount(_db, "e@example.com", "오서준").Id, MembershipStatus.Approved, Roles.Participant);
        Assert.Equal(PromotionError.NotFound, (await svc.SendAsync(_kidsAdmin.Id, [elemKid.Id], "JSELEM")).Error);   // 다른 부서 소속 Id
        Assert.Equal("jamsil-elem 부서", (await svc.LookupAsync("js-elem"))!.Name);
    }

    [Fact]
    public async Task 승인은_참여자_보호자_역할만_이미_소속이면_멈추고_신청_행은_이어_쓴다()
    {
        var (a, m, _) = Kid();
        var id = await SendOne(m);
        var approver = In(_elem).Approver;
        Assert.Equal(PromotionError.NoRole, await approver.ApproveAsync(_elemAdmin.Id, id, [], null));
        Assert.Equal(PromotionError.BadRole, await approver.ApproveAsync(_elemAdmin.Id, id, [Roles.TenantAdmin], null));

        // 기다리는 동안 아이가 참여 코드로 신청해 둔 행 → 그 행을 승인 소속으로 바꾼다(계정 · 부서당 1건)
        var applied = InMemoryDb.SeedMembership(_db, _elem.Id, a.Id, MembershipStatus.Requested, Roles.Guardian);
        Assert.Equal(PromotionError.None, await approver.ApproveAsync(_elemAdmin.Id, id, [Roles.Participant], null));
        using (var raw = InMemoryDb.Raw(_db))
        {
            var only = raw.Memberships.Include(x => x.Roles).Single(x => x.TenantId == _elem.Id && x.AccountId == a.Id);
            Assert.Equal((applied.Id, MembershipStatus.Approved), (only.Id, only.Status));
            Assert.Equal([Roles.Participant], only.Roles.Select(r => r.Role));
        }

        // 이미 승인된 소속이면 멈추고 아무것도 지우지 않는다
        var (b, m2, pid2) = Kid("minjun@example.com", "최민준");
        InMemoryDb.SeedMembership(_db, _elem.Id, b.Id, MembershipStatus.Approved, Roles.Participant);
        var id2 = await SendOne(m2);
        Assert.Equal(PromotionError.AlreadyMember, await approver.ApproveAsync(_elemAdmin.Id, id2, [Roles.Participant], null));
        using var raw2 = InMemoryDb.Raw(_db);
        Assert.True(raw2.Activities.Any(x => x.ParticipantId == pid2));
    }
}
