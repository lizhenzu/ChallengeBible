using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record SearchHit(byte BookId, short Chapter, short Verse, IReadOnlyDictionary<string, string> Texts);
public sealed record SearchResult(string Term, int Total, int Ot, int Nt, IReadOnlyList<(byte BookId, int Count)> Books, IReadOnlyList<SearchHit> Hits, int Shown);

/// <summary>
/// 성경 검색(R-163) · 부서 인기 검색어(R-164). 한글이 있으면 개역개정에서, 없으면 NIV 에서 찾고, 보여 줄 번역은 고른다(R-165).
/// 검색어는 이 부서의 기록으로만 남긴다(누가 찾았는지 없음). 구절 주소("요 3:16", "창세기 24")는 바로 그 본문으로 간다.
/// </summary>
public sealed partial class BibleSearchService(IAppDb db, TenantContext tenant, TimeProvider time)
{
    public const int MaxShown = 200;
    public static readonly string[] Suggestions = ["사랑", "믿음", "구원", "은혜", "기도", "평안", "소망", "감사"];

    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"[가-힣]")] private static partial Regex Hangul();
    [GeneratedRegex(@"(\d)\s*편")] private static partial Regex Psalm();
    [GeneratedRegex(@"^(?<book>[가-힣0-9 ]+?)\s*(?<ch>\d{1,3})(?:\s*[:장]\s*(?<v>\d{1,3})\s*절?)?\s*장?$")] private static partial Regex Reference();

    /// <summary>앞뒤 공백 · 겹친 공백을 정리하고 50자로 자른다.</summary>
    public static string Normalize(string? q)
    {
        var s = Spaces().Replace((q ?? "").Trim(), " ");
        return s.Length > SearchLog.MaxTerm ? s[..SearchLog.MaxTerm] : s;
    }

    /// <summary>"요한복음 3:16", "요 3:16", "창세기 24", "시편 23편" 같은 구절 주소면 (책, 장, 절).</summary>
    public static (byte Book, short Chapter, short Verse)? ParseReference(string q)
    {
        var m = Reference().Match(Psalm().Replace(q, "$1장"));   // "시편 23편" → "시편 23장" (책 이름의 '편'은 그대로)
        if (!m.Success) return null;
        var name = m.Groups["book"].Value.Trim();
        var book = BibleBooks.All().FirstOrDefault(b => b.Name == name || b.ShortName == name);
        if (book is null) return null;
        var ch = short.Parse(m.Groups["ch"].Value);
        if (ch < 1 || ch > book.ChapterCount) return null;
        var v = m.Groups["v"].Success ? short.Parse(m.Groups["v"].Value) : (short)1;
        return (book.Id, ch, v < 1 ? (short)1 : v);
    }

    /// <summary>찾기. scope: all · ot · nt · 책 번호. 보여 주는 절은 200개까지, 개수는 전부 센다.</summary>
    public async Task<SearchResult> SearchAsync(string term, IReadOnlyList<string> show, string scope, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var match = Hangul().IsMatch(term) ? (byte)1 : (byte)3;   // 한글 → 개역개정, 영어 → NIV
        var hits = await ctx.BibleVerses.AsNoTracking()
            .Where(v => v.TranslationId == match && v.Text.Contains(term))
            .OrderBy(v => v.BookId).ThenBy(v => v.Chapter).ThenBy(v => v.Verse)
            .Select(v => new { v.BookId, v.Chapter, v.Verse }).ToListAsync(ct);
        var books = hits.GroupBy(h => h.BookId).Select(g => (g.Key, g.Count())).ToList();
        var filtered = scope switch
        {
            "ot" => hits.Where(h => h.BookId <= 39).ToList(),
            "nt" => hits.Where(h => h.BookId >= 40).ToList(),
            _ when byte.TryParse(scope, out var b) => hits.Where(h => h.BookId == b).ToList(),
            _ => hits,
        };
        var page = filtered.Take(MaxShown).ToList();

        // 보여 줄 번역의 본문: 고른 절이 있는 책 · 장만 읽고 나머지는 걸러 낸다
        var ids = BibleTranslation.All.Where(t => show.Contains(t.Code)).Select(t => t.Id).ToArray();
        var bookIds = page.Select(p => p.BookId).Distinct().ToArray();
        var chapters = page.Select(p => p.Chapter).Distinct().ToArray();
        var keys = page.Select(p => (p.BookId, p.Chapter, p.Verse)).ToHashSet();
        var texts = page.Count == 0 ? [] : (await ctx.BibleVerses.AsNoTracking()
            .Where(v => ids.Contains(v.TranslationId) && bookIds.Contains(v.BookId) && chapters.Contains(v.Chapter))
            .Select(v => new { v.TranslationId, v.BookId, v.Chapter, v.Verse, v.Text }).ToListAsync(ct))
            .Where(v => keys.Contains((v.BookId, v.Chapter, v.Verse))).ToList();
        var code = BibleTranslation.All.ToDictionary(t => t.Id, t => t.Code);
        var list = page.Select(p => new SearchHit(p.BookId, p.Chapter, p.Verse,
            texts.Where(t => t.BookId == p.BookId && t.Chapter == p.Chapter && t.Verse == p.Verse).ToDictionary(t => code[t.TranslationId], t => t.Text))).ToList();
        return new(term, hits.Count, hits.Count(h => h.BookId <= 39), hits.Count(h => h.BookId >= 40), books, list, filtered.Count);
    }

    /// <summary>이 부서의 검색 기록 한 줄(R-164). 검색어와 시각만.</summary>
    public async Task LogAsync(string term, CancellationToken ct = default)
    {
        if (term.Length == 0) return;
        await using var ctx = db.Create();
        ctx.SearchLogs.Add(new SearchLog { TenantId = tenant.Current.Id, Term = term, SearchedUtc = time.GetUtcNow().UtcDateTime });
        await ctx.SaveChangesAsync(ct);
    }

    /// <summary>이 부서 최근 30일 인기 검색어 상위 10(R-164). 다른 부서 것은 보이지 않는다(필터 + RLS).</summary>
    public async Task<IReadOnlyList<(string Term, int Count)>> PopularAsync(CancellationToken ct = default)
    {
        var since = time.GetUtcNow().UtcDateTime.AddDays(-30);
        await using var ctx = db.Create();
        var rows = await ctx.SearchLogs.AsNoTracking().Where(s => s.SearchedUtc >= since)
            .GroupBy(s => s.Term).Select(g => new { g.Key, N = g.Count() })
            .OrderByDescending(x => x.N).ThenBy(x => x.Key).Take(10).ToListAsync(ct);
        return rows.Select(r => (r.Key, r.N)).ToList();
    }
}
