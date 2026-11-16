using System.Text;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ChallengeBible.Web.Services;

/// <summary>책 하나의 필사 진도: 절 수 · 쓴 절 수 · 다 쓴 장 수.</summary>
public sealed record CopyBook(byte BookId, string Name, bool New, int Chapters, int Verses, int Done, int DoneChapters)
{
    public double Percent => CopySummary.Percent(Done, Verses);
}

/// <summary>최근 쓴 장: 그 장의 쓴 절 수 · 전체 절 수 · 마지막으로 쓴 날(부서 날짜).</summary>
public sealed record CopyRecent(byte BookId, short Chapter, int Done, int Verses, DateOnly On)
{
    public string Name => BibleBooks.NameOf(BookId);
}

/// <summary>필사 홈(확정 시안 성경필사 ①): 전체 · 구약 · 신약 절 수와 쓴 수, 이어 쓸 곳, 최근 쓴 장.</summary>
public sealed record CopySummary(int Total, int OldTotal, int NewTotal, int Done, int OldDone, int NewDone, CopySpot Resume, IReadOnlyList<CopyRecent> Recent, int TodayDone = 0,
    IReadOnlyDictionary<DateOnly, int>? Daily = null, DateOnly Today = default, int Streak = 0, int Days = 0)
{
    public static double Percent(int done, int total) => total <= 0 ? 0 : Math.Floor(done * 1000.0 / total) / 10;
    public double All => Percent(Done, Total);
    public double Old => Percent(OldDone, OldTotal);
    public double New => Percent(NewDone, NewTotal);
}

/// <summary>절 하나의 자리. Done = 이미 쓴 절.</summary>
public sealed record CopySpot(byte BookId, short Chapter, short Verse)
{
    public string Name => BibleBooks.NameOf(BookId);
}

/// <summary>따라 치기 한 장: 절마다 번역별 본문 · 쓴 절 · 처음 보여 줄 절.</summary>
public sealed record CopyChapter(byte BookId, short Chapter, int ChapterCount, IReadOnlyList<VerseLine> Lines, IReadOnlySet<short> Done, short Start)
{
    public string Name => BibleBooks.NameOf(BookId);
}

public enum CopySaveError { None, NoVerse, Mismatch, BadTranslation }

/// <summary>절 쓰기 결과: 그 장 · 전체 진도와, 완필이 막 이루어졌으면 그 성경(신약 · 구약).</summary>
public sealed record CopySaveResult(CopySaveError Error, int ChapterDone, int ChapterVerses, bool NewlyDone);

/// <summary>참여자 현황 한 줄.</summary>
public sealed record CopyBoardRow(string UserName, string Name, int Done, int NewDone, int OldDone, DateOnly? LastOn, string? LastPlace, bool Me);

/// <summary>
/// 도전!!!성경필사(ADR 0099, R-305 ~ R-308): 본문을 자판으로 따라 쳐서 한 절씩 채운다. 글자만 맞으면 된다(띄어쓰기 · 문장 부호 · 대소문자 무시, R-306).
/// 같은 절은 번역이 달라도 한 번(계정 범위 표 CopyVerse — 통독처럼 부서를 옮겨도 따라가고, 새해에도 지우지 않는다). 절 수 기준은 BibleChapters(개역개정 절 수).
/// </summary>
public sealed class CopyService(IAppDb db, RequestScope scope, TenantContext tenant, TenantClock clock, TimeProvider time, IMemoryCache cache)
{
    private string Me => scope.UserName ?? throw new InvalidOperationException("로그인한 요청에서만 써요.");
    private DateOnly Local(DateTime utc) => TenantClock.ToTenantDate(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)), tenant.Current.TimeZoneId);

    /// <summary>글자만 남긴다(R-306): 글자 · 숫자만, 소문자로. 띄어쓰기 · 문장 부호 · 따옴표는 뺀다.</summary>
    public static string Letters(string? text)
    {
        var sb = new StringBuilder((text ?? "").Length);
        foreach (var c in (text ?? "").Normalize(NormalizationForm.FormC))
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>장마다 절 수(BibleChapters). 참조 데이터라 한 번 읽어 둔다.</summary>
    public async Task<IReadOnlyDictionary<(byte Book, short Chapter), short>> CountsAsync(CancellationToken ct = default) =>
        (await cache.GetOrCreateAsync("copy:verse-counts", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);
            await using var ctx = db.Create();
            var rows = await ctx.BibleChapters.AsNoTracking().Select(c => new { c.BookId, c.Chapter, c.VerseCount }).ToListAsync(ct);
            return (IReadOnlyDictionary<(byte, short), short>)rows.ToDictionary(r => (r.BookId, r.Chapter), r => r.VerseCount);
        }))!;

    public static bool IsNew(byte book) => book >= 40;

    private async Task<List<(byte BookId, short Chapter, short Verse, DateTime At)>> MineAsync(AppDbContext ctx, CancellationToken ct) =>
        (await ctx.CopyVerses.AsNoTracking().Select(v => new { v.BookId, v.Chapter, v.Verse, v.WrittenAt }).ToListAsync(ct))
            .Select(v => (v.BookId, v.Chapter, v.Verse, v.WrittenAt)).ToList();

    /// <summary>다음에 쓸 절: 마지막으로 쓴 절의 그 장에서 아직 안 쓴 첫 절 → 없으면 다음 장들 → 처음(창세기 1장 1절)부터.</summary>
    public static CopySpot NextSpot(IReadOnlyDictionary<(byte, short), short> counts, IReadOnlySet<(byte, short, short)> done, (byte Book, short Chapter)? from)
    {
        var order = counts.Keys.OrderBy(k => k.Item1).ThenBy(k => k.Item2).ToList();
        if (order.Count == 0) return new(1, 1, 1);
        var start = from is { } f ? Math.Max(0, order.IndexOf((f.Book, f.Chapter))) : 0;
        for (var i = 0; i < order.Count; i++)
        {
            var (b, c) = order[(start + i) % order.Count];
            for (short v = 1; v <= counts[(b, c)]; v++)
                if (!done.Contains((b, c, v))) return new(b, c, v);
        }
        return new(order[start].Item1, order[start].Item2, 1);   // 다 썼다
    }

    public async Task<CopySummary> SummaryAsync(CancellationToken ct = default)
    {
        var counts = await CountsAsync(ct);
        await using var ctx = db.Create();
        var mine = await MineAsync(ctx, ct);
        var valid = mine.Where(v => counts.TryGetValue((v.BookId, v.Chapter), out var n) && v.Verse <= n).ToList();
        var done = valid.Select(v => (v.BookId, v.Chapter, v.Verse)).ToHashSet();
        var last = valid.OrderByDescending(v => v.At).FirstOrDefault();
        var resume = NextSpot(counts, done, valid.Count == 0 ? null : (last.BookId, last.Chapter));
        var recent = valid.GroupBy(v => (v.BookId, v.Chapter)).Select(g => new CopyRecent(g.Key.BookId, g.Key.Chapter, g.Count(), counts[g.Key], Local(g.Max(x => x.At))))
            .OrderByDescending(r => valid.Where(v => v.BookId == r.BookId && v.Chapter == r.Chapter).Max(v => v.At)).Take(5).ToList();
        int Sum(bool isNew) => counts.Where(k => IsNew(k.Key.Book) == isNew).Sum(k => k.Value);
        int oldTotal = Sum(false), newTotal = Sum(true);
        int newDone = valid.Count(v => IsNew(v.BookId));
        var today = clock.Today(tenant.Current.TimeZoneId);
        var todayDone = valid.Count(v => Local(v.At) == today);   // 부서 홈 카드 "오늘 N절"(확정 시안 홈-필사카드)
        // 대시보드(확정 시안 필사-통독틀, ADR 0105): 날마다 쓴 절 · 이어 쓴 날 · 쓴 날 수 — 성경통독 대시보드와 같은 셈
        var daily = valid.GroupBy(v => Local(v.At)).ToDictionary(g => g.Key, g => g.Count());
        var streak = 0;
        for (var d = daily.ContainsKey(today) ? today : today.AddDays(-1); daily.ContainsKey(d); d = d.AddDays(-1)) streak++;
        return new(oldTotal + newTotal, oldTotal, newTotal, valid.Count, valid.Count - newDone, newDone, resume, recent, todayDone, daily, today, streak, daily.Count);
    }

    /// <summary>장 고르기(확정 시안 ③): 책마다 진도와, 장마다 쓴 절 수.</summary>
    public async Task<(IReadOnlyList<CopyBook> Books, IReadOnlyDictionary<(byte, short), int> ChapterDone)> BooksAsync(CancellationToken ct = default)
    {
        var counts = await CountsAsync(ct);
        await using var ctx = db.Create();
        var mine = await MineAsync(ctx, ct);
        var byChapter = mine.Where(v => counts.TryGetValue((v.BookId, v.Chapter), out var n) && v.Verse <= n)
            .GroupBy(v => (v.BookId, v.Chapter)).ToDictionary(g => g.Key, g => g.Count());
        var books = BibleBooks.All().Select(b =>
        {
            var chapters = counts.Keys.Where(k => k.Book == b.Id).ToList();
            return new CopyBook(b.Id, b.Name, IsNew(b.Id), chapters.Count, chapters.Sum(k => counts[k]),
                chapters.Sum(k => byChapter.GetValueOrDefault(k)), chapters.Count(k => byChapter.GetValueOrDefault(k) >= counts[k]));
        }).Where(b => b.Chapters > 0).ToList();
        return (books, byChapter.ToDictionary(k => ((byte, short))k.Key, k => k.Value));
    }

    /// <summary>따라 치기 한 장(확정 시안 ②): 세 번역 본문, 이미 쓴 절, 처음 보여 줄 절(v 가 있으면 그 절, 없으면 안 쓴 첫 절).</summary>
    public async Task<CopyChapter?> ChapterAsync(BibleTextService bible, byte book, short chapter, short? verse, CancellationToken ct = default)
    {
        var counts = await CountsAsync(ct);
        if (!counts.TryGetValue((book, chapter), out var n) || n == 0) return null;
        var lines = await bible.RangeAsync(new BibleRef(book, chapter, 1, chapter, n), BibleTextService.Codes, ct);
        await using var ctx = db.Create();
        var done = (await ctx.CopyVerses.AsNoTracking().Where(v => v.BookId == book && v.Chapter == chapter).Select(v => v.Verse).ToListAsync(ct)).ToHashSet();
        short start = verse is short v && v >= 1 && v <= n ? v
            : lines.Select(l => l.Verse).FirstOrDefault(x => !done.Contains(x)) is short f && f > 0 ? f : (short)1;
        return new(book, chapter, counts.Keys.Count(k => k.Book == book), lines, done, start);
    }

    /// <summary>
    /// 절 하나 쓰기(R-305 · R-306): 쓴 글이 그 번역 본문과 글자만 같으면 저장한다(이미 쓴 절이면 번역 · 시각만 고친다).
    /// NewlyDone = 이 절로 신약 또는 구약 전체를 막 다 썼다(완필 선물은 CopyGiftService 가 만든다, R-307).
    /// </summary>
    public async Task<CopySaveResult> SaveAsync(BibleTextService bible, byte book, short chapter, short verse, string translation, string? typed, CancellationToken ct = default)
    {
        var code = BibleTextService.Codes.FirstOrDefault(c => string.Equals(c, translation, StringComparison.OrdinalIgnoreCase));
        if (code is null) return new(CopySaveError.BadTranslation, 0, 0, false);
        var counts = await CountsAsync(ct);
        if (!counts.TryGetValue((book, chapter), out var n) || verse < 1 || verse > n) return new(CopySaveError.NoVerse, 0, 0, false);
        var line = (await bible.RangeAsync(new BibleRef(book, chapter, verse, chapter, verse), [code], ct)).FirstOrDefault();
        if (line is null || !line.Texts.TryGetValue(code, out var text)) return new(CopySaveError.NoVerse, 0, 0, false);
        if (Letters(text) != Letters(typed)) return new(CopySaveError.Mismatch, 0, 0, false);

        await using var ctx = db.Create();
        var now = time.GetUtcNow().UtcDateTime;
        var row = await ctx.CopyVerses.SingleOrDefaultAsync(v => v.BookId == book && v.Chapter == chapter && v.Verse == verse, ct);
        var isNew = row is null;
        if (row is null) ctx.CopyVerses.Add(new CopyVerse { UserName = Me, BookId = book, Chapter = chapter, Verse = verse, Translation = code, WrittenAt = now });
        else { row.Translation = code; row.WrittenAt = now; }
        await ctx.SaveChangesAsync(ct);

        var mine = await MineAsync(ctx, ct);
        var chapterDone = mine.Count(v => v.BookId == book && v.Chapter == chapter && v.Verse <= n);
        var testament = IsNew(book);
        var total = counts.Where(k => IsNew(k.Key.Book) == testament).Sum(k => k.Value);
        var doneInTestament = mine.Count(v => IsNew(v.BookId) == testament && counts.TryGetValue((v.BookId, v.Chapter), out var m) && v.Verse <= m);
        return new(CopySaveError.None, chapterDone, n, isNew && doneInTestament == total);
    }

    /// <summary>참여자 현황(확정 시안 ①의 링크): 이 부서 승인 소속원의 필사 진도. 임시 부서에서는 부르지 않는다.</summary>
    public async Task<IReadOnlyList<CopyBoardRow>> BoardAsync(CancellationToken ct = default)
    {
        var counts = await CountsAsync(ct);
        await using var ctx = db.Create();
        var members = await DeptMembership.PeopleAsync(ctx, ct);
        var ids = members.Select(m => m.UserName).ToList();
        // 필사 표는 계정 범위라 전역 필터가 "본인만"이다. 이 부서 승인 소속원으로 직접 거른다(통독 참여자 현황과 같다)
        var rows = await ctx.CopyVerses.IgnoreQueryFilters().AsNoTracking().Where(v => ids.Contains(v.UserName))
            .Select(v => new { v.UserName, v.BookId, v.Chapter, v.Verse, v.WrittenAt }).ToListAsync(ct);
        var me = scope.UserName;
        return members.Select(m =>
        {
            var mine = rows.Where(r => r.UserName == m.UserName && counts.TryGetValue((r.BookId, r.Chapter), out var n) && r.Verse <= n).ToList();
            var last = mine.OrderByDescending(r => r.WrittenAt).FirstOrDefault();
            return new CopyBoardRow(m.UserName, m.Name, mine.Count, mine.Count(r => IsNew(r.BookId)), mine.Count(r => !IsNew(r.BookId)),
                last is null ? null : Local(last.WrittenAt), last is null ? null : $"{BibleBooks.NameOf(last.BookId)} {last.Chapter}장", m.UserName == me);
        }).OrderByDescending(r => r.Done).ThenBy(r => r.Name).ToList();
    }
}
