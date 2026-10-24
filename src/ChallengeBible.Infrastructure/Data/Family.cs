using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

// 가족팀(R-90 ~ R-97, 0012 · 0046). 물리 설계 참조 DDL 의 app.FamilyTeam ~ FamilyMilestoneDismiss 와 이름 · 형식을 맞춘다.

/// <summary>가족팀: 영역 관리자가 만든다(R-90). 이름이 비면 "{대표 학생} 가족"(R-93).</summary>
public sealed class FamilyTeam : ITenantOwned
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string? Name { get; set; }
    public string? Memo { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public List<FamilyMember> Members { get; set; } = [];
}

/// <summary>구성원: 같은 영역의 참여자만(R-90). 한 참여자는 활성 팀 하나에만(R-91, 필터 고유 인덱스).</summary>
public sealed class FamilyMember : ITenantOwned
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int FamilyTeamId { get; set; }
    public int ParticipantId { get; set; }
    public string Relation { get; set; } = FamilyRelation.Student;
    public byte SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class FamilyRelation
{
    public const string Student = "Student";
    public const string Mother = "Mother";
    public const string Father = "Father";
    public const string Grandmother = "Grandmother";
    public const string Grandfather = "Grandfather";
    public const string Other = "Other";
    public static readonly string[] All = [Student, Mother, Father, Grandmother, Grandfather, Other];
}

/// <summary>가족 단계 도달(R-96) — 시즌 · 단계마다 한 번.</summary>
public sealed class FamilyMilestoneReached : ITenantOwned
{
    public int TenantId { get; set; }
    public int FamilyTeamId { get; set; }
    public short Year { get; set; }
    public int Threshold { get; set; }
    public DateTime ReachedUtc { get; set; }
}

/// <summary>관리자가 팀별로 숨긴 가족 단계 축하 카드(R-97).</summary>
public sealed class FamilyMilestoneDismiss : ITenantOwned
{
    public int TenantId { get; set; }
    public int FamilyTeamId { get; set; }
    public short Year { get; set; }
    public int Threshold { get; set; }
    public DateTime DismissedUtc { get; set; }
    public int DismissedByAccountId { get; set; }
}

internal sealed class FamilyTeamConfig : IEntityTypeConfiguration<FamilyTeam>
{
    public void Configure(EntityTypeBuilder<FamilyTeam> e)
    {
        e.ToTable("FamilyTeam");
        e.HasKey(x => x.Id).HasName("PK_FamilyTeam").IsClustered(false);
        e.HasAlternateKey(x => new { x.TenantId, x.Id }).HasName("UQ_FamilyTeam_TenantId_Id").IsClustered();
        e.Property(x => x.Name).HasMaxLength(50);
        e.Property(x => x.Memo).HasMaxLength(500);
        e.Property(x => x.CreatedUtc).HasColumnType("datetime2(3)");
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).HasConstraintName("FK_FamilyTeam_Tenant").OnDelete(DeleteBehavior.NoAction);
        e.HasMany(x => x.Members).WithOne()
            .HasForeignKey(m => new { m.TenantId, m.FamilyTeamId }).HasPrincipalKey(t => new { t.TenantId, t.Id })
            .HasConstraintName("FK_FamilyMember_Team").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FamilyMemberConfig : IEntityTypeConfiguration<FamilyMember>
{
    public void Configure(EntityTypeBuilder<FamilyMember> e)
    {
        e.ToTable("FamilyMember", t => t.HasCheckConstraint("CK_FamilyMember_Relation",
            $"Relation IN ({string.Join(",", FamilyRelation.All.Select(s => $"'{s}'"))})"));
        e.HasKey(x => x.Id).HasName("PK_FamilyMember").IsClustered(false);
        e.HasAlternateKey(x => new { x.TenantId, x.Id }).HasName("UQ_FamilyMember_TenantId_Id").IsClustered();
        e.HasIndex(x => new { x.TenantId, x.FamilyTeamId, x.ParticipantId }).IsUnique().HasDatabaseName("UQ_FamilyMember_TeamParticipant");
        e.HasIndex(x => new { x.TenantId, x.ParticipantId }).IsUnique().HasFilter("[IsActive] = 1").HasDatabaseName("UX_FamilyMember_OneActiveTeam");   // R-91
        e.Property(x => x.Relation).HasMaxLength(20).IsUnicode(false);
        // 같은 영역의 참여자만(R-90, T-3 복합 외래 키)
        e.HasOne<Participant>().WithMany().HasForeignKey(x => new { x.TenantId, x.ParticipantId }).HasPrincipalKey(p => new { p.TenantId, p.Id })
            .HasConstraintName("FK_FamilyMember_Participant").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FamilyMilestoneReachedConfig : IEntityTypeConfiguration<FamilyMilestoneReached>
{
    public void Configure(EntityTypeBuilder<FamilyMilestoneReached> e)
    {
        e.ToTable("FamilyMilestoneReached");
        e.HasKey(x => new { x.TenantId, x.FamilyTeamId, x.Year, x.Threshold }).HasName("PK_FamilyMilestoneReached");
        e.Property(x => x.ReachedUtc).HasColumnType("datetime2(3)");
        e.HasOne<FamilyTeam>().WithMany().HasForeignKey(x => new { x.TenantId, x.FamilyTeamId }).HasPrincipalKey(t => new { t.TenantId, t.Id })
            .HasConstraintName("FK_FamilyMilestoneReached_Team").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FamilyMilestoneDismissConfig : IEntityTypeConfiguration<FamilyMilestoneDismiss>
{
    public void Configure(EntityTypeBuilder<FamilyMilestoneDismiss> e)
    {
        e.ToTable("FamilyMilestoneDismiss");
        e.HasKey(x => new { x.TenantId, x.FamilyTeamId, x.Year, x.Threshold }).HasName("PK_FamilyMilestoneDismiss");
        e.Property(x => x.DismissedUtc).HasColumnType("datetime2(3)");
        e.HasOne<FamilyTeam>().WithMany().HasForeignKey(x => new { x.TenantId, x.FamilyTeamId }).HasPrincipalKey(t => new { t.TenantId, t.Id })
            .HasConstraintName("FK_FamilyMilestoneDismiss_Team").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.DismissedByAccountId).HasConstraintName("FK_FamilyMilestoneDismiss_Account").OnDelete(DeleteBehavior.NoAction);
    }
}
