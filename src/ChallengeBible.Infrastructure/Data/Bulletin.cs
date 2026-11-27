using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>
/// 부서 주보 한 건(ADR 0117, 2026-11-20 사용자): 날짜(부서 날짜) · 제목 · 내용 + 파일 여러 개. 부서마다 완전히 따로(RLS + 전역 필터).
/// 누구나 보고, 그 부서 교사 · 관리자가 올리고 고친다.
/// </summary>
public sealed class Bulletin : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    /// <summary>주보 날짜(부서 날짜, 보통 주일).</summary>
    public DateOnly Date { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public DateTime CreatedUtc { get; set; }
    public string? CreatedByUserName { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public string? UpdatedByUserName { get; set; }
}

/// <summary>주보에 붙은 파일 하나(사진 · PDF). 파일은 부서 폴더(Bulletins:Dir/{TenantId}/{StoredName})에 있고 이 줄로만 연다.</summary>
public sealed class BulletinFile : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public int BulletinId { get; set; }
    /// <summary>올린 사람이 고른 파일 이름(보이기만).</summary>
    public required string FileName { get; set; }
    /// <summary>저장한 이름(GUID + 확장자) — 사람이 고른 이름은 경로에 쓰지 않는다.</summary>
    public required string StoredName { get; set; }
    public required string ContentType { get; set; }
    public long Size { get; set; }
    public int SortOrder { get; set; }
}

internal sealed class BulletinConfig : IEntityTypeConfiguration<Bulletin>
{
    public void Configure(EntityTypeBuilder<Bulletin> e)
    {
        e.ToTable("Bulletin");
        e.HasKey(x => new { x.TenantId, x.Id });
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.Title).HasMaxLength(100);
        e.Property(x => x.Body).HasMaxLength(4000);
        e.Property(x => x.CreatedUtc).HasPrecision(3);
        e.Property(x => x.UpdatedUtc).HasPrecision(3);
        e.Property(x => x.CreatedByUserName).HasMaxLength(256);
        e.Property(x => x.UpdatedByUserName).HasMaxLength(256);
        e.HasIndex(x => new { x.TenantId, x.Date });
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class BulletinFileConfig : IEntityTypeConfiguration<BulletinFile>
{
    public void Configure(EntityTypeBuilder<BulletinFile> e)
    {
        e.ToTable("BulletinFile");
        e.HasKey(x => new { x.TenantId, x.Id });
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.FileName).HasMaxLength(200);
        e.Property(x => x.StoredName).HasMaxLength(60).IsUnicode(false);
        e.Property(x => x.ContentType).HasMaxLength(50).IsUnicode(false);
        e.HasIndex(x => new { x.TenantId, x.BulletinId });
        e.HasOne<Bulletin>().WithMany().HasForeignKey(x => new { x.TenantId, x.BulletinId }).OnDelete(DeleteBehavior.NoAction);
    }
}
