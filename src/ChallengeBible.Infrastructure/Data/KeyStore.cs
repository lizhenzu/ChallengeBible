using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>
/// 로그인 쿠키 · 위조 방지 토큰을 푸는 Data Protection 키(ADR 0055). 영역 데이터가 아니다 — TenantId · RLS 없음.
/// 표는 AppDbContext 마이그레이션이 만들고, 읽고 쓰는 것은 웹의 KeyStoreDbContext 만 한다.
/// </summary>
public sealed class DataProtectionKeyConfiguration : IEntityTypeConfiguration<DataProtectionKey>
{
    public void Configure(EntityTypeBuilder<DataProtectionKey> e)
    {
        e.ToTable("DataProtectionKey", "app");
        e.HasKey(x => x.Id);
        e.Property(x => x.FriendlyName).HasMaxLength(100);
    }
}
