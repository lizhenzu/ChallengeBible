using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

// 온가족(0065, R-175 ~ R-178): 부서를 넘어 계정끼리 묶은 한 집안. 가족팀(Family.cs)과 별개 — 시상 없이 총점 보기만.
// TenantId 가 없는 플랫폼 표(RLS 없음, 앱 정책). 물리 설계 참조 DDL 의 app.Family ~ FamilyRequest 와 이름 · 형식을 맞춘다.

/// <summary>온가족. 이름이 비면 "{대표 구성원} 온가족". 묶기 · 빼기 · 해체는 플랫폼 관리자만(R-175).</summary>
public sealed class Family
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public DateTime CreatedUtc { get; set; }
    public int CreatedByAccountId { get; set; }
    public List<FamilyLink> Links { get; set; } = [];
}

/// <summary>온가족 구성원: 계정. 한 계정은 한 온가족에만(R-175, AccountId 고유). 빼면 행을 지운다(이력은 AuditLog).</summary>
public sealed class FamilyLink
{
    public int FamilyId { get; set; }
    public int AccountId { get; set; }
    public string Relation { get; set; } = FamilyRelation.Student;
    public byte SortOrder { get; set; }
    public DateTime AddedUtc { get; set; }
    public int AddedByAccountId { get; set; }
}

/// <summary>온가족 신청(R-176): 부모가 자유 글로 부탁하고 플랫폼 관리자가 확인해 묶는다. 처리 안 된 신청은 계정당 1건.</summary>
public sealed class FamilyRequest
{
    public int Id { get; set; }
    public int RequesterAccountId { get; set; }
    public required string Body { get; set; }
    public string Status { get; set; } = FamilyRequestStatus.Requested;
    public DateTime RequestedUtc { get; set; }
    public DateTime? DecidedUtc { get; set; }
    public int? DecidedByAccountId { get; set; }
    public int? FamilyId { get; set; }
}

public static class FamilyRequestStatus
{
    public const string Requested = "Requested";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

internal sealed class OnFamilyConfig : IEntityTypeConfiguration<Family>
{
    public void Configure(EntityTypeBuilder<Family> e)
    {
        e.ToTable("Family");
        e.HasKey(x => x.Id).HasName("PK_Family");
        e.Property(x => x.Name).HasMaxLength(30);
        e.Property(x => x.CreatedUtc).HasColumnType("datetime2(3)");
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.CreatedByAccountId).HasConstraintName("FK_Family_CreatedBy").OnDelete(DeleteBehavior.NoAction);
        e.HasMany(x => x.Links).WithOne().HasForeignKey(l => l.FamilyId).HasConstraintName("FK_FamilyLink_Family").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FamilyLinkConfig : IEntityTypeConfiguration<FamilyLink>
{
    public void Configure(EntityTypeBuilder<FamilyLink> e)
    {
        e.ToTable("FamilyLink", t => t.HasCheckConstraint("CK_FamilyLink_Relation",
            $"Relation IN ({string.Join(",", FamilyRelation.All.Select(s => $"'{s}'"))})"));
        e.HasKey(x => new { x.FamilyId, x.AccountId }).HasName("PK_FamilyLink");
        e.HasIndex(x => x.AccountId).IsUnique().HasDatabaseName("UQ_FamilyLink_Account");   // 한 계정 한 온가족(R-175)
        e.Property(x => x.Relation).HasMaxLength(20).IsUnicode(false);
        e.Property(x => x.AddedUtc).HasColumnType("datetime2(3)");
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).HasConstraintName("FK_FamilyLink_Account").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.AddedByAccountId).HasConstraintName("FK_FamilyLink_AddedBy").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FamilyRequestConfig : IEntityTypeConfiguration<FamilyRequest>
{
    public void Configure(EntityTypeBuilder<FamilyRequest> e)
    {
        e.ToTable("FamilyRequest", t => t.HasCheckConstraint("CK_FamilyRequest_Status", "Status IN ('Requested','Approved','Rejected')"));
        e.HasKey(x => x.Id).HasName("PK_FamilyRequest");
        e.Property(x => x.Body).HasMaxLength(500);
        e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
        e.Property(x => x.RequestedUtc).HasColumnType("datetime2(3)");
        e.Property(x => x.DecidedUtc).HasColumnType("datetime2(3)");
        e.HasIndex(x => x.RequesterAccountId).IsUnique().HasFilter("[Status] = 'Requested'").HasDatabaseName("UX_FamilyRequest_Open");   // R-176
        e.HasIndex(x => new { x.Status, x.RequestedUtc }).HasDatabaseName("IX_FamilyRequest_Status");
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.RequesterAccountId).HasConstraintName("FK_FamilyRequest_Requester").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.DecidedByAccountId).HasConstraintName("FK_FamilyRequest_DecidedBy").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).HasConstraintName("FK_FamilyRequest_Family").OnDelete(DeleteBehavior.NoAction);
    }
}
