using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>
/// 부서 명부의 사람 한 줄(ADR 0093 사람 · 소속). 가입하지 않은 사람 · 새친구도 올린다.
/// UserId 가 차 있으면 그 계정과 연결된 줄이고, 역할은 소속(DeptRoleUserAssoc)에서 읽는다. 비어 있으면 RoleCode 를 쓴다.
/// 생일은 월 · 일만(연도 없음), 연락처는 두 칸. 출석 · 반 · 생일 · 연락은 이 줄의 Id 로 저장한다.
/// </summary>
public sealed class Person : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public required string Name { get; set; }
    /// <summary>연결 안 된 사람의 역할 코드(비면 학부모). 연결되면 쓰지 않는다.</summary>
    public string? RoleCode { get; set; }
    public byte? BirthMonth { get; set; }
    public byte? BirthDay { get; set; }
    public string? Phone1 { get; set; }
    public string? Phone2 { get; set; }
    /// <summary>연결된 계정(Identity.User.Id). 아이디가 바뀌어도 그대로라 Id 로 잇는다.</summary>
    public string? UserId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public string? CreatedByUserName { get; set; }
}

internal sealed class PersonConfig : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> e)
    {
        e.ToTable("Person");
        e.HasKey(x => new { x.TenantId, x.Id });
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.Name).HasMaxLength(50);
        e.Property(x => x.RoleCode).HasMaxLength(50);
        e.Property(x => x.Phone1).HasMaxLength(20).IsUnicode(false);
        e.Property(x => x.Phone2).HasMaxLength(20).IsUnicode(false);
        e.Property(x => x.UserId).HasMaxLength(450);
        e.Property(x => x.CreatedUtc).HasPrecision(3);
        e.Property(x => x.CreatedByUserName).HasMaxLength(256);
        e.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique().HasFilter("[UserId] IS NOT NULL");
        e.HasIndex(x => x.UserId);
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
    }
}
