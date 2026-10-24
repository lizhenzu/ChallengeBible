using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>
/// 계정 범위 표(0029): 영역이 아니라 로그인 계정에 붙는다. TenantId 가 없고,
/// 앱 전역 필터(AccountId = 이 요청의 계정)와 RLS(sec.fn_Account)로 이중 격리한다.
/// </summary>
public interface IAccountOwned
{
    int AccountId { get; }
}

/// <summary>절 읽음 표시(R-112). 번역본과 무관하게 절 위치로 남기고, 읽은 시각(UTC)을 처음부터 남긴다(R-114). 점수와 무관(R-113).</summary>
public sealed class VerseReadMark : IAccountOwned
{
    public int AccountId { get; set; }
    public byte BookId { get; set; }
    public short Chapter { get; set; }
    public short Verse { get; set; }
    public DateTime ReadUtc { get; set; }
}

/// <summary>책갈피(R-112). 절 위치로 남긴다.</summary>
public sealed class Bookmark : IAccountOwned
{
    public int AccountId { get; set; }
    public byte BookId { get; set; }
    public short Chapter { get; set; }
    public short Verse { get; set; }
    public DateTime CreatedUtc { get; set; }
}

internal sealed class VerseReadMarkConfig : IEntityTypeConfiguration<VerseReadMark>
{
    public void Configure(EntityTypeBuilder<VerseReadMark> e)
    {
        e.ToTable("VerseReadMark");
        e.HasKey(x => new { x.AccountId, x.BookId, x.Chapter, x.Verse }).HasName("PK_VerseReadMark");
        e.Property(x => x.ReadUtc).HasColumnType("datetime2(3)");
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).HasConstraintName("FK_VerseReadMark_Account").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<BibleBook>().WithMany().HasForeignKey(x => x.BookId).HasConstraintName("FK_VerseReadMark_Book").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class BookmarkConfig : IEntityTypeConfiguration<Bookmark>
{
    public void Configure(EntityTypeBuilder<Bookmark> e)
    {
        e.ToTable("Bookmark");
        e.HasKey(x => new { x.AccountId, x.BookId, x.Chapter, x.Verse }).HasName("PK_Bookmark");
        e.Property(x => x.CreatedUtc).HasColumnType("datetime2(3)");
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).HasConstraintName("FK_Bookmark_Account").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<BibleBook>().WithMany().HasForeignKey(x => x.BookId).HasConstraintName("FK_Bookmark_Book").OnDelete(DeleteBehavior.NoAction);
    }
}
