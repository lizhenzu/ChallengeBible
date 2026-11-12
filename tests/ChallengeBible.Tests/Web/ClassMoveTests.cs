using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;

namespace ChallengeBible.Tests.Web;

/// <summary>반별 학생 이동(ADR 0093): R-238 미배정반으로 · 꼬리표, R-239 타부서로 바로, 새친구반 표시.</summary>
public class ClassMoveTests
{
    private static readonly DateOnly Sun = new(2027, 1, 3);
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids, _elem, _lobby;
    private readonly int _c1, _kid, _plain, _teacherPid;
    private readonly AppUser _kidAcc;

    public ClassMoveTests()
    {
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
        _lobby = InMemoryDb.SeedTenant(_db, "lobby", "LOBBY1");
        using (var raw = InMemoryDb.Raw(_db)) { raw.Tenants.Single(t => t.Id == _lobby.Id).IsLobby = true; raw.SaveChanges(); }
        InMemoryDb.SeedMembership(_db, _kids.Id, InMemoryDb.SeedAccount(_db, "eunhye@example.com", "이은혜").UserName!, MembershipStatus.Approved, Roles.TenantAdmin);
        InMemoryDb.SeedMembership(_db, _elem.Id, InMemoryDb.SeedAccount(_db, "minsu@example.com", "박민수").UserName!, MembershipStatus.Approved, Roles.TenantAdmin);
        var t = InMemoryDb.SeedAccount(_db, "jeong@example.com", "정교사");
        InMemoryDb.SeedMembership(_db, _kids.Id, t.UserName!, MembershipStatus.Approved, Roles.Teacher);
        _kidAcc = InMemoryDb.SeedAccount(_db, "cheolsu@example.com", "김철수");
        InMemoryDb.SeedMembership(_db, _kids.Id, _kidAcc.UserName!, MembershipStatus.Approved, Roles.Participant);
        var roster = People(_kids).RosterAsync(null, null).Result;
        _teacherPid = roster.Single(r => r.UserName == "jeong").PersonId;
        _kid = roster.Single(r => r.UserName == "cheolsu").PersonId;
        _plain = People(_kids).AddAsync("eunhye", new PersonInput("박하린", "T1001", 3, 5, "010-1111-2222", null)).Result.Id;
        using (var raw = InMemoryDb.Raw(_db)) { raw.People.Single(p => p.Id == _teacherPid).Phone1 = "010-1234-5678"; raw.SaveChanges(); }
        _c1 = Classes(_kids).CreateAsync("1학년1반").Result.Id;
        Classes(_kids).AddAsync(_c1, [_kid, _plain], teacher: false).Wait();
        Classes(_kids).AddAsync(_c1, [_teacherPid], teacher: true).Wait();
        using (var raw = InMemoryDb.Raw(_db)) { raw.AttendanceChecks.Add(new AttendanceCheck { TenantId = _kids.Id, PersonId = _plain, Sunday = Sun }); raw.SaveChanges(); }
    }

    private (InMemoryDb Db, TenantContext Ctx) In(Tenant t)
    {
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        var ctx = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db)) ctx.Set(raw.Tenants.Single(x => x.Id == t.Id), new DeptRoleUserAssoc { RoleCode = "T1002" }, true);
        return (new InMemoryDb(_db, scope), ctx);
    }

    private PeopleService People(Tenant t) { var (d, c) = In(t); return new PeopleService(d, c, _time); }
    private ClassService Classes(Tenant t) { var (d, c) = In(t); return new ClassService(d, new PeopleService(d, c, _time), c, _time); }
    private ClassMoveService Mover(Tenant t) { var (d, c) = In(t); return new ClassMoveService(d, new InMemorySystemDb(_db), new ClassService(d, new PeopleService(d, c, _time), c, _time), c, _time); }

    [Fact]
    public async Task R238_반_하나를_미배정반으로_옮기면_꼬리표가_남고_반과_담임은_그대로다()
    {
        Assert.Equal((MoveClassError.None, 2), Take(await Mover(_kids).MoveAsync("eunhye", _c1, [_kid, _plain], MoveDest.Unassigned, null, null)));
        var un = await Classes(_kids).UnassignedAsync();
        var note = un.Single(p => p.PersonId == _plain).Note;
        Assert.Equal("이전: jamsil-kids 1학년1반 · 담임 정교사 010-1234-5678".Replace("jamsil-kids", _kids.Name), note);
        Assert.Contains(un, p => p.PersonId == _kid);
        var c = (await Classes(_kids).ClassesAsync()).Single(x => x.Id == _c1);
        Assert.Equal((0, "정교사"), (c.Students, Assert.Single(c.Teachers)));
        Assert.Equal(MoveClassError.Empty, (await Mover(_kids).MoveAsync("eunhye", _c1, [], MoveDest.Unassigned, null, null)).Error);
        var nc = (await Classes(_kids).ClassesAsync()).Single(x => x.IsNewcomer).Id;
        Assert.Equal(MoveClassError.Fixed, (await Mover(_kids).MoveAsync("eunhye", nc, [_kid], MoveDest.Unassigned, null, null)).Error);
    }

    [Fact]
    public async Task R239_타부서로_옮기면_승인_없이_그_부서_미배정반에_들어가고_가입_안_한_아이와_출석_꼬리표가_따라간다()
    {
        Assert.Equal(MoveClassError.BadTarget, (await Mover(_kids).MoveAsync("eunhye", _c1, [_kid, _plain], MoveDest.Dept, null, _lobby.Id)).Error);   // 임시 부서로는 안 된다
        var (e, n, skipped) = await Mover(_kids).MoveAsync("eunhye", _c1, [_kid, _plain], MoveDest.Dept, null, _elem.Id);
        Assert.Equal((MoveClassError.None, 2), (e, n));
        Assert.Empty(skipped);

        using (var raw = InMemoryDb.Raw(_db))
        {
            Assert.Equal(_elem.Id, raw.DeptRoles.Single(d => d.UserId == _kidAcc.Id).TenantId);                // 가입한 아이는 소속째
            Assert.DoesNotContain(raw.People, p => p.TenantId == _kids.Id && (p.Name == "박하린" || p.UserId == _kidAcc.Id));
            var moved = raw.People.Single(p => p.TenantId == _elem.Id && p.Name == "박하린");                  // 가입 안 한 아이는 명부째
            Assert.Equal(("010-1111-2222", "1학년1반", "010-1234-5678"), (moved.Phone1, moved.PrevClassName, moved.PrevTeacherPhone));
            Assert.Equal(_elem.Id, raw.AttendanceChecks.Single(a => a.Sunday == Sun).TenantId);
            Assert.Equal("1학년1반", raw.People.Single(p => p.TenantId == _elem.Id && p.UserId == _kidAcc.Id).PrevClassName);
        }
        var un = await Classes(_elem).UnassignedAsync();
        Assert.Equal(2, un.Count(p => p.Note is not null && p.Note.Contains("1학년1반")));
    }

    private static (MoveClassError, int) Take((MoveClassError E, int N, List<string> S) r) => (r.E, r.N);

    [Fact]
    public async Task 마법사_고른_아이만_다른_반으로_옮기고_꼬리표가_남고_미배정반에서도_반으로_보낸다()
    {
        var c2 = (await Classes(_kids).CreateAsync("2학년1반")).Id;
        Assert.Equal(MoveClassError.SameClass, (await Mover(_kids).MoveAsync("eunhye", _c1, [_plain], MoveDest.Class, _c1, null)).Error);
        Assert.Equal(MoveClassError.NotFound, (await Mover(_kids).MoveAsync("eunhye", c2, [_plain], MoveDest.Class, _c1, null)).Error);   // 그 반에 없는 아이
        Assert.Equal((MoveClassError.None, 1), Take(await Mover(_kids).MoveAsync("eunhye", _c1, [_plain], MoveDest.Class, c2, null)));
        var d2 = await Classes(_kids).DetailAsync(c2);
        Assert.Equal(_plain, Assert.Single(d2!.Students).PersonId);
        Assert.Equal(_kid, Assert.Single((await Classes(_kids).DetailAsync(_c1))!.Students).PersonId);         // 고르지 않은 아이는 남는다
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal("1학년1반", raw.People.Single(p => p.Id == _plain).PrevClassName);

        Assert.Equal((MoveClassError.None, 1), Take(await Mover(_kids).MoveAsync("eunhye", c2, [_plain], MoveDest.Unassigned, null, null)));
        Assert.Equal(MoveClassError.Empty, (await Mover(_kids).MoveAsync("eunhye", null, [], MoveDest.Class, _c1, null)).Error);
        Assert.Equal((MoveClassError.None, 1), Take(await Mover(_kids).MoveAsync("eunhye", null, [_plain], MoveDest.Class, _c1, null)));
        Assert.Equal(2, (await Classes(_kids).DetailAsync(_c1))!.Students.Count);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal("2학년1반", raw.People.Single(p => p.Id == _plain).PrevClassName);   // 미배정반에서 옮기면 꼬리표 그대로
    }

    [Fact]
    public async Task 새친구는_새친구반과_실제_반에_함께_보인다()
    {
        var (d, c) = In(_kids);
        var att = new AttendanceService(d, new ClassService(d, new PeopleService(d, c, _time), c, _time), c, _time);
        var (_, pid) = await att.RegisterAsync("eunhye", new NewcomerInput("오다은", Sun, null, null, null, null, _c1, true, null));
        var list = await Classes(_kids).ClassesAsync();
        Assert.Equal(1, list.Single(x => x.IsNewcomer).Students);
        var ncDetail = await Classes(_kids).DetailAsync(list.Single(x => x.IsNewcomer).Id);
        Assert.Equal(("오다은", "1학년1반"), (Assert.Single(ncDetail!.Students).Name, ncDetail.Students[0].Note));
        Assert.True((await Classes(_kids).DetailAsync(_c1))!.Students.Single(s => s.PersonId == pid).Newcomer);
    }
}
