using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>성경 번역본(0036): KRV 개역개정 · KLB 현대인의성경 · NIV. 플랫폼 공용 참조 데이터(TenantId 없음).</summary>
public sealed class BibleTranslation
{
    public byte Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public byte SortOrder { get; set; }

    public const string Krv = "KRV", Klb = "KLB", Niv = "NIV";

    /// <summary>적재 원본(기존 표) 이름. 열 구조: Id · BibleText(책 이름) · Jang(장) · zul(절) · BibleData(본문).</summary>
    public static readonly (byte Id, string Code, string Name, string SourceTable)[] All =
    [
        (1, Krv, "개역개정", "eBible"),
        (2, Klb, "현대인의성경", "eBibleHD"),
        (3, Niv, "NIV", "eBibleEN"),
    ];
}

/// <summary>한 절. (번역본, 책, 장, 절)로 식별한다(0019).</summary>
public sealed class BibleVerse
{
    public byte TranslationId { get; set; }
    public byte BookId { get; set; }
    public short Chapter { get; set; }
    public short Verse { get; set; }
    public required string Text { get; set; }
}

internal sealed class BibleTranslationConfig : IEntityTypeConfiguration<BibleTranslation>
{
    public void Configure(EntityTypeBuilder<BibleTranslation> e)
    {
        e.ToTable("BibleTranslation");
        e.HasKey(x => x.Id).HasName("PK_BibleTranslation");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Code).HasMaxLength(20).IsUnicode(false);
        e.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UQ_BibleTranslation_Code");
        e.Property(x => x.Name).HasMaxLength(50);
        e.HasData(BibleTranslation.All.Select(t => new BibleTranslation { Id = t.Id, Code = t.Code, Name = t.Name, SortOrder = t.Id }));
    }
}

internal sealed class BibleVerseConfig : IEntityTypeConfiguration<BibleVerse>
{
    public void Configure(EntityTypeBuilder<BibleVerse> e)
    {
        e.ToTable("BibleVerse");
        e.HasKey(x => new { x.TranslationId, x.BookId, x.Chapter, x.Verse }).HasName("PK_BibleVerse");
        e.Property(x => x.TranslationId).ValueGeneratedNever();
        e.Property(x => x.Text).HasMaxLength(1000);
        e.HasOne<BibleTranslation>().WithMany().HasForeignKey(x => x.TranslationId).HasConstraintName("FK_BibleVerse_Translation").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<BibleBook>().WithMany().HasForeignKey(x => x.BookId).HasConstraintName("FK_BibleVerse_Book").OnDelete(DeleteBehavior.NoAction);
    }
}
