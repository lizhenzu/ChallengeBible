using ChallengeBible.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>일반 요청용 DbContext 를 만든다. 서비스는 이것만 받는다.</summary>
public interface IAppDb
{
    /// <summary>지금 RequestScope 의 영역 · 계정으로 연결한다. 영역을 정한 뒤에 새로 만들어야 그 영역이 적용된다.</summary>
    AppDbContext Create();
}

/// <summary>시스템 연결(PlatformScope). 물리 설계 3-3 의 처리기만 받는다.</summary>
public interface ISystemDb
{
    AppDbContext Create(int actorAccountId);
}

/// <summary>일반 요청용: 이 요청의 영역 · 계정을 세션 값과 전역 필터에 쓴다.</summary>
public sealed class RequestDbContextFactory(string connectionString, RequestScope scope) : IAppDb
{
    public AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .AddInterceptors(new SessionContextInterceptor(() => new SessionValues(scope.TenantId, scope.AccountId, PlatformScope: false)))
            .Options;
        return new AppDbContext(options, scope.TenantId);
    }
}

/// <summary>
/// 시스템 연결(PlatformScope = 1): 영역을 넘는 처리기만 쓴다(물리 설계 3-3 — 진급 승인, 읽음 초기화,
/// 비밀번호 초기화, 플랫폼 관리, 아웃박스). 전역 필터를 끈다. 처리기는 시작 전에 권한을 검사하고 감사 로그를 남긴다.
/// </summary>
public sealed class SystemDbContextFactory(string connectionString) : ISystemDb
{
    public AppDbContext Create(int actorAccountId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .AddInterceptors(new SessionContextInterceptor(() => new SessionValues(null, actorAccountId, PlatformScope: true)))
            .Options;
        return new AppDbContext(options, tenantId: null) { FilterTenants = false };
    }
}

/// <summary>`dotnet ef` 도구용. 마이그레이션을 만들 때는 DB 에 연결하지 않는다.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("CB_DESIGN_CONNECTION") ?? "Server=.;Database=ChallengeBibleDesign;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options;
        return new AppDbContext(options, tenantId: null);
    }
}
