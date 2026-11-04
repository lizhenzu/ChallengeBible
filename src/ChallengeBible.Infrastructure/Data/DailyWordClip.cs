using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>
/// 그날 말씀 "듣기"가 차례로 재생할 영상 구간(R-127, ADR 0081). 플랫폼 공용 콘텐츠(TenantId 없음, RLS 없음 — <see cref="ChapterMedia"/> 와 같다).
/// 입력은 API 로만, 그날 목록을 통째로 바꾼다. 그날 말씀(DailyWord)이 없어도 날짜로 저장된다.
/// </summary>
public sealed class DailyWordClip
{
    public int Id { get; set; }
    public DateOnly WordDate { get; set; }
    /// <summary>재생 차례(0부터). 보낸 차례 그대로.</summary>
    public byte Seq { get; set; }
    public short Chapter { get; set; }
    /// <summary>유튜브 영상 번호(11자). 비우면 그 장의 기본 듣기 영상(<see cref="ChapterMedia"/>).</summary>
    public string? YoutubeVideoId { get; set; }
    /// <summary>시작(초). 비우면 처음부터.</summary>
    public int? StartSec { get; set; }
    /// <summary>끝(초). 비우면 끝까지.</summary>
    public int? EndSec { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

internal sealed class DailyWordClipConfig : IEntityTypeConfiguration<DailyWordClip>
{
    public void Configure(EntityTypeBuilder<DailyWordClip> e)
    {
        e.ToTable("DailyWordClip");
        e.HasKey(x => x.Id).HasName("PK_DailyWordClip");
        e.Property(x => x.YoutubeVideoId).HasMaxLength(20).IsUnicode(false);
        e.HasIndex(x => new { x.WordDate, x.Seq }).IsUnique().HasDatabaseName("UQ_DailyWordClip_Slot");
    }
}
