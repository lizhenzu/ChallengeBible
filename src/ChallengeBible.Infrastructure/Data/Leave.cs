using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>
/// 탈퇴 신청(ADR 0119, R-342): 본인이 내고, 그 부서 관리자나 플랫폼 관리자가 승인하면 계정 · 기록이 모두 지워진다.
/// 기다리는 신청만 둔다 — 취소 · 반려 · 승인하면 이 줄도 없어진다. 부서마다 따로(RLS + 전역 필터).
/// </summary>
public sealed class LeaveRequest : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string UserName { get; set; }
    public required string FullName { get; set; }
    public string? RoleCode { get; set; }
    public DateTime RequestedUtc { get; set; }
}

internal sealed class LeaveRequestConfig : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> e)
    {
        e.ToTable("LeaveRequest");
        e.HasKey(x => new { x.TenantId, x.Id });
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.UserId).HasMaxLength(450);
        e.Property(x => x.UserName).HasMaxLength(256);
        e.Property(x => x.FullName).HasMaxLength(100);
        e.Property(x => x.RoleCode).HasMaxLength(20).IsUnicode(false);
        e.Property(x => x.RequestedUtc).HasPrecision(3);
        e.HasIndex(x => x.UserName).IsUnique();
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
    }
}
