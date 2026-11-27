using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;

namespace ChallengeBible.Tests.Web;

/// <summary>예배 안내(ADR 0116, R-337): DB 에 두고 부서 관리자가 언제든 고친다. 부서마다 따로(격리), 바꾼 기록이 남는다.</summary>
public class WorshipTests
{
    private readonly string _db = InMemoryDb.NewName();
    private readonly Tenant _kids, _elem;

    public WorshipTests()
    {
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _elem = InMemoryDb.SeedTenant(_db, "jamsil-elem", "JSELEM");
    }

    private WorshipService In(Tenant t, bool admin = true)
    {
        var scope = new RequestScope();
        scope.SetTenant(t.Id);
        var tc = new TenantContext();
        using (var raw = InMemoryDb.Raw(_db))
            tc.Set(raw.Tenants.Single(x => x.Id == t.Id), new DeptRoleUserAssoc { TenantId = t.Id, Id = 1, UserId = "u", RoleCode = "T1002" }, admin);
        return new WorshipService(new InMemoryDb(_db, scope), tc, FixedTime.SeoulMorning());
    }

    [Fact]
    public async Task R337_예배_시간과_장소는_부서마다_따로_두고_언제든_바꾼다()
    {
        var (id, e) = await In(_kids).SaveAsync("eunhye", new WorshipInput(null, null, 0, "10:00", "비전센터 믿음홀(3F)", "서울특별시 송파구 석촌호수로 102", null, null));
        Assert.True(e.Ok);
        var row = Assert.Single(await In(_kids).ListAsync());
        Assert.Equal(("비전센터 믿음홀(3F)", new TimeOnly(10, 0), (byte)0), (row.Place, row.StartTime, row.DayOfWeek));
        Assert.Equal("매주 주일 오전 10시", WorshipService.When(row.DayOfWeek, row.StartTime));
        Assert.Empty(await In(_elem).ListAsync());                                   // 다른 부서에는 안 보인다

        // 장소 · 시간이 바뀌면 같은 줄을 고친다
        await In(_kids).SaveAsync("eunhye", new WorshipInput(id, null, 0, "11:30", "비전센터 소망홀(4F)", null, "https://www.saenae.org/bulletin", "2026-11-15"));
        row = Assert.Single(await In(_kids).ListAsync());
        Assert.Equal(("비전센터 소망홀(4F)", new TimeOnly(11, 30), "https://www.saenae.org/bulletin", new DateOnly(2026, 11, 15)), (row.Place, row.StartTime, row.BulletinUrl, row.BulletinDate));
        Assert.Equal("매주 주일 오전 11시 30분", WorshipService.When(row.DayOfWeek, row.StartTime));

        // 숨기기 · 지우기
        await In(_kids).SaveAsync("eunhye", new WorshipInput(id, null, 0, "11:30", "비전센터 소망홀(4F)", null, null, null, IsActive: false));
        Assert.Empty(await In(_kids).ListAsync());
        Assert.Single(await In(_kids).ListAsync(includeHidden: true));
        Assert.True(await In(_kids).DeleteAsync("eunhye", id!.Value));
        Assert.Empty(await In(_kids).ListAsync(includeHidden: true));

        using var raw = InMemoryDb.Raw(_db);
        var logs = raw.AuditLogs.Where(l => l.Action == WorshipService.LogAction).ToList();
        Assert.Equal(4, logs.Count);                                                   // 넣기 · 고치기 · 숨기기 · 지우기
        Assert.All(logs, l => Assert.Equal(_kids.Id, l.TenantId));
        Assert.Contains(logs, l => l.Detail!.Contains("믿음홀") && l.Detail.Contains("소망홀"));
    }

    [Fact]
    public async Task 틀린_값은_칸별로_알려주고_관리자만_고친다()
    {
        var (id, e) = await In(_kids).SaveAsync("eunhye", new WorshipInput(null, null, 9, "열시", " ", null, "javascript:alert(1)", "11/15"));
        Assert.Null(id);
        Assert.Equal(["BulletinDate", "BulletinUrl", "DayOfWeek", "Place", "StartTime"], e.Keys.Order().ToArray());
        Assert.Empty(await In(_kids).ListAsync(includeHidden: true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => In(_kids, admin: false).SaveAsync("cheolsu", new WorshipInput(null, null, 0, "10:00", "본당", null, null, null)));
    }
}
