using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;

namespace ChallengeBible.Tests.Web;

/// <summary>
/// R-313 부서 관리자 규칙(ADR 0101): 권한은 역할 · 업무에 붙는다. 체크한 역할이거나 체크한 업무를 맡으면 바로 부서 관리자, 풀면 규칙으로 받은 사람만 빠진다.
/// 어드민 역할은 늘 관리자. 사람마다 관리자를 켜는 기능은 없다. 따로 정한 관리자는 어드민 역할로 옮기고, 계정은 그대로.
/// </summary>
public class DeptAdminRuleTests
{
    private readonly string _db = InMemoryDb.NewName();
    private DeptAdminRuleService Svc => new(new InMemorySystemDb(_db), TimeProvider.System);

    private string Person(int tenant, string userName, string code)
    {
        var u = InMemoryDb.SeedAccount(_db, userName + "@example.com", userName);
        using var raw = InMemoryDb.Raw(_db);
        var row = raw.Users.Single(x => x.Id == u.Id);
        (row.UserName, row.NormalizedUserName) = (userName, AppUser.Normalize(userName));
        raw.DeptRoles.Add(new DeptRoleUserAssoc { TenantId = tenant, UserId = u.Id, RoleCode = code });
        raw.SaveChanges();
        return u.Id;
    }

    private bool IsAdmin(string userId)
    {
        using var raw = InMemoryDb.Raw(_db);
        var role = raw.IdentityRoleRows.FirstOrDefault(r => r.Name == IdentityRoles.Admin);
        return role is not null && raw.UserRoles.Any(r => r.UserId == userId && r.RoleId == role.Id);
    }

    private int Seed()
    {
        var t = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM").Id;
        var boss = InMemoryDb.SeedAccount(_db, "boss@example.com", "운영자");
        InMemoryDb.MakePlatformAdmin(_db, boss);
        using var raw = InMemoryDb.Raw(_db);
        if (!raw.Duties.Any(d => d.Key == "Gift")) raw.Duties.Add(new Duty { Key = "Gift", Name = "선물 담당", IsAdmin = true, SortOrder = 1 });
        raw.SaveChanges();
        return t;
    }

    private string Boss()
    {
        using var raw = InMemoryDb.Raw(_db);
        return raw.Users.Single(u => u.Email == "boss@example.com").UserName!;
    }

    private void Tag(int t, string uid, string key)
    {
        using var raw = InMemoryDb.Raw(_db);
        raw.MemberTags.Add(new MemberTag { TenantId = t, UserId = uid, Tag = key });
        raw.SaveChanges();
    }

    [Fact]
    public async Task R313_기본값은_지도_부장_부감_서기_어드민이고_저장하면_바로_관리자가_된다()
    {
        var t = Seed();
        var boo = Person(t, "boo", "T1005");
        var dir = Person(t, "dir", "T1004");
        var acc = Person(t, "acc", "T1009");
        var tch = Person(t, "tch", "T1002");
        var view = await Svc.ViewAsync(Boss());
        Assert.Equal(["T1030", "T1004", "T1005", "T1006", "T1008"], view.Roles.Where(r => r.On).Select(r => r.Code));
        Assert.True(view.Roles.Single(r => r.Code == "T1030").Fixed);
        Assert.Contains(view.Duties, d => d.Name == "선물 담당" && d.On);

        var (granted, revoked, _) = await Svc.SaveAsync(Boss(), DeptAdminRuleService.Default, ["Gift"]);
        Assert.Equal((2, 0), (granted, revoked));
        Assert.True(IsAdmin(boo));
        Assert.True(IsAdmin(dir));
        Assert.False(IsAdmin(acc));
        Assert.False(IsAdmin(tch));
    }

    [Fact]
    public async Task R313_교사도_관리자_업무를_맡으면_관리자가_되고_업무를_빼면_빠진다()
    {
        var t = Seed();
        var kim = Person(t, "kim", "T1002");
        await Svc.SaveAsync(Boss(), DeptAdminRuleService.Default, ["Gift"]);
        Assert.False(IsAdmin(kim));
        Tag(t, kim, "Gift");   // 교사 + 선물 담당
        using (var sys = new InMemorySystemDb(_db).Create("x")) await DeptAdminRuleService.SyncAsync(sys, [kim], default);
        Assert.True(IsAdmin(kim));
        using (var sys = new InMemorySystemDb(_db).Create("x")) Assert.Equal("업무 \"선물 담당\"", await DeptAdminRuleService.ReasonAsync(sys, t, kim, default));

        await Svc.AddDutyAsync(Boss(), "찬양 담당");
        string praise;
        using (var raw = InMemoryDb.Raw(_db)) praise = raw.Duties.Single(d => d.Name == "찬양 담당").Key;
        var lee = Person(t, "lee", "T1002");
        Tag(t, lee, praise);   // 찬양 담당은 관리자 업무가 아니다
        using (var sys = new InMemorySystemDb(_db).Create("x")) await DeptAdminRuleService.SyncAsync(sys, [lee], default);
        Assert.False(IsAdmin(lee));

        await Svc.SaveAsync(Boss(), DeptAdminRuleService.Default, []);   // 선물 담당 체크 풀기
        Assert.False(IsAdmin(kim));
    }

    [Fact]
    public async Task R313_따로_정한_관리자는_어드민_역할로_옮기고_규칙을_바꿔도_빠지지_않는다()
    {
        var t = Seed();
        var boo = Person(t, "boo", "T1005");
        var manual = Person(t, "manual", "T1002");
        InMemoryDb.AddRole(_db, manual, IdentityRoles.Admin);   // 예전에 사람마다 정한 관리자(관리초 등)
        var (_, _, moved) = await Svc.SaveAsync(Boss(), DeptAdminRuleService.Default, ["Gift"]);
        Assert.Equal(1, moved);
        using (var raw = InMemoryDb.Raw(_db)) Assert.Equal(UserRole.AdminCode, raw.DeptRoles.Single(d => d.UserId == manual).RoleCode);
        Assert.True(IsAdmin(boo));

        var (_, revoked, _) = await Svc.SaveAsync(Boss(), [], []);   // 역할 · 업무 모두 풀어도 어드민은 남는다
        Assert.Equal(1, revoked);
        Assert.False(IsAdmin(boo));
        Assert.True(IsAdmin(manual));
        using var after = InMemoryDb.Raw(_db);
        Assert.Contains(after.Users, u => u.Id == boo);   // 계정은 지우지 않는다
    }

    [Fact]
    public async Task R313_역할이_바뀌면_따라가고_사람마다_관리자를_켤_수는_없다()
    {
        var t = Seed();
        var u = Person(t, "kim", "T1002");
        await Svc.SaveAsync(Boss(), ["T1005"], []);
        using (var raw = InMemoryDb.Raw(_db)) { raw.DeptRoles.Single(d => d.UserId == u).RoleCode = "T1005"; raw.SaveChanges(); }
        using (var sys = new InMemorySystemDb(_db).Create("x")) await DeptAdminRuleService.SyncAsync(sys, [u], default);
        Assert.True(IsAdmin(u));
        using (var raw = InMemoryDb.Raw(_db)) { raw.DeptRoles.Single(d => d.UserId == u).RoleCode = "T1002"; raw.SaveChanges(); }
        using (var sys = new InMemorySystemDb(_db).Create("x")) await DeptAdminRuleService.SyncAsync(sys, [u], default);
        Assert.False(IsAdmin(u));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Svc.SaveAsync("kim", DeptAdminRuleService.Default, []));
    }
}
