using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>
/// 진급 요청(0026, 0030, 0058): 두 영역에 걸친 플랫폼 범위 기록. 보낸 영역(Source) · 받는 영역(Target) 관리자만 본다
/// (전역 필터 + RLS sec.fn_Promotion). 받는 영역의 소속은 승인할 때 만든다(0058).
/// </summary>
public sealed class PromotionRequest
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public int SourceTenantId { get; set; }
    public int TargetTenantId { get; set; }
    public string Status { get; set; } = PromotionStatus.Pending;
    public DateTime RequestedUtc { get; set; }
    public int RequestedByAccountId { get; set; }
    public DateTime? DecidedUtc { get; set; }
    public int? DecidedByAccountId { get; set; }
}

public static class PromotionStatus
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
    public static readonly string[] All = [Pending, Approved, Rejected, Cancelled];
}

/// <summary>시스템 연결로 한 일의 기록(물리 설계 3-3). 플랫폼 표라 RLS 를 걸지 않는다. 추가만 한다.</summary>
public sealed class AuditLog
{
    public long Id { get; set; }
    public DateTime OccurredUtc { get; set; }
    public int ActorAccountId { get; set; }
    public required string Action { get; set; }
    public int? TenantId { get; set; }
    public string? Detail { get; set; }
}

internal sealed class PromotionRequestConfig : IEntityTypeConfiguration<PromotionRequest>
{
    public void Configure(EntityTypeBuilder<PromotionRequest> e)
    {
        e.ToTable("PromotionRequest", t =>
        {
            t.HasCheckConstraint("CK_PromotionRequest_Status", $"Status IN ({string.Join(",", PromotionStatus.All.Select(s => $"'{s}'"))})");
            t.HasCheckConstraint("CK_PromotionRequest_Tenants", "SourceTenantId <> TargetTenantId");
        });
        e.HasKey(x => x.Id).HasName("PK_PromotionRequest");
        e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
        e.Property(x => x.RequestedUtc).HasColumnType("datetime2(3)");
        e.Property(x => x.DecidedUtc).HasColumnType("datetime2(3)");
        // 같은 계정 · 같은 보낸 영역에 진행 중 요청은 1건(0030, 물리 설계 2절)
        e.HasIndex(x => new { x.AccountId, x.SourceTenantId }).IsUnique().HasFilter("[Status] = 'Pending'").HasDatabaseName("UQ_PromotionRequest_Pending");
        e.HasIndex(x => new { x.SourceTenantId, x.Status }).HasDatabaseName("IX_PromotionRequest_Source");
        e.HasIndex(x => new { x.TargetTenantId, x.Status }).HasDatabaseName("IX_PromotionRequest_Target");
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).HasConstraintName("FK_PromotionRequest_Account").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.SourceTenantId).HasConstraintName("FK_PromotionRequest_Source").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TargetTenantId).HasConstraintName("FK_PromotionRequest_Target").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.RequestedByAccountId).HasConstraintName("FK_PromotionRequest_RequestedBy").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.DecidedByAccountId).HasConstraintName("FK_PromotionRequest_DecidedBy").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AuditLogConfig : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> e)
    {
        e.ToTable("AuditLog");
        e.HasKey(x => x.Id).HasName("PK_AuditLog");
        e.Property(x => x.OccurredUtc).HasColumnType("datetime2(3)");
        e.Property(x => x.Action).HasMaxLength(40).IsUnicode(false);
        e.Property(x => x.Detail).HasMaxLength(400);
        e.HasIndex(x => x.OccurredUtc).HasDatabaseName("IX_AuditLog_Occurred");
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorAccountId).HasConstraintName("FK_AuditLog_Actor").OnDelete(DeleteBehavior.NoAction);
    }
}
