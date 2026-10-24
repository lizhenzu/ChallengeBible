using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Tests.Web;

public class MemberAdminServiceTests
{
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids, _elem;
    private readonly Account _admin;

    public MemberAdminServiceTests()
    {
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        _admin = InMemoryDb.SeedAccount(_db, "eunhye", "이은혜");
        InMemoryDb.SeedMembership(_db, _kids.Id, _admin.Id, MembershipStatus.Approved, Roles.Teacher, Roles.TenantAdmin);
    }

    /// <summary>그 부서 관리 화면에서 부르는 것처럼: RequestScope · TenantContext 를 그 부서로.</summary>
    private MemberAdminService In(Tenant t)
    {
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        var ctx = new TenantContext();
        ctx.Set(t, null);
        return new MemberAdminService(new InMemoryDb(_db, scope), new InMemorySystemDb(_db), ctx, TimeProvider.System);
    }

    private Membership Load(int id)
    {
        using var raw = InMemoryDb.Raw(_db);
        return raw.Memberships.Include(m => m.Roles).Single(m => m.Id == id);
    }

    private Membership Applicant(Tenant t, string login = "cheolsu", params string[] roles)
    {
        var a = InMemoryDb.SeedAccount(_db, login, login);
        return InMemoryDb.SeedMembership(_db, t.Id, a.Id, MembershipStatus.Requested, roles.Length == 0 ? [Roles.Participant] : roles);
    }

    [Fact]
    public async Task 승인하면_소속이_되고_역할과_반이_정해진다()
    {
        var m = Applicant(_kids);
        var e = await In(_kids).ApproveAsync(_admin.Id, m.Id, [Roles.Participant], " 사랑반 ");
        Assert.Equal(MemberError.None, e);
        var after = Load(m.Id);
        Assert.Equal(MembershipStatus.Approved, after.Status);
        Assert.Equal("사랑반", after.GroupLabel);
        Assert.Equal(_admin.Id, after.DecidedByAccountId);
        Assert.Equal([Roles.Participant], after.Roles.Select(r => r.Role));
    }

    [Fact]
    public async Task 여러_명_반_바꾸기는_이_부서의_승인된_참여자만()
    {
        Membership Approved(Tenant t, string login, params string[] roles) =>
            InMemoryDb.SeedMembership(_db, t.Id, InMemoryDb.SeedAccount(_db, login, login).Id, MembershipStatus.Approved, roles);
        var a = Approved(_kids, "cheolsu", Roles.Participant);
        var b = Approved(_kids, "seoyeon", Roles.Participant, Roles.Guardian);
        var teacher = Approved(_kids, "minsu", Roles.Teacher);
        var waiting = Applicant(_kids, "minjun");
        var other = Approved(_elem, "jiho", Roles.Participant);

        var moved = await In(_kids).RegroupAsync([a.Id, b.Id, teacher.Id, waiting.Id, other.Id, a.Id], "  믿음반 ");
        Assert.Equal(2, moved);
        Assert.Equal(("믿음반", "믿음반"), (Load(a.Id).GroupLabel, Load(b.Id).GroupLabel));
        Assert.Null(Load(teacher.Id).GroupLabel);                      // 참여자가 아니면 그대로
        Assert.Null(Load(waiting.Id).GroupLabel);                      // 신청 중이면 그대로
        Assert.Null(Load(other.Id).GroupLabel);                        // 다른 부서는 보이지도 않는다

        Assert.Equal(1, await In(_kids).RegroupAsync([a.Id], " "));     // 빈칸 = 반 없음
        Assert.Null(Load(a.Id).GroupLabel);
        Assert.Equal(0, await In(_kids).RegroupAsync([], "사랑반"));
    }

    [Fact]
    public async Task 승인은_신청_상태만_역할은_하나_이상()
    {
        var m = Applicant(_kids);
        Assert.Equal(MemberError.NoRole, await In(_kids).ApproveAsync(_admin.Id, m.Id, [], null));
        await In(_kids).ApproveAsync(_admin.Id, m.Id, [Roles.Participant], null);
        Assert.Equal(MemberError.BadState, await In(_kids).ApproveAsync(_admin.Id, m.Id, [Roles.Participant], null));
    }

    [Fact]
    public async Task 다른_부서의_소속은_찾을_수_없다_격리()
    {
        var other = Applicant(_elem, "minji");
        Assert.Equal(MemberError.NotFound, await In(_kids).ApproveAsync(_admin.Id, other.Id, [Roles.Participant], null));
        Assert.Equal(MemberError.NotFound, await In(_kids).RejectAsync(_admin.Id, other.Id));
        Assert.DoesNotContain(await In(_kids).ListAsync(new MemberFilter(MembershipStatus.Requested, null, null, null)), r => r.MembershipId == other.Id);
        Assert.Equal(MembershipStatus.Requested, Load(other.Id).Status);
    }

    [Fact]
    public async Task 내_관리자_역할은_뺄_수_없고_나를_정지할_수_없다()
    {
        var mine = (await In(_kids).ListAsync(new MemberFilter(MembershipStatus.Approved, "eunhye", null, null))).Single();
        Assert.Equal(MemberError.SelfAdminRemove, await In(_kids).UpdateAsync(_admin.Id, mine.MembershipId, [Roles.Teacher], null));
        Assert.Equal(MemberError.SelfSuspend, await In(_kids).SuspendAsync(_admin.Id, mine.MembershipId));
    }

    [Fact]
    public async Task 역할_바꾸기는_더하고_뺀다()
    {
        var m = Applicant(_kids);
        await In(_kids).ApproveAsync(_admin.Id, m.Id, [Roles.Participant], null);
        Assert.Equal(MemberError.None, await In(_kids).UpdateAsync(_admin.Id, m.Id, [Roles.Teacher], "믿음반"));
        var after = Load(m.Id);
        Assert.Equal([Roles.Teacher], after.Roles.Select(r => r.Role));
        Assert.Equal("믿음반", after.GroupLabel);
    }

    [Fact]
    public async Task 거절_정지_해제()
    {
        var a = Applicant(_kids, "a1");
        Assert.Equal(MemberError.None, await In(_kids).RejectAsync(_admin.Id, a.Id));
        Assert.Equal(MembershipStatus.Rejected, Load(a.Id).Status);

        var b = Applicant(_kids, "b1");
        await In(_kids).ApproveAsync(_admin.Id, b.Id, [Roles.Participant], null);
        Assert.Equal(MemberError.None, await In(_kids).SuspendAsync(_admin.Id, b.Id));
        Assert.Equal(MembershipStatus.Suspended, Load(b.Id).Status);
        Assert.Equal(MemberError.None, await In(_kids).ResumeAsync(_admin.Id, b.Id));
        Assert.Equal(MembershipStatus.Approved, Load(b.Id).Status);
    }

    [Fact]
    public async Task R168_보호자는_가족_부서를_고르기_전에는_참여자가_될_수_없다()
    {
        var m = Applicant(_kids, "younghee", Roles.Participant, Roles.Guardian);
        Assert.Equal(MemberError.FamilyTenantNotChosen, await In(_kids).ApproveAsync(_admin.Id, m.Id, [Roles.Participant, Roles.Guardian], null));
        // 보호자로만은 승인된다
        Assert.Equal(MemberError.None, await In(_kids).ApproveAsync(_admin.Id, m.Id, [Roles.Guardian], null));
    }

    [Fact]
    public async Task R168_가족_부서가_다른_부서면_여기서는_보호자로만()
    {
        var parent = InMemoryDb.SeedAccount(_db, "younghee", "김영희", familyTenantId: _kids.Id);
        var inElem = InMemoryDb.SeedMembership(_db, _elem.Id, parent.Id, MembershipStatus.Requested, Roles.Participant, Roles.Guardian);
        var elemAdmin = InMemoryDb.SeedAccount(_db, "woosung", "정우성");
        Assert.Equal(MemberError.FamilyTenantElsewhere, await In(_elem).ApproveAsync(elemAdmin.Id, inElem.Id, [Roles.Participant, Roles.Guardian], null));

        // 가족 부서(유년부)에서는 참여자 + 보호자로 승인된다
        var inKids = InMemoryDb.SeedMembership(_db, _kids.Id, parent.Id, MembershipStatus.Requested, Roles.Participant, Roles.Guardian);
        Assert.Equal(MemberError.None, await In(_kids).ApproveAsync(_admin.Id, inKids.Id, [Roles.Participant, Roles.Guardian], null));
    }

    [Fact]
    public async Task R168_다른_부서에서_보호자인_계정도_가족_부서_검사를_받는다()
    {
        var parent = InMemoryDb.SeedAccount(_db, "daeho", "김대호", familyTenantId: _elem.Id);
        InMemoryDb.SeedMembership(_db, _elem.Id, parent.Id, MembershipStatus.Approved, Roles.Guardian);
        var hereOnlyParticipant = InMemoryDb.SeedMembership(_db, _kids.Id, parent.Id, MembershipStatus.Requested, Roles.Participant);
        Assert.Equal(MemberError.FamilyTenantElsewhere, await In(_kids).ApproveAsync(_admin.Id, hereOnlyParticipant.Id, [Roles.Participant], null));
    }

    [Fact]
    public async Task 목록은_상태_이름_반으로_거른다()
    {
        var a = Applicant(_kids, "seoyeon");
        var b = Applicant(_kids, "minjun");
        await In(_kids).ApproveAsync(_admin.Id, a.Id, [Roles.Participant], "사랑반");
        await In(_kids).ApproveAsync(_admin.Id, b.Id, [Roles.Participant], "믿음반");
        var svc = In(_kids);
        Assert.Equal(["minjun"], (await svc.ListAsync(new MemberFilter(MembershipStatus.Approved, null, null, "믿음반"))).Select(r => r.Email));
        Assert.Equal(["seoyeon"], (await svc.ListAsync(new MemberFilter(MembershipStatus.Approved, "seo", null, null))).Select(r => r.Email));
        Assert.Equal(["eunhye"], (await svc.ListAsync(new MemberFilter(MembershipStatus.Approved, null, Roles.TenantAdmin, null))).Select(r => r.Email));
        Assert.Equal(3, (await svc.CountsAsync())[MembershipStatus.Approved]);
    }
}
