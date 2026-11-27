using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>
/// 부서마다 직접 입력하는 「이번 주 말씀」(ADR 0142, R-374, 2026-11-27 사용자): **주일 날짜 · 말씀 제목 · 설교 내용** 세 칸.
/// 날짜는 입력한 날이 아니라 그 말씀을 전한 **주일**(일요일만). 부서마다 같은 주일은 한 건(TenantId + SundayDate 가 키).
/// 공용 말씀(플랫폼이 모든 부서에 같이 넣는 오늘의 말씀 · WeeklyVerse)과 별개다. 부서 관리자만 입력한다.
/// </summary>
public sealed class WeeklyWord : ITenantOwned
{
    public int TenantId { get; set; }
    /// <summary>전한 주일 날짜(부서 날짜, 일요일만).</summary>
    public DateOnly SundayDate { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public string? UpdatedByUserName { get; set; }
}

internal sealed class WeeklyWordConfig : IEntityTypeConfiguration<WeeklyWord>
{
    public void Configure(EntityTypeBuilder<WeeklyWord> e)
    {
        e.ToTable("WeeklyWord");
        e.HasKey(x => new { x.TenantId, x.SundayDate });
        e.Property(x => x.Title).HasMaxLength(60);
        e.Property(x => x.Body).HasMaxLength(4000);
        e.Property(x => x.CreatedUtc).HasPrecision(3);
        e.Property(x => x.UpdatedUtc).HasPrecision(3);
        e.Property(x => x.UpdatedByUserName).HasMaxLength(256);
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
    }
}
