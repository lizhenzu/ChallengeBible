using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Tests.Infrastructure;

/// <summary>
/// DB 없이 확인하는 격리 테스트: 만들어지는 SQL 에 영역 조건이 붙는지(0020 앱 쪽 필터).
/// DB 의 RLS 자체는 통합 테스트(접속 문자열이 있을 때)에서 따로 확인한다.
/// </summary>
public class TenantIsolationModelTests
{
    private const string Cs = "Server=.;Database=Unused;Trusted_Connection=True;TrustServerCertificate=True";

    [Fact]
    public void 일반_요청은_영역_조건이_붙는다()
    {
        var scope = new RequestScope();
        scope.SetTenant(7);
        using var db = new RequestDbContextFactory(Cs, scope).Create();

        var sql = db.Memberships.Where(m => m.Status == MembershipStatus.Approved).ToQueryString();

        Assert.Contains("[m].[TenantId] = @", sql);
        Assert.Equal(7, db.CurrentTenantId);
    }

    [Fact]
    public void 영역이_없는_요청은_영역_데이터를_못_본다()
    {
        using var db = new RequestDbContextFactory(Cs, new RequestScope()).Create();
        var sql = db.MembershipRoles.ToQueryString();
        // TenantId = NULL 비교는 늘 거짓 → 아무 행도 안 나온다
        Assert.Contains("TenantId", sql);
        Assert.Null(db.CurrentTenantId);
    }

    [Fact]
    public void 시스템_연결은_전역_필터를_끈다()
    {
        using var db = new SystemDbContextFactory(Cs).Create(actorAccountId: 1);
        var sql = db.Memberships.ToQueryString();
        Assert.DoesNotContain("[m].[TenantId] = @", sql);
    }

    [Fact]
    public void 플랫폼_표에는_필터가_없다()
    {
        var scope = new RequestScope();
        scope.SetTenant(7);
        using var db = new RequestDbContextFactory(Cs, scope).Create();
        Assert.DoesNotContain("WHERE", db.Tenants.ToQueryString());
    }

    [Fact]
    public void 한_요청의_영역은_바꿀_수_없다()
    {
        var scope = new RequestScope();
        scope.SetTenant(7);
        scope.SetTenant(7);
        Assert.Throws<InvalidOperationException>(() => scope.SetTenant(8));
    }
}
