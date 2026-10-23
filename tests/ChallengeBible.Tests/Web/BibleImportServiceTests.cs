using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;

namespace ChallengeBible.Tests.Web;

/// <summary>성경 본문 적재(0036)의 검사. 적재 SQL 자체는 실제 SQL Server 에서 확인한다(InMemory 는 원본 표가 없다).</summary>
public class BibleImportServiceTests
{
    private readonly string _db = InMemoryDb.NewName();
    private BibleImportService Svc() => new(new InMemoryDb(_db, new RequestScope()));

    private void Load(byte translation, int book, int chapters, int versesPerChapter)
    {
        using var raw = InMemoryDb.Raw(_db);
        raw.Database.EnsureCreated();   // 66권 · 번역본 3개 시드(HasData)
        for (short c = 1; c <= chapters; c++)
            for (short v = 1; v <= versesPerChapter; v++)
                raw.BibleVerses.Add(new BibleVerse { TranslationId = translation, BookId = (byte)book, Chapter = c, Verse = v, Text = $"t{translation}-{book}:{c}:{v}" });
        raw.SaveChanges();
    }

    [Fact]
    public void 번역본_세_개가_기본으로_들어_있다()
    {
        Assert.Equal(["KRV", "KLB", "NIV"], BibleTranslation.All.Select(t => t.Code).ToArray());
        Assert.Equal("eBible", BibleTranslation.All[0].SourceTable);
    }

    [Fact]
    public async Task 장_수가_BibleBook_과_같으면_문제가_없다()
    {
        Load(1, 8, 4, 3);   // 룻기 4장
        var r = await Svc().CheckAsync();
        Assert.True(r.Clean);
        Assert.Equal(("KRV", 12, 1, 4), r.Summary.Single());
    }

    [Fact]
    public async Task 장이_빠졌으면_장_수_문제로_알려_준다()
    {
        Load(1, 8, 3, 3);   // 룻기는 4장인데 3장만
        var issue = (await Svc().CheckAsync()).Issues.Single();
        Assert.Equal(("장 수", "KRV", 8, null, 4, 3), (issue.Kind, issue.Code, issue.BookId, issue.Chapter, issue.Expected, issue.Actual));
    }

    [Fact]
    public async Task 번역본끼리_장별_절_수가_다르면_알려_준다()
    {
        Load(1, 8, 4, 3);
        Load(2, 8, 4, 3);
        using (var raw = InMemoryDb.Raw(_db))   // 현대인의성경 룻기 2장에 절 하나 더
        {
            raw.BibleVerses.Add(new BibleVerse { TranslationId = 2, BookId = 8, Chapter = 2, Verse = 4, Text = "extra" });
            raw.SaveChanges();
        }
        var issue = (await Svc().CheckAsync()).Issues.Single();
        Assert.Equal(("절 수", "KLB", 8, (int?)2, 3, 4), (issue.Kind, issue.Code, issue.BookId, issue.Chapter, issue.Expected, issue.Actual));
    }
}
