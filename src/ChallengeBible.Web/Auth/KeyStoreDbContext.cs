using ChallengeBible.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Auth;

/// <summary>
/// Data Protection 키 저장 전용 연결(ADR 0055). <c>PersistKeysToDbContext</c> 가 DI 로 받는다.
/// 웹 프로젝트에 두어 Infrastructure 의 `dotnet ef` 명령이 AppDbContext 하나만 보게 한다.
/// </summary>
public sealed class KeyStoreDbContext(DbContextOptions<KeyStoreDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b) =>
        new DataProtectionKeyConfiguration().Configure(b.Entity<DataProtectionKey>());
}
