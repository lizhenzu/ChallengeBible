using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>
/// 책 개요(R-116, ADR 0062): 성경 읽기 · 통독 본문 위에 보이는 66권 소개. 플랫폼 공용 참조 데이터(TenantId 없음, RLS 없음).
/// 처음 데이터는 옛 Bible2 의 dbo.BibleBasicInfo(간략본) · dbo.BibleBookOverview_Detail(상세본)에서 옮긴다(tools/sql/bookoverview-*.sql).
/// </summary>
public sealed class BibleBookOverview
{
    public byte BookId { get; set; }
    // 간략본
    public string? Author { get; set; }
    public string? WhenWritten { get; set; }
    public string? Summary { get; set; }
    public string? Purpose { get; set; }
    // 상세본
    public string? DetailAuthor { get; set; }
    public string? AuthorDescription { get; set; }
    public string? WrittenFrom { get; set; }
    public string? WrittenTo { get; set; }
    public string? AlternativeDate { get; set; }
    public string? WrittenPlace { get; set; }
    public string? WrittenTarget { get; set; }
    public string? HistoricalPeriod { get; set; }
    public string? HistoricalBackground { get; set; }
    public string? Keywords { get; set; }
    public string? ContentSummary { get; set; }
    // 관련 영상(유튜브 주소)
    public string? VideoUrl1 { get; set; }
    public string? VideoUrl2 { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

internal sealed class BibleBookOverviewConfig : IEntityTypeConfiguration<BibleBookOverview>
{
    public void Configure(EntityTypeBuilder<BibleBookOverview> e)
    {
        e.ToTable("BibleBookOverview");
        e.HasKey(x => x.BookId).HasName("PK_BibleBookOverview");
        e.Property(x => x.BookId).ValueGeneratedNever();
        e.Property(x => x.Author).HasMaxLength(200);
        e.Property(x => x.WhenWritten).HasMaxLength(200);
        e.Property(x => x.DetailAuthor).HasMaxLength(200);
        e.Property(x => x.WrittenFrom).HasMaxLength(20);
        e.Property(x => x.WrittenTo).HasMaxLength(20);
        e.Property(x => x.AlternativeDate).HasMaxLength(100);
        e.Property(x => x.WrittenPlace).HasMaxLength(200);
        e.Property(x => x.HistoricalPeriod).HasMaxLength(100);
        e.Property(x => x.Keywords).HasMaxLength(300);
        e.Property(x => x.VideoUrl1).HasMaxLength(200);
        e.Property(x => x.VideoUrl2).HasMaxLength(200);
        e.HasOne<BibleBook>().WithOne().HasForeignKey<BibleBookOverview>(x => x.BookId).HasConstraintName("FK_BibleBookOverview_Book").OnDelete(DeleteBehavior.NoAction);
    }
}
