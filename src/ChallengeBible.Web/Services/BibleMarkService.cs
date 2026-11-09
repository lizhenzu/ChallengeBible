using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>북마크 모음의 한 줄: 절 위치 · 담은 때 · 개역개정 본문(없으면 null).</summary>
public sealed record BookmarkRow(byte BookId, short Chapter, short Verse, DateTime CreatedAt, string? Text);

/// <summary>한 범위 안에서 내가 읽은 절 · 책갈피한 절(장, 절).</summary>
public sealed record VerseMarks(IReadOnlySet<(short Chapter, short Verse)> Read, IReadOnlySet<(short Chapter, short Verse)> Bookmarked);

/// <summary>
/// 절 읽음 표시 · 책갈피(R-112, 0029). 계정 범위: 이 요청의 로그인 계정 것만 읽고 쓴다(앱 필터 + RLS sec.fn_Account).
/// 토글이 아니라 켜기 · 끄기를 따로 둔다(X-17): 이미 켜진 것을 다시 켜도, 꺼진 것을 다시 꺼도 그대로다.
/// 점수와 무관하다(R-113).
/// </summary>
public sealed class BibleMarkService(IAppDb db, RequestScope scope, TimeProvider time)
{
    private string Me => scope.UserName ?? throw new InvalidOperationException("로그인한 요청에서만 써요.");

    public async Task<VerseMarks> MarksAsync(BibleRef r, CancellationToken ct = default)
    {
        if (scope.UserName is null || !r.IsValid) return new(new HashSet<(short, short)>(), new HashSet<(short, short)>());
        await using var ctx = db.Create();
        var read = await InRange(ctx.ReadingVerses.AsNoTracking(), r).Select(x => new { x.Chapter, x.Verse }).ToListAsync(ct);
        var marks = await InRange(ctx.ReadingBookmarks.AsNoTracking(), r).Select(x => new { x.Chapter, x.Verse }).ToListAsync(ct);
        return new(read.Select(x => (x.Chapter, x.Verse)).ToHashSet(), marks.Select(x => (x.Chapter, x.Verse)).ToHashSet());
    }

    /// <summary>절 하나 읽음. 없는 절이면 false.</summary>
    public async Task<bool> ReadAsync(byte book, short chapter, short verse, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        if (!await Exists(ctx, book, chapter, verse, ct)) return false;
        if (!await ctx.ReadingVerses.AnyAsync(x => x.BookId == book && x.Chapter == chapter && x.Verse == verse, ct))
        {
            ctx.ReadingVerses.Add(new ReadingVerse { UserName = Me, RoundNo = await RoundNoAsync(ctx, ct), BookId = book, Chapter = chapter, Verse = verse, ReadAt = time.GetUtcNow().UtcDateTime });
            await SaveIgnoringDuplicate(ctx, ct);
        }
        return true;
    }

    /// <summary>읽음은 진행 중 회차에 붙는다(0090, DM2_ReadingVerse). 회차가 없으면 1회차를 연다.</summary>
    private async Task<int> RoundNoAsync(AppDbContext ctx, CancellationToken ct)
    {
        var open = await ctx.ReadingRounds.Where(x => x.CompletedAt == null && x.ClosedAt == null).Select(x => (int?)x.RoundNo).FirstOrDefaultAsync(ct);
        if (open is int no) return no;
        var next = (await ctx.ReadingRounds.Select(x => (int?)x.RoundNo).MaxAsync(ct) ?? 0) + 1;
        ctx.ReadingRounds.Add(new ReadingRound { UserName = Me, RoundNo = next, StartedAt = time.GetUtcNow().UtcDateTime });
        await ctx.SaveChangesAsync(ct);
        return next;
    }

    public async Task UnreadAsync(byte book, short chapter, short verse, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var row = await ctx.ReadingVerses.SingleOrDefaultAsync(x => x.BookId == book && x.Chapter == chapter && x.Verse == verse, ct);
        if (row is null) return;
        ctx.ReadingVerses.Remove(row);
        await ctx.SaveChangesAsync(ct);
    }

    /// <summary>범위의 모든 절 읽음(이미 읽은 절은 그대로, 읽은 시각도 그대로). 새로 표시한 절 수를 돌려준다.</summary>
    public async Task<int> ReadRangeAsync(BibleRef r, CancellationToken ct = default)
    {
        if (!r.IsValid) return 0;
        await using var ctx = db.Create();
        var verses = await InRange(ctx.BibleVerses.AsNoTracking().Where(v => v.BookId == r.BookId), r)
            .Select(v => new { v.Chapter, v.Verse }).Distinct().ToListAsync(ct);
        var already = (await InRange(ctx.ReadingVerses.AsNoTracking(), r).Select(x => new { x.Chapter, x.Verse }).ToListAsync(ct)).ToHashSet();
        var now = time.GetUtcNow().UtcDateTime;
        var add = verses.Where(v => !already.Contains(v)).ToList();
        var round = add.Count > 0 ? await RoundNoAsync(ctx, ct) : 0;
        foreach (var v in add) ctx.ReadingVerses.Add(new ReadingVerse { UserName = Me, RoundNo = round, BookId = r.BookId, Chapter = v.Chapter, Verse = v.Verse, ReadAt = now });
        if (add.Count > 0) await SaveIgnoringDuplicate(ctx, ct);
        return add.Count;
    }

    public async Task<int> UnreadRangeAsync(BibleRef r, CancellationToken ct = default)
    {
        if (!r.IsValid) return 0;
        await using var ctx = db.Create();
        var rows = await InRange(ctx.ReadingVerses, r).ToListAsync(ct);
        ctx.ReadingVerses.RemoveRange(rows);
        await ctx.SaveChangesAsync(ct);
        return rows.Count;
    }

    public async Task<bool> BookmarkAsync(byte book, short chapter, short verse, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        if (!await Exists(ctx, book, chapter, verse, ct)) return false;
        if (!await ctx.ReadingBookmarks.AnyAsync(x => x.BookId == book && x.Chapter == chapter && x.Verse == verse, ct))
        {
            ctx.ReadingBookmarks.Add(new ReadingBookmark { UserName = Me, BookId = book, Chapter = chapter, Verse = verse, CreatedAt = time.GetUtcNow().UtcDateTime });
            await SaveIgnoringDuplicate(ctx, ct);
        }
        return true;
    }

    public async Task UnbookmarkAsync(byte book, short chapter, short verse, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var row = await ctx.ReadingBookmarks.SingleOrDefaultAsync(x => x.BookId == book && x.Chapter == chapter && x.Verse == verse, ct);
        if (row is null) return;
        ctx.ReadingBookmarks.Remove(row);
        await ctx.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 내 북마크 모두(0041 북마크 화면 — 0029 책갈피를 그대로 씀). 성경말씀 · 성경통독 어디서 담았든 같이 모인다.
    /// bibleOrder 면 성경 순서, 아니면 최근 담은 순.
    /// </summary>
    public async Task<IReadOnlyList<BookmarkRow>> BookmarksAsync(bool bibleOrder, CancellationToken ct = default)
    {
        if (scope.UserName is null) return [];
        await using var ctx = db.Create();
        var rows = await (from b in ctx.ReadingBookmarks.AsNoTracking()
                          join v in ctx.BibleVerses.AsNoTracking().Where(v => v.TranslationId == 1)
                              on new { b.BookId, b.Chapter, b.Verse } equals new { v.BookId, v.Chapter, v.Verse } into g
                          from v in g.DefaultIfEmpty()
                          select new BookmarkRow(b.BookId, b.Chapter, b.Verse, b.CreatedAt, v == null ? null : v.Text)).ToListAsync(ct);
        return bibleOrder
            ? rows.OrderBy(r => r.BookId).ThenBy(r => r.Chapter).ThenBy(r => r.Verse).ToList()
            : rows.OrderByDescending(r => r.CreatedAt).ToList();
    }

    private static Task<bool> Exists(AppDbContext ctx, byte book, short chapter, short verse, CancellationToken ct) =>
        ctx.BibleVerses.AnyAsync(v => v.BookId == book && v.Chapter == chapter && v.Verse == verse, ct);

    private static IQueryable<ReadingVerse> InRange(IQueryable<ReadingVerse> q, BibleRef r)
    {
        var (sc, sv, ec, ev) = (r.StartChapter, r.StartVerse, r.EndChapter, r.EndVerse);
        return q.Where(x => x.BookId == r.BookId && (x.Chapter > sc || (x.Chapter == sc && x.Verse >= sv)) && (x.Chapter < ec || (x.Chapter == ec && x.Verse <= ev)));
    }

    private static IQueryable<ReadingBookmark> InRange(IQueryable<ReadingBookmark> q, BibleRef r)
    {
        var (sc, sv, ec, ev) = (r.StartChapter, r.StartVerse, r.EndChapter, r.EndVerse);
        return q.Where(x => x.BookId == r.BookId && (x.Chapter > sc || (x.Chapter == sc && x.Verse >= sv)) && (x.Chapter < ec || (x.Chapter == ec && x.Verse <= ev)));
    }

    private static IQueryable<BibleVerse> InRange(IQueryable<BibleVerse> q, BibleRef r)
    {
        var (sc, sv, ec, ev) = (r.StartChapter, r.StartVerse, r.EndChapter, r.EndVerse);
        return q.Where(x => (x.Chapter > sc || (x.Chapter == sc && x.Verse >= sv)) && (x.Chapter < ec || (x.Chapter == ec && x.Verse <= ev)));
    }

    /// <summary>두 번 빨리 눌러 같은 절이 동시에 들어오면 PK 가 막는다 — 결과는 같으니 그대로 둔다(멱등).</summary>
    private static async Task SaveIgnoringDuplicate(AppDbContext ctx, CancellationToken ct)
    {
        try { await ctx.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2627 or 2601 }) { /* 이미 있음 */ }
    }
}
