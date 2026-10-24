using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

// 도전!!!성경통독(0039 ~ 0042). 통독 기록은 계정에 붙는다(0040 규칙 1, R-158): TenantId 없이 AccountId 로 격리(IAccountOwned + RLS).

/// <summary>회차(1독, 2독, …). 진행 중 회차는 계정마다 하나(R-153). 다 읽으면 Completed, 새로 시작하면 이전 것은 Closed(R-154).</summary>
public sealed class ReadingRound : IAccountOwned
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public int RoundNo { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public DateTime? ClosedUtc { get; set; }
    public bool IsOpen => CompletedUtc is null && ClosedUtc is null;
}

/// <summary>회차의 장 완료(R-151). 읽은 시각은 오늘 읽은 장 · 연속 읽은 날의 근거(R-156).</summary>
public sealed class ChapterRead : IAccountOwned
{
    public int AccountId { get; set; }
    public int RoundNo { get; set; }
    public byte BookId { get; set; }
    public short Chapter { get; set; }
    public DateTime ReadUtc { get; set; }
}

/// <summary>회차별 절 읽음(R-160, 0042). 장의 모든 절이 읽음이면 장 자동 완료(R-162).</summary>
public sealed class RoundVerseRead : IAccountOwned
{
    public int AccountId { get; set; }
    public int RoundNo { get; set; }
    public byte BookId { get; set; }
    public short Chapter { get; set; }
    public short Verse { get; set; }
    public DateTime ReadUtc { get; set; }
}

/// <summary>보던 위치(장 · 첫 절) — 이어 읽기용, 읽음과 무관(R-161).</summary>
public sealed class ChapterProgress : IAccountOwned
{
    public int AccountId { get; set; }
    public int RoundNo { get; set; }
    public byte BookId { get; set; }
    public short Chapter { get; set; }
    public short LastViewedVerse { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

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

internal sealed class ReadingRoundConfig : IEntityTypeConfiguration<ReadingRound>
{
    public void Configure(EntityTypeBuilder<ReadingRound> e)
    {
        e.ToTable("ReadingRound", t =>
        {
            t.HasCheckConstraint("CK_ReadingRound_No", "RoundNo >= 1");
            t.HasCheckConstraint("CK_ReadingRound_End", "CompletedUtc IS NULL OR ClosedUtc IS NULL");
        });
        e.HasKey(x => x.Id).HasName("PK_ReadingRound");
        e.HasAlternateKey(x => new { x.AccountId, x.RoundNo }).HasName("UQ_ReadingRound");
        e.HasIndex(x => x.AccountId).IsUnique().HasFilter("[CompletedUtc] IS NULL AND [ClosedUtc] IS NULL").HasDatabaseName("UX_ReadingRound_Open");
        e.Property(x => x.StartedUtc).HasColumnType("datetime2(3)");
        e.Property(x => x.CompletedUtc).HasColumnType("datetime2(3)");
        e.Property(x => x.ClosedUtc).HasColumnType("datetime2(3)");
        e.Ignore(x => x.IsOpen);
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).HasConstraintName("FK_ReadingRound_Account").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class ChapterReadConfig : IEntityTypeConfiguration<ChapterRead>
{
    public void Configure(EntityTypeBuilder<ChapterRead> e)
    {
        e.ToTable("ChapterRead", t => t.HasCheckConstraint("CK_ChapterRead_Chapter", "Chapter >= 1"));
        e.HasKey(x => new { x.AccountId, x.RoundNo, x.BookId, x.Chapter }).HasName("PK_ChapterRead");
        e.Property(x => x.ReadUtc).HasColumnType("datetime2(3)");
        e.HasIndex(x => new { x.AccountId, x.ReadUtc }).HasDatabaseName("IX_ChapterRead_Day");
        e.HasOne<ReadingRound>().WithMany().HasForeignKey(x => new { x.AccountId, x.RoundNo }).HasPrincipalKey(r => new { r.AccountId, r.RoundNo })
            .HasConstraintName("FK_ChapterRead_Round").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<BibleBook>().WithMany().HasForeignKey(x => x.BookId).HasConstraintName("FK_ChapterRead_Book").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class RoundVerseReadConfig : IEntityTypeConfiguration<RoundVerseRead>
{
    public void Configure(EntityTypeBuilder<RoundVerseRead> e)
    {
        e.ToTable("RoundVerseRead", t => t.HasCheckConstraint("CK_RoundVerseRead_Verse", "Verse >= 1"));
        e.HasKey(x => new { x.AccountId, x.RoundNo, x.BookId, x.Chapter, x.Verse }).HasName("PK_RoundVerseRead");
        e.Property(x => x.ReadUtc).HasColumnType("datetime2(3)");
        e.HasOne<ReadingRound>().WithMany().HasForeignKey(x => new { x.AccountId, x.RoundNo }).HasPrincipalKey(r => new { r.AccountId, r.RoundNo })
            .HasConstraintName("FK_RoundVerseRead_Round").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<BibleChapter>().WithMany().HasForeignKey(x => new { x.BookId, x.Chapter }).HasConstraintName("FK_RoundVerseRead_Chapter").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class ChapterProgressConfig : IEntityTypeConfiguration<ChapterProgress>
{
    public void Configure(EntityTypeBuilder<ChapterProgress> e)
    {
        e.ToTable("ChapterProgress", t => t.HasCheckConstraint("CK_ChapterProgress_Verse", "LastViewedVerse >= 1"));
        e.HasKey(x => new { x.AccountId, x.RoundNo, x.BookId, x.Chapter }).HasName("PK_ChapterProgress");
        e.Property(x => x.UpdatedUtc).HasColumnType("datetime2(3)");
        e.HasIndex(x => new { x.AccountId, x.UpdatedUtc }).IsDescending(false, true).HasDatabaseName("IX_ChapterProgress_Recent");
        e.HasOne<ReadingRound>().WithMany().HasForeignKey(x => new { x.AccountId, x.RoundNo }).HasPrincipalKey(r => new { r.AccountId, r.RoundNo })
            .HasConstraintName("FK_ChapterProgress_Round").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<BibleChapter>().WithMany().HasForeignKey(x => new { x.BookId, x.Chapter }).HasConstraintName("FK_ChapterProgress_Chapter").OnDelete(DeleteBehavior.NoAction);
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
