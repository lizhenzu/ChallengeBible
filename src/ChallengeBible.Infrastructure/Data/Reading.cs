using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

// 도전!!!성경통독(0039 ~ 0042)의 참조 표. 통독 기록은 Shared.cs 의 DM2_Reading* 표(0090).

/// <summary>장마다 절 수(기본 번역 개역개정 기준, 0041). 성경 본문 적재 때 채운다. 66권 1,189장.</summary>
public sealed class BibleChapter
{
    public byte BookId { get; set; }
    public short Chapter { get; set; }
    public short VerseCount { get; set; }
}

/// <summary>부서 인기 검색어용 기록(R-164): 영역 · 검색어 · 시각만. 누가 찾았는지는 남기지 않는다(계정 열 없음).</summary>
public sealed class SearchLog : ITenantOwned
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public required string Term { get; set; }
    public DateTime SearchedUtc { get; set; }
    public const int MaxTerm = 50;
}

internal sealed class SearchLogConfig : IEntityTypeConfiguration<SearchLog>
{
    public void Configure(EntityTypeBuilder<SearchLog> e)
    {
        e.ToTable("SearchLog");
        e.HasKey(x => x.Id).HasName("PK_SearchLog").IsClustered(false);
        e.HasIndex(x => new { x.TenantId, x.Id }).IsUnique().IsClustered().HasDatabaseName("UQ_SearchLog_TenantId_Id");
        e.HasIndex(x => new { x.TenantId, x.SearchedUtc }).IncludeProperties(x => x.Term).HasDatabaseName("IX_SearchLog_Recent");
        e.Property(x => x.Term).HasMaxLength(SearchLog.MaxTerm);
        e.Property(x => x.SearchedUtc).HasColumnType("datetime2(3)");
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).HasConstraintName("FK_SearchLog_Tenant").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class BibleChapterConfig : IEntityTypeConfiguration<BibleChapter>
{
    public void Configure(EntityTypeBuilder<BibleChapter> e)
    {
        e.ToTable("BibleChapter", t => t.HasCheckConstraint("CK_BibleChapter_Verses", "VerseCount >= 1"));
        e.HasKey(x => new { x.BookId, x.Chapter }).HasName("PK_BibleChapter");
        e.HasOne<BibleBook>().WithMany().HasForeignKey(x => x.BookId).HasConstraintName("FK_BibleChapter_Book").OnDelete(DeleteBehavior.NoAction);
    }
}
