using ChallengeBible.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

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
        return new AppDbContext(options, scope.TenantId) { CurrentAccountId = scope.AccountId };
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

/// <summary>
/// `dotnet ef` 도구용. 마이그레이션을 만들 때는 DB 에 연결하지 않는다.
/// `database update` 는 웹 프로젝트의 user-secrets(ConnectionStrings:App)를 읽는다 — 접속 문자열을 명령줄에 쓰지 않기 위해.
/// </summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <summary>ChallengeBible.Web.csproj 의 UserSecretsId 와 같아야 한다.</summary>
    public const string WebUserSecretsId = "c87ac3b6-7be9-403a-8e2d-324d539b077b";

    public AppDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets(WebUserSecretsId)
            .AddEnvironmentVariables()
            .Build();
        var cs = config.GetConnectionString("App")
                 ?? "Server=.;Database=ChallengeBibleDesign;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options;
        return new AppDbContext(options, tenantId: null);
    }
}
