using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>
/// 플랫폼 기본 규칙 버전(물리 설계 PlatformRuleSetVersion + ADR 0060 적용 시작일). 값은 RuleSet 전체 JSON.
/// 어떤 날의 규칙 = 적용 시작일이 그날 이전인 버전 가운데 번호가 가장 큰 것(나중 결정이 이긴다). 행이 없으면 코드 기본값(v1).
/// 플랫폼 표라 TenantId · RLS 가 없다. 쓰기는 플랫폼 관리자 화면(시스템 연결)에서만.
/// </summary>
public sealed class PlatformRuleSetVersion
{
    public int Id { get; set; }
    public int VersionNo { get; set; }
    public required string Values { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateTime ChangedUtc { get; set; }
    public int ChangedByAccountId { get; set; }
}

internal sealed class PlatformRuleSetVersionConfig : IEntityTypeConfiguration<PlatformRuleSetVersion>
{
    public void Configure(EntityTypeBuilder<PlatformRuleSetVersion> e)
    {
        e.ToTable("PlatformRuleSetVersion", t => t.HasCheckConstraint("CK_PlatformRuleSetVersion_Values", "ISJSON([Values]) = 1"));
        e.HasKey(x => x.Id).HasName("PK_PlatformRuleSetVersion");
        e.HasIndex(x => x.VersionNo).IsUnique().HasDatabaseName("UQ_PlatformRuleSetVersion_No");
        e.Property(x => x.Values).HasColumnName("Values");
        e.Property(x => x.EffectiveFrom).HasColumnType("date");
        e.Property(x => x.ChangedUtc).HasColumnType("datetime2(3)");
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.ChangedByAccountId).HasConstraintName("FK_PlatformRuleSetVersion_Account").OnDelete(DeleteBehavior.NoAction);
    }
}
