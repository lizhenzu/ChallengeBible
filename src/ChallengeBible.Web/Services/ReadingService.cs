using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record BookProgress(byte BookId, string Name, string Testament, int Read, int Total)
{
    public bool Done => Read >= Total;
}
public sealed record ResumePoint(byte BookId, short Chapter, short Verse, int VerseCount, int VersesRead, DateTime? ViewedUtc);
public sealed record ReadingSummary(
    int RoundNo, int CompletedRounds, int ReadChapters, int OtRead, int NtRead,
    int TodayChapters, int Streak, int DaysRead, DateOnly Today,
    ResumePoint Resume, IReadOnlyList<(byte BookId, short Chapter)> Next,
    IReadOnlyList<BookProgress> Books, IReadOnlyDictionary<DateOnly, int> Daily)
{
    public static int Total => ReadingService.TotalChapters;
    public static int OtTotal => ReadingService.OtChapters;
    public static int NtTotal => ReadingService.TotalChapters - ReadingService.OtChapters;
    public static double Percent(int read, int total) => total == 0 ? 0 : Math.Floor(read * 1000.0 / total) / 10;   // 소수 첫째 자리(R-155)
}
public sealed record ChapterView(byte BookId, short Chapter, int VerseCount, bool Done, IReadOnlySet<short> VersesRead, int RoundNo);
public sealed record ReadingResult(bool Ok, bool ChapterDone, double Progress, bool RoundCompleted, int? NewRoundNo, string? Error = null);

/// <summary>
/// 도전!!!성경통독(0039 ~ 0042, R-151 ~ R-162). 기록은 계정에 붙고(R-158) 이 요청의 로그인 계정 것만 다룬다(앱 필터 + RLS).
/// 켜기 · 끄기는 멱등(X-17). 점수는 없다(R-157). "오늘"은 접속한 영역의 시간대(R-156).
/// 불변식: 장 완료 ⇔ 그 장의 모든 절이 그 회차에서 읽음. 책 목록에서 장을 바로 완료하면 그 장의 절도 모두 읽음으로 채운다.
/// </summary>
public sealed class ReadingService(IAppDb db, RequestScope scope, TenantContext tenant, TenantClock clock, TimeProvider time, BibleMarkService marks)
{
    public static readonly int TotalChapters = BibleBooks.All().Sum(b => b.ChapterCount);          // 1,189
    public static readonly int OtChapters = BibleBooks.All().Where(b => b.Testament == "OT").Sum(b => b.ChapterCount);   // 929

    private int Me => scope.AccountId ?? throw new InvalidOperationException("로그인한 요청에서만 써요.");
    private DateTime Now => time.GetUtcNow().UtcDateTime;
    private DateOnly Local(DateTime utc) => TenantClock.ToTenantDate(new DateTimeOffset(utc, TimeSpan.Zero), tenant.Current.TimeZoneId);

    /// <summary>진행 중 회차. 없으면 1회차(또는 다음 회차)를 연다(R-153).</summary>
    private async Task<ReadingRound> OpenRoundAsync(AppDbContext ctx, CancellationToken ct)
    {
        var open = await ctx.ReadingRounds.SingleOrDefaultAsync(r => r.CompletedUtc == null && r.ClosedUtc == null, ct);
        if (open is not null) return open;
        var last = await ctx.ReadingRounds.MaxAsync(r => (int?)r.RoundNo, ct) ?? 0;
        var round = new ReadingRound { AccountId = Me, RoundNo = last + 1, StartedUtc = Now };
        ctx.ReadingRounds.Add(round);
        await ctx.SaveChangesAsync(ct);
        return round;
    }

    private static Task<short> VerseCountAsync(AppDbContext ctx, byte book, short chapter, CancellationToken ct) =>
        ctx.BibleChapters.Where(c => c.BookId == book && c.Chapter == chapter).Select(c => c.VerseCount).SingleOrDefaultAsync(ct);

    private async Task<double> ProgressAsync(AppDbContext ctx, int roundNo, CancellationToken ct) =>
        ReadingSummary.Percent(await ctx.ChapterReads.CountAsync(c => c.RoundNo == roundNo, ct), TotalChapters);

    /// <summary>장 완료(R-151). 그 장의 절도 회차 읽음 · 성경 읽음 표시(R-152)로 채운다. 1,189장이면 회차 완료(R-153).</summary>
    public async Task<ReadingResult> CompleteChapterAsync(byte book, short chapter, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var count = await VerseCountAsync(ctx, book, chapter, ct);
        if (count == 0) return new(false, false, 0, false, null, "없는 장이에요.");
        var round = await OpenRoundAsync(ctx, ct);
        var have = (await ctx.RoundVerseReads.Where(v => v.RoundNo == round.RoundNo && v.BookId == book && v.Chapter == chapter).Select(v => v.Verse).ToListAsync(ct)).ToHashSet();
        var now = Now;
        for (short v = 1; v <= count; v++)
            if (!have.Contains(v)) ctx.RoundVerseReads.Add(new RoundVerseRead { AccountId = Me, RoundNo = round.RoundNo, BookId = book, Chapter = chapter, Verse = v, ReadUtc = now });
        var (completed, next) = await MarkChapterDoneAsync(ctx, round, book, chapter, ct);
        await ctx.SaveChangesAsync(ct);
        await marks.ReadRangeAsync(new BibleRef(book, chapter, 1, chapter, count), ct);
        return new(true, true, completed ? 0 : await ProgressAsync(ctx, round.RoundNo, ct), completed, next);
    }

    /// <summary>장 완료 해제: 그 회차의 장 완료와 절 읽음을 지운다. 성경 읽음 표시(0029)는 그대로(R-152 해제 연동 없음).</summary>
    public async Task<ReadingResult> UndoChapterAsync(byte book, short chapter, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var round = await OpenRoundAsync(ctx, ct);
        ctx.ChapterReads.RemoveRange(await ctx.ChapterReads.Where(c => c.RoundNo == round.RoundNo && c.BookId == book && c.Chapter == chapter).ToListAsync(ct));
        ctx.RoundVerseReads.RemoveRange(await ctx.RoundVerseReads.Where(v => v.RoundNo == round.RoundNo && v.BookId == book && v.Chapter == chapter).ToListAsync(ct));
        await ctx.SaveChangesAsync(ct);
        return new(true, false, await ProgressAsync(ctx, round.RoundNo, ct), false, null);
    }

    /// <summary>절 읽음 켜기 · 끄기(R-160). 켜면 성경 읽음 표시에도 남기고(R-152), 장의 절이 다 차면 장 자동 완료(R-162).</summary>
    public async Task<ReadingResult> SetVersesAsync(byte book, short chapter, short from, short to, bool on, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var count = await VerseCountAsync(ctx, book, chapter, ct);
        if (count == 0 || from < 1 || to < from || to > count) return new(false, false, 0, false, null, "없는 절이에요.");
        var round = await OpenRoundAsync(ctx, ct);
        var rows = await ctx.RoundVerseReads.Where(v => v.RoundNo == round.RoundNo && v.BookId == book && v.Chapter == chapter).ToListAsync(ct);
        var have = rows.Select(v => v.Verse).ToHashSet();
        var completed = false; int? next = null;
        if (on)
        {
            var now = Now;
            for (var v = from; v <= to; v++)
                if (!have.Contains(v)) { ctx.RoundVerseReads.Add(new RoundVerseRead { AccountId = Me, RoundNo = round.RoundNo, BookId = book, Chapter = chapter, Verse = v, ReadUtc = now }); have.Add(v); }
            if (have.Count >= count) (completed, next) = await MarkChapterDoneAsync(ctx, round, book, chapter, ct);
        }
        else
        {
            ctx.RoundVerseReads.RemoveRange(rows.Where(v => v.Verse >= from && v.Verse <= to));
            for (var v = from; v <= to; v++) have.Remove(v);
            // 불변식: 한 절이라도 빠지면 장 완료도 풀린다
            ctx.ChapterReads.RemoveRange(await ctx.ChapterReads.Where(c => c.RoundNo == round.RoundNo && c.BookId == book && c.Chapter == chapter).ToListAsync(ct));
        }
        await ctx.SaveChangesAsync(ct);
        if (on) await marks.ReadRangeAsync(new BibleRef(book, chapter, from, chapter, to), ct);
        var done = on && have.Count >= count;
        return new(true, done, completed ? 0 : await ProgressAsync(ctx, round.RoundNo, ct), completed, next);
    }

    /// <summary>장 완료를 넣는다(이미 있으면 그대로). 1,189장이 되면 회차 완료 + 다음 회차 시작(R-153).</summary>
    private async Task<(bool Completed, int? NextRound)> MarkChapterDoneAsync(AppDbContext ctx, ReadingRound round, byte book, short chapter, CancellationToken ct)
    {
        var exists = await ctx.ChapterReads.AnyAsync(c => c.RoundNo == round.RoundNo && c.BookId == book && c.Chapter == chapter, ct);
        if (exists) return (false, null);
        ctx.ChapterReads.Add(new ChapterRead { AccountId = Me, RoundNo = round.RoundNo, BookId = book, Chapter = chapter, ReadUtc = Now });
        var done = await ctx.ChapterReads.CountAsync(c => c.RoundNo == round.RoundNo, ct) + 1;
        if (done < TotalChapters) return (false, null);
        round.CompletedUtc = Now;
        await ctx.SaveChangesAsync(ct);   // 진행 중 회차는 하나(UX_ReadingRound_Open): 먼저 닫고 새로 연다
        var next = new ReadingRound { AccountId = Me, RoundNo = round.RoundNo + 1, StartedUtc = Now };
        ctx.ReadingRounds.Add(next);
        return (true, next.RoundNo);
    }

    /// <summary>새 회차 시작(R-154): 진행 중 회차를 닫고(미완료) 다음 회차를 연다. 기록은 남는다.</summary>
    public async Task<int> StartNewRoundAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var round = await OpenRoundAsync(ctx, ct);
        round.ClosedUtc = Now;
        await ctx.SaveChangesAsync(ct);
        var next = new ReadingRound { AccountId = Me, RoundNo = round.RoundNo + 1, StartedUtc = Now };
        ctx.ReadingRounds.Add(next);
        await ctx.SaveChangesAsync(ct);
        return next.RoundNo;
    }

    /// <summary>보던 위치(R-161) — 읽음과 무관.</summary>
    public async Task<bool> SavePositionAsync(byte book, short chapter, short verse, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var count = await VerseCountAsync(ctx, book, chapter, ct);
        if (count == 0 || verse < 1 || verse > count) return false;
        var round = await OpenRoundAsync(ctx, ct);
        var row = await ctx.ChapterProgresses.SingleOrDefaultAsync(p => p.RoundNo == round.RoundNo && p.BookId == book && p.Chapter == chapter, ct);
        if (row is null) ctx.ChapterProgresses.Add(new ChapterProgress { AccountId = Me, RoundNo = round.RoundNo, BookId = book, Chapter = chapter, LastViewedVerse = verse, UpdatedUtc = Now });
        else { row.LastViewedVerse = verse; row.UpdatedUtc = Now; }
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ChapterView?> ChapterAsync(byte book, short chapter, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var count = await VerseCountAsync(ctx, book, chapter, ct);
        if (count == 0) return null;
        var round = await OpenRoundAsync(ctx, ct);
        var read = (await ctx.RoundVerseReads.Where(v => v.RoundNo == round.RoundNo && v.BookId == book && v.Chapter == chapter).Select(v => v.Verse).ToListAsync(ct)).ToHashSet();
        var done = await ctx.ChapterReads.AnyAsync(c => c.RoundNo == round.RoundNo && c.BookId == book && c.Chapter == chapter, ct);
        return new(book, chapter, count, done, read, round.RoundNo);
    }

    /// <summary>현재 회차에서 끝낸 장(책 → 장 번호들). 책 목록 · 장 번호 줄에 쓴다.</summary>
    public async Task<IReadOnlyDictionary<byte, IReadOnlySet<short>>> DoneChaptersAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var round = await OpenRoundAsync(ctx, ct);
        var rows = await ctx.ChapterReads.Where(c => c.RoundNo == round.RoundNo).Select(c => new { c.BookId, c.Chapter }).ToListAsync(ct);
        return rows.GroupBy(r => r.BookId).ToDictionary(g => g.Key, g => (IReadOnlySet<short>)g.Select(x => x.Chapter).ToHashSet());
    }

    /// <summary>대시보드 요약(R-155 · R-156 · R-161).</summary>
    /// <summary>통독을 시작했는지(회차가 하나라도 있는지). 부서 홈은 이것으로 먼저 보고 시작한 사람만 요약을 읽는다(요약은 처음이면 1회차를 연다, R-153).</summary>
    public async Task<bool> StartedAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return await ctx.ReadingRounds.AnyAsync(ct);
    }

    public async Task<ReadingSummary> SummaryAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var round = await OpenRoundAsync(ctx, ct);
        var completedRounds = await ctx.ReadingRounds.CountAsync(r => r.CompletedUtc != null, ct);
        var inRound = await ctx.ChapterReads.Where(c => c.RoundNo == round.RoundNo).Select(c => new { c.BookId, c.Chapter, c.ReadUtc }).ToListAsync(ct);
        var allDays = (await ctx.ChapterReads.Select(c => c.ReadUtc).ToListAsync(ct)).Select(Local).ToList();
        var today = clock.Today(tenant.Current.TimeZoneId);

        var dayCounts = allDays.GroupBy(d => d).ToDictionary(g => g.Key, g => g.Count());
        var streakEnd = dayCounts.ContainsKey(today) ? today : today.AddDays(-1);   // 오늘 아직 안 읽었어도 어제까지 이어졌으면 끊기지 않음
        var streak = 0;
        for (var d = streakEnd; dayCounts.ContainsKey(d); d = d.AddDays(-1)) streak++;
        var from = today.AddDays(-(int)today.DayOfWeek - 49);   // 최근 8주(일요일 시작)
        var daily = dayCounts.Where(kv => kv.Key >= from && kv.Key <= today).ToDictionary(kv => kv.Key, kv => kv.Value);

        var books = BibleBooks.All().Select(b => new BookProgress(b.Id, b.Name, b.Testament, inRound.Count(c => c.BookId == b.Id), b.ChapterCount)).ToList();
        var done = inRound.Select(c => (c.BookId, c.Chapter)).ToHashSet();

        // 이어 읽기(R-161): 이 회차에서 마지막으로 보던 곳, 없으면 마지막으로 끝낸 장의 다음 장, 그것도 없으면 창세기 1장
        var pos = await ctx.ChapterProgresses.Where(p => p.RoundNo == round.RoundNo).OrderByDescending(p => p.UpdatedUtc).FirstOrDefaultAsync(ct);
        (byte Book, short Chapter, short Verse, DateTime? At) at = pos is not null
            ? (pos.BookId, pos.Chapter, pos.LastViewedVerse, pos.UpdatedUtc)
            : inRound.OrderByDescending(c => c.ReadUtc).Select(c => NextChapter(c.BookId, c.Chapter)).Select(n => (n.Book, n.Chapter, (short)1, (DateTime?)null)).FirstOrDefault();
        if (at.Book == 0) at = (1, 1, 1, null);
        var count = await VerseCountAsync(ctx, at.Book, at.Chapter, ct);
        var versesRead = await ctx.RoundVerseReads.CountAsync(v => v.RoundNo == round.RoundNo && v.BookId == at.Book && v.Chapter == at.Chapter, ct);
        var resume = new ResumePoint(at.Book, at.Chapter, at.Verse, count, versesRead, at.At);

        // 다음에 읽을 곳: 읽는 중인 책마다 아직 안 읽은 첫 장(최근에 읽은 책 순)
        var next = inRound.GroupBy(c => c.BookId).OrderByDescending(g => g.Max(c => c.ReadUtc))
            .Select(g => (Book: g.Key, Chapter: FirstUnread(g.Key, done)))
            .Where(x => x.Chapter > 0).Take(6).Select(x => (x.Book, x.Chapter)).ToList();
        if (next.Count == 0) next.Add((1, 1));

        return new(round.RoundNo, completedRounds, inRound.Count, inRound.Count(c => c.BookId <= 39), inRound.Count(c => c.BookId >= 40),
            dayCounts.GetValueOrDefault(today), streak, dayCounts.Count, today, resume, next, books, daily);
    }

    private static short FirstUnread(byte book, IReadOnlySet<(byte, short)> done)
    {
        var max = BibleBooks.All().First(b => b.Id == book).ChapterCount;
        for (short c = 1; c <= max; c++) if (!done.Contains((book, c))) return c;
        return 0;
    }

    /// <summary>다음 장(책 끝이면 다음 책 1장, 요한계시록 끝이면 창세기 1장).</summary>
    public static (byte Book, short Chapter) NextChapter(byte book, short chapter)
    {
        var max = BibleBooks.All().First(b => b.Id == book).ChapterCount;
        if (chapter < max) return (book, (short)(chapter + 1));
        return book < 66 ? ((byte)(book + 1), (short)1) : ((byte)1, (short)1);
    }

    public static (byte Book, short Chapter)? PrevChapter(byte book, short chapter)
    {
        if (chapter > 1) return (book, (short)(chapter - 1));
        if (book == 1) return null;
        var prev = BibleBooks.All().First(b => b.Id == book - 1);
        return (prev.Id, prev.ChapterCount);
    }
}
