using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>
/// 자녀를 위한 365 기도(ADR 0106): 날짜(월일 "0101" ~ "1231", 2/29 포함 366편)마다 기도문 한 편. 해마다 같은 날 같은 기도문.
/// 플랫폼 공용(TenantId 없음, 성경 데이터 · 업무 목록처럼) — 플랫폼 관리자만 시스템 연결로 고친다.
/// 처음에는 샘플(IsSample), 진짜는 유년부에서 JSON 으로 옮겨 같은 날짜를 바꿔 넣는다.
/// </summary>
public sealed class Prayer365
{
    public required string MonthDay { get; set; }
    public required string Title { get; set; }
    public string? VerseRef { get; set; }
    public string? VerseText { get; set; }
    public required string Body { get; set; }
    /// <summary>교정 기록 JSON [{"From":"…","To":"…"}](유년부에서 옮겨 온 그대로).</summary>
    public string? Fixes { get; set; }
    public bool IsSample { get; set; }
    public bool IsReviewed { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>[오늘 기도 했어요](ADR 0106, R-317): 한 사람 · 하루(부서 날짜)에 한 줄. 누른 부서를 남긴다(부서별 집계, R-318).</summary>
public sealed class Prayer365Log : ITenantOwned
{
    public int TenantId { get; set; }
    public long Id { get; set; }
    public required string UserName { get; set; }
    public DateOnly PrayDate { get; set; }
    public required string MonthDay { get; set; }
    public DateTime PrayedUtc { get; set; }
}

/// <summary>기도문을 연 횟수(부서 · 날짜 · 월일). 관리자 보기는 세지 않는다(ADR 0106).</summary>
public sealed class Prayer365View : ITenantOwned
{
    public int TenantId { get; set; }
    public DateOnly ViewDate { get; set; }
    public required string MonthDay { get; set; }
    public int Count { get; set; }
}

internal sealed class Prayer365Config : IEntityTypeConfiguration<Prayer365>
{
    public void Configure(EntityTypeBuilder<Prayer365> e)
    {
        e.ToTable("Prayer365");
        e.HasKey(x => x.MonthDay);
        e.Property(x => x.MonthDay).HasMaxLength(4).IsUnicode(false).IsFixedLength();
        e.Property(x => x.Title).HasMaxLength(200);
        e.Property(x => x.VerseRef).HasMaxLength(100);
        e.Property(x => x.VerseText).HasMaxLength(1000);
        e.Property(x => x.UpdatedUtc).HasPrecision(3);
        e.Property(x => x.UpdatedBy).HasMaxLength(256);
    }
}

internal sealed class Prayer365LogConfig : IEntityTypeConfiguration<Prayer365Log>
{
    public void Configure(EntityTypeBuilder<Prayer365Log> e)
    {
        e.ToTable("Prayer365Log");
        e.HasKey(x => new { x.TenantId, x.Id });
        e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
        e.Property(x => x.UserName).HasMaxLength(256);
        e.Property(x => x.MonthDay).HasMaxLength(4).IsUnicode(false).IsFixedLength();
        e.Property(x => x.PrayedUtc).HasPrecision(3);
        e.HasIndex(x => new { x.UserName, x.PrayDate }).IsUnique().HasDatabaseName("UX_Prayer365Log_User_Day");   // 하루 한 번(부서와 상관없이)
        e.HasIndex(x => new { x.TenantId, x.PrayDate });
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class Prayer365ViewConfig : IEntityTypeConfiguration<Prayer365View>
{
    public void Configure(EntityTypeBuilder<Prayer365View> e)
    {
        e.ToTable("Prayer365View");
        e.HasKey(x => new { x.TenantId, x.ViewDate, x.MonthDay });
        e.Property(x => x.MonthDay).HasMaxLength(4).IsUnicode(false).IsFixedLength();
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
    }
}
