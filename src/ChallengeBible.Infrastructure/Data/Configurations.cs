using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

// 물리 설계 참조 DDL(docs/design/물리-설계.sql)과 이름 · 형식을 맞춘다.
// 영역 범위 표: 기본 키는 비클러스터, (TenantId, Id) 고유 클러스터(물리 설계 2절).

internal sealed class TenantConfig : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> e)
    {
        e.ToTable("Tenant", t =>
        {
            t.HasCheckConstraint("CK_Tenant_Status", "Status IN ('Active','Suspended')");
            t.HasCheckConstraint("CK_Tenant_Program", "MissionEnabled = 1 OR ReadingEnabled = 1");
            t.HasCheckConstraint("CK_Tenant_Slug", "LEN(Slug) >= 2 AND Slug NOT LIKE '%[^a-z0-9-]%'");
            t.HasCheckConstraint("CK_Tenant_JoinCode", "JoinCode NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN");
        });
        e.HasKey(x => x.Id).HasName("PK_Tenant");
        e.Property(x => x.Slug).HasMaxLength(40).IsUnicode(false);
        e.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("UQ_Tenant_Slug");
        e.Property(x => x.Name).HasMaxLength(100);
        e.Property(x => x.JoinCode).HasMaxLength(6).IsFixedLength().IsUnicode(false);
        e.HasIndex(x => x.JoinCode).IsUnique().HasDatabaseName("UQ_Tenant_JoinCode");
        e.Property(x => x.TimeZoneId).HasMaxLength(64).IsUnicode(false);
        e.Property(x => x.ThemeColor).HasMaxLength(20).IsUnicode(false);
        e.Property(x => x.AppName).HasMaxLength(30);
        e.Property(x => x.AppShortName).HasMaxLength(12);
        e.Property(x => x.ReportOrg).HasMaxLength(40);
        e.Property(x => x.Milestones).HasMaxLength(100).IsUnicode(false);
        e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
        e.Property(x => x.CreatedUtc).HasColumnType("datetime2(3)");
        e.HasIndex(x => x.IsLobby).IsUnique().HasFilter("[IsLobby] = 1").HasDatabaseName("UX_Tenant_Lobby");   // 임시 부서는 하나(0064)
    }
}

internal sealed class TenantSeasonConfig : IEntityTypeConfiguration<TenantSeason>
{
    public void Configure(EntityTypeBuilder<TenantSeason> e)
    {
        e.ToTable("TenantSeason", t => t.HasCheckConstraint("CK_TenantSeason_StartInYear", "YEAR(StartDate) = [Year]"));
        e.HasKey(x => new { x.TenantId, x.Year }).HasName("PK_TenantSeason");
        e.Property(x => x.ChangedUtc).HasColumnType("datetime2(3)");
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).HasConstraintName("FK_TenantSeason_Tenant").OnDelete(DeleteBehavior.NoAction);
        e.Property(x => x.ChangedByUserName).HasMaxLength(256);
    }
}
