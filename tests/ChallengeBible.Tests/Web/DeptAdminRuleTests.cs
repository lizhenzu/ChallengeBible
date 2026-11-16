using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;

namespace ChallengeBible.Tests.Web;

/// <summary>R-313 부서 관리자 규칙(확정 시안 관리자규칙): 체크한 직분은 바로 부서 관리자, 풀면 규칙으로 받은 사람만 빠진다. 따로 정한 관리자 · 계정은 그대로.</summary>
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

    private (int Tenant, string Boss) Seed()
    {
        var t = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM").Id;
        var boss = InMemoryDb.SeedAccount(_db, "boss@example.com", "운영자");
        InMemoryDb.MakePlatformAdmin(_db, boss);
        return (t, "boss@example.com");
    }

    private string BossName()
    {
        using var raw = InMemoryDb.Raw(_db);
        return raw.Users.Single(u => u.Email == "boss@example.com").UserName!;
    }

    [Fact]
    public async Task R313_기본값은_부장_부감_서기이고_저장하면_바로_관리자가_된다()
    {
        var (t, _) = Seed();
        var boo = Person(t, "boo", "T1005");
        var clk = Person(t, "clk", "T1008");
        var dir = Person(t, "dir", "T1004");
        var tch = Person(t, "tch", "T1002");
        var view = await Svc.ViewAsync(BossName());
        Assert.Equal(["T1005", "T1006", "T1008"], view.Rows.Where(r => r.On).Select(r => r.Code));

        var (granted, revoked) = await Svc.SaveAsync(BossName(), DeptAdminRuleService.Default);
        Assert.Equal((2, 0), (granted, revoked));
        Assert.True(IsAdmin(boo));
        Assert.True(IsAdmin(clk));
        Assert.False(IsAdmin(dir));
        Assert.False(IsAdmin(tch));
        Assert.Equal(2, (await Svc.ViewAsync(BossName())).ByRule);
    }

    [Fact]
    public async Task R313_체크를_풀면_규칙으로_받은_사람만_빠지고_따로_정한_관리자와_계정은_남는다()
    {
        var (t, _) = Seed();
        var boo = Person(t, "boo", "T1005");
        var manual = Person(t, "manual", "T1006");
        InMemoryDb.AddRole(_db, manual, IdentityRoles.Admin);   // 따로 정한 관리자(부감)
        await Svc.SaveAsync(BossName(), DeptAdminRuleService.Default);
        Assert.True(IsAdmin(boo));

        var (_, revoked) = await Svc.SaveAsync(BossName(), []);
        Assert.Equal(1, revoked);
        Assert.False(IsAdmin(boo));
        Assert.True(IsAdmin(manual));
        using var raw = InMemoryDb.Raw(_db);
        Assert.Contains(raw.Users, u => u.Id == boo);   // 계정은 지우지 않는다
        Assert.Empty(raw.MemberTags.Where(m => m.Tag == DeptAdminRuleService.AutoTag));
    }

    [Fact]
    public async Task R313_직분이_바뀌면_따라간다()
    {
        var (t, _) = Seed();
        var u = Person(t, "kim", "T1002");
        await Svc.SaveAsync(BossName(), ["T1005"]);
        Assert.False(IsAdmin(u));
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.DeptRoles.Single(d => d.UserId == u).RoleCode = "T1005";
            raw.SaveChanges();
        }
        using (var sys = new InMemorySystemDb(_db).Create("x")) await DeptAdminRuleService.SyncAsync(sys, [u], default);
        Assert.True(IsAdmin(u));
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.DeptRoles.Single(d => d.UserId == u).RoleCode = "T1002";
            raw.SaveChanges();
        }
        using (var sys = new InMemorySystemDb(_db).Create("x")) await DeptAdminRuleService.SyncAsync(sys, [u], default);
        Assert.False(IsAdmin(u));
        using (var sys = new InMemorySystemDb(_db).Create("x"))
            Assert.False(await DeptAdminRuleService.ByRuleAsync(sys, u, default));
    }

    [Fact]
    public async Task R313_플랫폼_관리자만_저장할_수_있다()
    {
        var (t, _) = Seed();
        Person(t, "boo", "T1005");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Svc.SaveAsync("boo", DeptAdminRuleService.Default));
    }
}
