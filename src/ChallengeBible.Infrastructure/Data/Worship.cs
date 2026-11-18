using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>
/// 부서 예배 안내 한 줄(ADR 0116): 언제 · 어디서. 부서마다 여러 줄(예: 1부 · 2부)도 되고, 부서 관리자가 언제든 바꾼다.
/// 바꾼 기록은 AuditLog(WorshipSchedule). 주보는 링크 · 날짜만(올리기 기능은 나중에 정함).
/// </summary>
public sealed class WorshipSchedule : ITenantOwned
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    /// <summary>화면에 보이는 순서(작은 것부터).</summary>
    public int SortOrder { get; set; }
    /// <summary>"1부 예배"처럼 이름이 필요할 때만. 비우면 "○○부 예배".</summary>
    public string? Title { get; set; }
    /// <summary>요일(0 = 일요일 … 6 = 토요일, System.DayOfWeek 와 같다).</summary>
    public byte DayOfWeek { get; set; }
    /// <summary>시작 시각(부서 시간대의 벽시계 시각 — UTC 로 바꾸지 않는다).</summary>
    public TimeOnly StartTime { get; set; }
    /// <summary>장소(예: 비전센터 믿음홀(3F)).</summary>
    public required string Place { get; set; }
    /// <summary>주소(예: 서울특별시 송파구 석촌호수로 102). 비우면 안 보인다.</summary>
    public string? Address { get; set; }
    /// <summary>주보 링크(교회 홈페이지 등). 비우면 주보 버튼이 안 보인다.</summary>
    public string? BulletinUrl { get; set; }
    /// <summary>주보 날짜(부서 날짜). 링크와 함께 바꾼다.</summary>
    public DateOnly? BulletinDate { get; set; }
    /// <summary>잠시 숨기기(지우지 않고).</summary>
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedUtc { get; set; }
    public string? UpdatedByUserName { get; set; }
}

internal sealed class WorshipScheduleConfig : IEntityTypeConfiguration<WorshipSchedule>
{
    public void Configure(EntityTypeBuilder<WorshipSchedule> e)
    {
        e.ToTable("WorshipSchedule");
        e.HasKey(x => new { x.TenantId, x.Id });
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.Title).HasMaxLength(50);
        e.Property(x => x.Place).HasMaxLength(100);
        e.Property(x => x.Address).HasMaxLength(200);
        e.Property(x => x.BulletinUrl).HasMaxLength(500).IsUnicode(false);
        e.Property(x => x.StartTime).HasColumnType("time(0)");
        e.Property(x => x.UpdatedUtc).HasPrecision(3);
        e.Property(x => x.UpdatedByUserName).HasMaxLength(256);
        e.HasIndex(x => new { x.TenantId, x.SortOrder });
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
    }
}
