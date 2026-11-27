using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>앨범 공개 범위(ADR 0118): Public 누구나 · Login 로그인한 사람 모두(기본) · Dept 그 부서 사람만 · Admin 그 부서 관리자만.</summary>
public static class AlbumVisibility
{
    public const string Public = "Public", Login = "Login", Dept = "Dept", Admin = "Admin";
    public static readonly string[] All = [Public, Login, Dept, Admin];
}

/// <summary>
/// 부서 사진첩 앨범(ADR 0118, 2026-11-20 사용자): 제목 · 설명 · 촬영일 · 대표 사진 · 공개 범위 · 내려받기 허용. 부서마다 완전히 따로(RLS + 전역 필터).
/// 만들기 · 고치기 · 지우기는 그 부서 관리자, 사진 올리기는 교사도.
/// </summary>
public sealed class Album : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DateOnly? EventDate { get; set; }
    public int? CoverPhotoId { get; set; }
    public string Visibility { get; set; } = AlbumVisibility.Login;
    public bool AllowDownload { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public string? CreatedByUserName { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public string? UpdatedByUserName { get; set; }
}

/// <summary>앨범 사진 한 장. 원본 · 썸네일은 부서 폴더(Albums:Dir/{TenantId}/)에 GUID 이름으로, 이 줄로만 연다.</summary>
public sealed class Photo : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public int AlbumId { get; set; }
    public required string FileName { get; set; }
    public required string StoredName { get; set; }
    public required string ThumbName { get; set; }
    public required string ContentType { get; set; }
    public long Size { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>같은 앨범에 같은 사진을 두 번 올리지 않게(내용 SHA-256, 16진 64자).</summary>
    public required string Hash { get; set; }
    public int SortOrder { get; set; }
    public string? UploadedByUserName { get; set; }
    public DateTime UploadedUtc { get; set; }
}

internal sealed class AlbumConfig : IEntityTypeConfiguration<Album>
{
    public void Configure(EntityTypeBuilder<Album> e)
    {
        e.ToTable("Album", t => t.HasCheckConstraint("CK_Album_Visibility", "[Visibility] IN ('Public', 'Login', 'Dept', 'Admin')"));
        e.HasKey(x => new { x.TenantId, x.Id });
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.Title).HasMaxLength(100);
        e.Property(x => x.Description).HasMaxLength(2000);
        e.Property(x => x.Visibility).HasMaxLength(10).IsUnicode(false);
        e.Property(x => x.CreatedUtc).HasPrecision(3);
        e.Property(x => x.UpdatedUtc).HasPrecision(3);
        e.Property(x => x.CreatedByUserName).HasMaxLength(256);
        e.Property(x => x.UpdatedByUserName).HasMaxLength(256);
        e.HasIndex(x => new { x.TenantId, x.EventDate });
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class PhotoConfig : IEntityTypeConfiguration<Photo>
{
    public void Configure(EntityTypeBuilder<Photo> e)
    {
        e.ToTable("Photo");
        e.HasKey(x => new { x.TenantId, x.Id });
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.FileName).HasMaxLength(200);
        e.Property(x => x.StoredName).HasMaxLength(60).IsUnicode(false);
        e.Property(x => x.ThumbName).HasMaxLength(60).IsUnicode(false);
        e.Property(x => x.ContentType).HasMaxLength(30).IsUnicode(false);
        e.Property(x => x.Hash).HasMaxLength(64).IsUnicode(false);
        e.Property(x => x.UploadedByUserName).HasMaxLength(256);
        e.Property(x => x.UploadedUtc).HasPrecision(3);
        e.HasIndex(x => new { x.TenantId, x.AlbumId, x.Hash });
        e.HasOne<Album>().WithMany().HasForeignKey(x => new { x.TenantId, x.AlbumId }).OnDelete(DeleteBehavior.NoAction);
    }
}
