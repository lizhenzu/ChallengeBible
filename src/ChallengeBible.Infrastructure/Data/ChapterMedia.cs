using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>
/// 장마다 붙는 유튜브 영상(R-166, 논리 설계 ChapterMedia, ADR 0063). 플랫폼 공용 참조 데이터(TenantId 없음, RLS 없음).
/// 처음 데이터는 옛 Bible2 의 dbo.eBibleYouTuBu(장마다 "유튜브 듣기" 1개)에서 옮긴다(tools/sql/chaptermedia-*.sql).
/// 책 개요 영상(책마다)은 BibleBookOverview.VideoUrl1 · 2 에 있다.
/// </summary>
public sealed class ChapterMedia
{
    public int Id { get; set; }
    public byte BookId { get; set; }
    public short Chapter { get; set; }
    /// <summary><see cref="ChapterMediaKind"/></summary>
    public required string Kind { get; set; }
    public byte SortOrder { get; set; }
    /// <summary>유튜브 영상 번호(11자).</summary>
    public required string YoutubeVideoId { get; set; }
    public string? Title { get; set; }
    /// <summary>영상 길이(초). 옛 데이터의 TimeMM · TimeSS — 시작 위치가 아니다(옛 화면도 처음부터 재생).</summary>
    public int? DurationSec { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public static class ChapterMediaKind
{
    /// <summary>장 듣기(낭독)</summary>
    public const string Listen = "Listen";
    /// <summary>장 해설</summary>
    public const string Overview = "Overview";
    public static readonly string[] All = [Listen, Overview];
}

internal sealed class ChapterMediaConfig : IEntityTypeConfiguration<ChapterMedia>
{
    public void Configure(EntityTypeBuilder<ChapterMedia> e)
    {
        e.ToTable("ChapterMedia", t => t.HasCheckConstraint("CK_ChapterMedia_Kind", $"Kind IN ({string.Join(",", ChapterMediaKind.All.Select(k => $"'{k}'"))})"));
        e.HasKey(x => x.Id).HasName("PK_ChapterMedia");
        e.Property(x => x.Kind).HasMaxLength(20).IsUnicode(false);
        e.Property(x => x.YoutubeVideoId).HasMaxLength(20).IsUnicode(false);
        e.Property(x => x.Title).HasMaxLength(200);
        e.HasIndex(x => new { x.BookId, x.Chapter, x.Kind, x.SortOrder }).IsUnique().HasDatabaseName("UQ_ChapterMedia_Slot");
        e.HasOne<BibleBook>().WithMany().HasForeignKey(x => x.BookId).HasConstraintName("FK_ChapterMedia_Book").OnDelete(DeleteBehavior.NoAction);
    }
}
