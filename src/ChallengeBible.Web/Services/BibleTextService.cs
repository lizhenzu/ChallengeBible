using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>구절 범위(책, 시작 장:절, 끝 장:절). 여러 장에 걸칠 수 있다(R-110).</summary>
public sealed record BibleRef(byte BookId, short StartChapter, short StartVerse, short EndChapter, short EndVerse)
{
    public string Book => BibleBooks.NameOf(BookId);
    public string Label => BibleBooks.Range(BookId, StartChapter, StartVerse, EndChapter, EndVerse);
    /// <summary>"24장 1~9절"처럼 책 이름 뒤에 오는 부분.</summary>
    public string Tail => Label[Book.Length..].Trim();

    public bool IsValid => BookId is >= 1 and <= 66 && StartChapter >= 1 && StartVerse >= 1
        && (EndChapter > StartChapter || (EndChapter == StartChapter && EndVerse >= StartVerse)) && EndChapter - StartChapter < 50;
}

/// <summary>한 절의 번역별 본문(없는 번역은 빠진다 — 0036: 빠진 절은 건너뛴다).</summary>
public sealed record VerseLine(short Chapter, short Verse, IReadOnlyDictionary<string, string> Texts);

public sealed record ReadPage(IReadOnlyList<VerseLine> Lines, int Total, int Page, int Pages, int First, int Last);

/// <summary>성경 본문 읽기(R-110 · R-111). 플랫폼 참조 데이터라 TenantId 가 없다.</summary>
public sealed class BibleTextService(IAppDb db)
{
    public static readonly string[] Codes = [BibleTranslation.Krv, BibleTranslation.Klb, BibleTranslation.Niv];
    public static string Short(string code) => code switch { BibleTranslation.Krv => "개역개정", BibleTranslation.Klb => "현대인", _ => "NIV" };

    public async Task<IReadOnlyList<VerseLine>> RangeAsync(BibleRef r, IReadOnlyList<string> codes, CancellationToken ct = default)
    {
        var ids = BibleTranslation.All.Where(t => codes.Contains(t.Code)).Select(t => t.Id).ToArray();
        if (!r.IsValid || ids.Length == 0) return [];
        var (sc, sv, ec, ev) = (r.StartChapter, r.StartVerse, r.EndChapter, r.EndVerse);
        await using var ctx = db.Create();
        var rows = await ctx.BibleVerses.AsNoTracking()
            .Where(v => ids.Contains(v.TranslationId) && v.BookId == r.BookId
                && (v.Chapter > sc || (v.Chapter == sc && v.Verse >= sv))
                && (v.Chapter < ec || (v.Chapter == ec && v.Verse <= ev)))
            .Select(v => new { v.TranslationId, v.Chapter, v.Verse, v.Text })
            .ToListAsync(ct);
        var code = BibleTranslation.All.ToDictionary(t => t.Id, t => t.Code);
        return rows.GroupBy(x => (x.Chapter, x.Verse)).OrderBy(g => g.Key.Chapter).ThenBy(g => g.Key.Verse)
            .Select(g => new VerseLine(g.Key.Chapter, g.Key.Verse, g.ToDictionary(x => code[x.TranslationId], x => x.Text)))
            .ToList();
    }

    /// <summary>size 0 = 전체. 쪽 번호는 범위 안으로 맞춘다.</summary>
    public static ReadPage Slice(IReadOnlyList<VerseLine> all, int size, int page)
    {
        if (all.Count == 0) return new([], 0, 1, 1, 0, 0);
        var per = size <= 0 ? all.Count : size;
        var pages = (all.Count + per - 1) / per;
        var p = Math.Clamp(page, 1, pages);
        var lines = all.Skip((p - 1) * per).Take(per).ToList();
        return new(lines, all.Count, p, pages, (p - 1) * per + 1, (p - 1) * per + lines.Count);
    }
}
