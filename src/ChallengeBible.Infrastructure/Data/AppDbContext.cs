using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>
/// 일반 요청용 DbContext. 영역 범위 표에는 전역 필터(TenantId = 이 요청의 영역)를 건다.
/// DB 의 RLS 와 같은 조건을 앱에서 한 번 더 거는 것이다(0020 이중 격리).
/// 영역을 넘는 일은 <see cref="SystemDbContextFactory"/> 로만 한다(물리 설계 3-3).
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options, int? tenantId) : DbContext(options)
{
    /// <summary>필터 기준 영역. 시스템 연결에서는 null 이고 필터를 끈다.</summary>
    public int? CurrentTenantId { get; } = tenantId;
    internal bool FilterTenants { get; init; } = true;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<MembershipRole> MembershipRoles => Set<MembershipRole>();
    public DbSet<TenantSeason> TenantSeasons => Set<TenantSeason>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("app");
        b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var type in b.Model.GetEntityTypes().Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType)))
        {
            var method = typeof(AppDbContext).GetMethod(nameof(TenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(type.ClrType);
            b.Entity(type.ClrType).HasQueryFilter((LambdaExpression)method.Invoke(this, null)!);
        }
    }

    private Expression<Func<T, bool>> TenantFilter<T>() where T : ITenantOwned =>
        e => !FilterTenants || e.TenantId == CurrentTenantId;
}
