using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>
/// 업무 꼬리표(ADR 0100, 오늘 할 일): 역할(교사 · 부장 …)과 따로 사람에게 붙이는 일 표시. 지금은 "Gift"(선물 담당) 하나.
/// 부서 관리자가 소속 관리 › 사람 자세히에서 켠다. 부서 범위(RLS · 전역 필터).
/// </summary>
public sealed class MemberTag : ITenantOwned
{
    public int TenantId { get; set; }
    public required string UserId { get; set; }
    public required string Tag { get; set; }
    public DateTime CreatedUtc { get; set; }
    public string? CreatedByUserName { get; set; }
}

/// <summary>
/// 오늘 할 일 "누가 보나"(ADR 0100): 할 일 종류(Kind)마다 볼 사람 묶음(Roles, 쉼표). TenantId 가 null 이면 공통 규칙(플랫폼),
/// 부서 줄이 하나라도 있으면 그 부서는 "우리 부서만 따로"(모든 종류를 부서 줄로). 줄이 없는 종류는 코드 기본값.
/// </summary>
public sealed class TodoRule : ISharedContent
{
    public int Id { get; set; }
    public int? TenantId { get; set; }
    public required string Kind { get; set; }
    public required string Roles { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public string? UpdatedByUserName { get; set; }
}

internal sealed class MemberTagConfig : IEntityTypeConfiguration<MemberTag>
{
    public void Configure(EntityTypeBuilder<MemberTag> e)
    {
        e.ToTable("MemberTag");
        e.HasKey(x => new { x.TenantId, x.UserId, x.Tag });
        e.Property(x => x.UserId).HasMaxLength(450);
        e.Property(x => x.Tag).HasMaxLength(20).IsUnicode(false);
        e.Property(x => x.CreatedUtc).HasPrecision(3);
        e.Property(x => x.CreatedByUserName).HasMaxLength(256);
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class TodoRuleConfig : IEntityTypeConfiguration<TodoRule>
{
    public void Configure(EntityTypeBuilder<TodoRule> e)
    {
        e.ToTable("TodoRule");
        e.HasKey(x => x.Id);
        e.Property(x => x.Kind).HasMaxLength(20).IsUnicode(false);
        e.Property(x => x.Roles).HasMaxLength(100).IsUnicode(false);
        e.Property(x => x.UpdatedUtc).HasPrecision(3);
        e.Property(x => x.UpdatedByUserName).HasMaxLength(256);
        e.HasIndex(x => new { x.TenantId, x.Kind }).IsUnique().HasDatabaseName("UX_TodoRule_Tenant_Kind");
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
    }
}

/// <summary>
/// 업무 목록(2026-11-16 사용자 "역할 하나 + 업무 여러 개"): 플랫폼 관리자가 만들고 모든 부서가 같이 쓴다. Key 는 사람 꼬리표(MemberTag.Tag)에 그대로 쓴다.
/// IsAdmin 이면 이 업무를 맡은 사람은 그 부서의 부서 관리자(부서 관리자 규칙). 처음에는 Gift(선물 담당) 하나.
/// </summary>
public sealed class Duty
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public bool IsAdmin { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedUtc { get; set; }
}

internal sealed class DutyConfig : IEntityTypeConfiguration<Duty>
{
    public void Configure(EntityTypeBuilder<Duty> e)
    {
        e.ToTable("Duty");
        e.HasKey(x => x.Key);
        e.Property(x => x.Key).HasMaxLength(20).IsUnicode(false);
        e.Property(x => x.Name).HasMaxLength(30);
        e.Property(x => x.CreatedUtc).HasPrecision(3);
        e.HasIndex(x => x.Name).IsUnique().HasDatabaseName("UX_Duty_Name");
        e.HasData(new Duty { Key = "Gift", Name = "선물 담당", IsAdmin = true, SortOrder = 1, CreatedUtc = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc) });
    }
}
