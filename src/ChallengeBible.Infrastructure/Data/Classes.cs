using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>
/// 한 해의 반 묶음(ADR 0093 조직 · 반). 해마다 새로 짜고, StartDate(부서 날짜)부터 쓴다 — 그 전까지는 지난해 묶음을 쓴다.
/// </summary>
public sealed class ClassTerm : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public int Year { get; set; }
    /// <summary>"이날부터 사용"(부서 시간대 날짜).</summary>
    public DateOnly StartDate { get; set; }
    public DateTime CreatedUtc { get; set; }
}

/// <summary>반 하나. 새친구반은 해마다 하나(IsNewcomer).</summary>
public sealed class SchoolClass : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public int TermId { get; set; }
    public required string Name { get; set; }
    public bool IsNewcomer { get; set; }
    public DateTime CreatedUtc { get; set; }
}

/// <summary>반에 든 명부 사람(학생 또는 선생님). 한 사람은 한 해에 한 반에만 든다(TermId · PersonId 유일).</summary>
public sealed class ClassMember : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public int TermId { get; set; }
    public int ClassId { get; set; }
    public int PersonId { get; set; }
    public bool IsTeacher { get; set; }
    public DateTime CreatedUtc { get; set; }
}

internal sealed class ClassTermConfig : IEntityTypeConfiguration<ClassTerm>
{
    public void Configure(EntityTypeBuilder<ClassTerm> e)
    {
        e.ToTable("ClassTerm");
        e.HasKey(x => new { x.TenantId, x.Id });
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.CreatedUtc).HasPrecision(3);
        e.HasIndex(x => new { x.TenantId, x.Year }).IsUnique();
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class SchoolClassConfig : IEntityTypeConfiguration<SchoolClass>
{
    public void Configure(EntityTypeBuilder<SchoolClass> e)
    {
        e.ToTable("Class");
        e.HasKey(x => new { x.TenantId, x.Id });
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.Name).HasMaxLength(50);
        e.Property(x => x.CreatedUtc).HasPrecision(3);
        e.HasIndex(x => new { x.TenantId, x.TermId });
        e.HasOne<ClassTerm>().WithMany().HasForeignKey(x => new { x.TenantId, x.TermId }).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class ClassMemberConfig : IEntityTypeConfiguration<ClassMember>
{
    public void Configure(EntityTypeBuilder<ClassMember> e)
    {
        e.ToTable("ClassMember");
        e.HasKey(x => new { x.TenantId, x.Id });
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.CreatedUtc).HasPrecision(3);
        e.HasIndex(x => new { x.TenantId, x.TermId, x.PersonId }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.ClassId });
        e.HasOne<SchoolClass>().WithMany().HasForeignKey(x => new { x.TenantId, x.ClassId }).OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Person>().WithMany().HasForeignKey(x => new { x.TenantId, x.PersonId }).OnDelete(DeleteBehavior.NoAction);
    }
}
