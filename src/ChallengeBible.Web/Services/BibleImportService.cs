using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record BibleSourceStatus(byte Id, string Code, string Name, string SourceTable, bool SourceExists, int SourceVerses, int LoadedVerses);
public sealed record BibleIssue(string Kind, string Code, int BookId, int? Chapter, int Expected, int Actual);
public sealed record BibleCheck(IReadOnlyList<(string Code, int Verses, int Books, int Chapters)> Summary, IReadOnlyList<BibleIssue> Issues)
{
    public bool Clean => Issues.Count == 0;
}
public sealed record BibleImportResult(bool Ok, int Loaded, string Message);

/// <summary>
/// 성경 본문 적재(0036): 같은 DB 의 기존 표(`dbo.eBible` · `eBibleHD` · `eBibleEN`)를 `app.BibleVerse` 로 한 번 옮긴다.
/// 책 번호는 기존 표에서 책 이름이 처음 나오는 순서(1 ~ 66)로 맞추고, 책별 장 수를 `BibleBook` 과 비교해 검증한다.
/// 플랫폼 참조 데이터라 TenantId 가 없다.
/// </summary>
public sealed class BibleImportService(IAppDb db)
{
    private static (byte Id, string Code, string Name, string Source) Find(string code) =>
        BibleTranslation.All.Select(t => (t.Id, t.Code, t.Name, Source: t.SourceTable)).FirstOrDefault(t => t.Code == code);

    public async Task<IReadOnlyList<BibleSourceStatus>> StatusAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var list = new List<BibleSourceStatus>();
        foreach (var (id, code, name, source) in BibleTranslation.All)
        {
            var exists = await ctx.Database.SqlQueryRaw<int>("SELECT CASE WHEN OBJECT_ID(N'dbo.' + {0}) IS NULL THEN 0 ELSE 1 END AS [Value]", source).SingleAsync(ct) == 1;
            var src = exists ? await ctx.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS [Value] FROM dbo.{source}").SingleAsync(ct) : 0;   // source 는 위 고정 목록 값
            var loaded = await ctx.BibleVerses.CountAsync(v => v.TranslationId == id, ct);
            list.Add(new(id, code, name, source, exists, src, loaded));
        }
        return list;
    }

    /// <summary>한 번역본을 지우고 다시 옮긴다(한 트랜잭션). 책이 66권이 아니거나 중복 절이 있으면 아무것도 바꾸지 않는다.</summary>
    public async Task<BibleImportResult> ImportAsync(string code, CancellationToken ct = default)
    {
        var t = Find(code);
        if (t.Code is null) return new(false, 0, "알 수 없는 번역본이에요.");
        await using var ctx = db.Create();
        if (await ctx.Database.SqlQueryRaw<int>("SELECT CASE WHEN OBJECT_ID(N'dbo.' + {0}) IS NULL THEN 0 ELSE 1 END AS [Value]", t.Source).SingleAsync(ct) == 0)
            return new(false, 0, $"원본 표 {t.Source} 가 없어요.");

        var books = await ctx.Database.SqlQueryRaw<int>($"SELECT COUNT(DISTINCT BibleText) AS [Value] FROM dbo.{t.Source}").SingleAsync(ct);
        if (books != 66) return new(false, 0, $"책이 {books}권이에요. 66권이어야 해요.");
        var dups = await ctx.Database.SqlQueryRaw<int>($"SELECT COUNT(*) - COUNT(DISTINCT CONCAT(BibleText, N'|', Jang, N'|', zul)) AS [Value] FROM dbo.{t.Source}").SingleAsync(ct);
        if (dups > 0) return new(false, 0, $"같은 절이 {dups}개 겹쳐 있어요.");
        var tooLong = await ctx.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS [Value] FROM dbo.{t.Source} WHERE LEN(BibleData) > 1000").SingleAsync(ct);
        if (tooLong > 0) return new(false, 0, $"1000자를 넘는 절이 {tooLong}개 있어요.");

        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM app.BibleVerse WHERE TranslationId = {0}", [t.Id], ct);
        var added = await ctx.Database.ExecuteSqlRawAsync($$"""
            WITH b AS (SELECT BibleText, CAST(ROW_NUMBER() OVER (ORDER BY MIN(Id)) AS tinyint) AS BookId FROM dbo.{{t.Source}} GROUP BY BibleText)
            INSERT INTO app.BibleVerse (TranslationId, BookId, Chapter, Verse, Text)
            SELECT {0}, b.BookId, s.Jang, s.zul, s.BibleData
            FROM dbo.{{t.Source}} s JOIN b ON b.BibleText = s.BibleText
            WHERE s.BibleData IS NOT NULL AND LTRIM(RTRIM(s.BibleData)) <> N''
            """, [t.Id], ct);   // 빈 절은 옮기지 않는다(0036: 빠진 절은 비워 둔다)
        await tx.CommitAsync(ct);
        return new(true, added, $"{t.Name} {added:N0}절을 옮겼어요.");
    }

    /// <summary>적재한 번역본 검사: 책별 장 수를 BibleBook 과, 장별 절 수를 번역본끼리 비교한다.</summary>
    public async Task<BibleCheck> CheckAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var codes = await ctx.BibleTranslations.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var books = await ctx.BibleBooks.AsNoTracking().ToDictionaryAsync(b => (int)b.Id, b => (int)b.ChapterCount, ct);
        var chapters = await ctx.BibleVerses.AsNoTracking()
            .GroupBy(v => new { v.TranslationId, v.BookId, v.Chapter })
            .Select(g => new { g.Key.TranslationId, g.Key.BookId, g.Key.Chapter, Verses = g.Count() })
            .ToListAsync(ct);

        var issues = new List<BibleIssue>();
        var summary = new List<(string, int, int, int)>();
        foreach (var tr in chapters.GroupBy(c => c.TranslationId).OrderBy(g => g.Key))
        {
            var code = codes.GetValueOrDefault(tr.Key, tr.Key.ToString());
            summary.Add((code, tr.Sum(c => c.Verses), tr.Select(c => c.BookId).Distinct().Count(), tr.Count()));
            foreach (var book in tr.GroupBy(c => (int)c.BookId))
            {
                var expected = books.GetValueOrDefault(book.Key);
                if (book.Count() != expected) issues.Add(new("장 수", code, book.Key, null, expected, book.Count()));
            }
        }
        // 번역본끼리 장별 절 수가 다른 곳(번역별 절 구분 차이, 0036)
        foreach (var g in chapters.GroupBy(c => (c.BookId, c.Chapter)).Where(g => g.Select(x => x.Verses).Distinct().Count() > 1))
        {
            var baseline = g.OrderBy(x => x.TranslationId).First();
            foreach (var other in g.Where(x => x.Verses != baseline.Verses))
                issues.Add(new("절 수", codes.GetValueOrDefault(other.TranslationId, "?"), g.Key.BookId, g.Key.Chapter, baseline.Verses, other.Verses));
        }
        return new(summary, issues.OrderBy(i => i.BookId).ThenBy(i => i.Chapter).ToList());
    }
}
