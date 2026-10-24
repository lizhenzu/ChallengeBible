using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;

namespace ChallengeBible.Tests.Web;

/// <summary>도전!!!성경통독 규칙(R-151 ~ R-162). 오늘 = 2027-01-05 (서울). 장마다 절 수는 시험용으로 3절.</summary>
public class ReadingServiceTests
{
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly Tenant _kids;
    private readonly int _me, _other;

    public ReadingServiceTests()
    {
        _kids = InMemoryDb.SeedTenant(_db, "jamsil-kids", "JSKIDS");
        _me = InMemoryDb.SeedAccount(_db, "cheolsu@example.com", "김철수").Id;
        _other = InMemoryDb.SeedAccount(_db, "younghee@example.com", "김영희").Id;
        using var raw = InMemoryDb.Raw(_db);
        foreach (var b in BibleBooks.All())
            for (short c = 1; c <= b.ChapterCount; c++) raw.BibleChapters.Add(new BibleChapter { BookId = b.Id, Chapter = c, VerseCount = 3 });
        for (short v = 1; v <= 3; v++) raw.BibleVerses.Add(new BibleVerse { TranslationId = 1, BookId = 1, Chapter = 24, Verse = v, Text = $"KRV 1:24:{v}" });
        raw.SaveChanges();
    }

    private ReadingService As(int accountId)
    {
        var scope = new RequestScope();
        scope.SetTenant(_kids.Id);
        scope.SetAccount(accountId);
        var tc = new TenantContext();
        tc.Set(_kids, null);
        var db = new InMemoryDb(_db, scope);
        return new ReadingService(db, scope, tc, new TenantClock(_time), _time, new BibleMarkService(db, scope, _time));
    }

    [Fact]
    public async Task R153_처음_쓰면_1회차가_열린다()
    {
        var s = await As(_me).SummaryAsync();
        Assert.Equal(1, s.RoundNo);
        Assert.Equal(0, s.CompletedRounds);
        Assert.Equal(0, s.ReadChapters);
        Assert.Equal((byte)1, s.Resume.BookId);   // 처음이면 창세기 1장부터
        Assert.Equal(1189, ReadingSummary.Total);
        Assert.Equal(929, ReadingSummary.OtTotal);
    }

    [Fact]
    public async Task R151_장_완료는_두_번_눌러도_하나이고_해제할_수_있다()
    {
        var a = await As(_me).CompleteChapterAsync(1, 24);
        var b = await As(_me).CompleteChapterAsync(1, 24);
        Assert.True(a.ChapterDone && b.ChapterDone);
        Assert.Single(InMemoryDb.Raw(_db).ChapterReads);
        var view = (await As(_me).ChapterAsync(1, 24))!;
        Assert.True(view.Done);
        Assert.Equal(3, view.VersesRead.Count);               // 장을 끝내면 그 장의 절도 모두 읽음(불변식)
        await As(_me).UndoChapterAsync(1, 24);
        await As(_me).UndoChapterAsync(1, 24);
        Assert.Empty(InMemoryDb.Raw(_db).ChapterReads);
        Assert.Empty(InMemoryDb.Raw(_db).RoundVerseReads);
    }

    [Fact]
    public async Task R152_장을_끝내면_성경_읽음_표시에도_남고_해제는_연동하지_않는다()
    {
        await As(_me).CompleteChapterAsync(1, 24);
        Assert.Equal(3, InMemoryDb.Raw(_db).VerseReadMarks.Count(m => m.AccountId == _me));
        await As(_me).UndoChapterAsync(1, 24);
        Assert.Equal(3, InMemoryDb.Raw(_db).VerseReadMarks.Count(m => m.AccountId == _me));
    }

    [Fact]
    public async Task R160_R162_절을_다_읽으면_장이_자동_완료되고_한_절을_빼면_풀린다()
    {
        var r1 = await As(_me).SetVersesAsync(1, 24, 1, 2, on: true);   // 이 페이지 모두 읽음(1~2절)
        Assert.False(r1.ChapterDone);
        Assert.Empty(InMemoryDb.Raw(_db).ChapterReads);
        var r2 = await As(_me).SetVersesAsync(1, 24, 3, 3, on: true);
        Assert.True(r2.ChapterDone);
        Assert.Single(InMemoryDb.Raw(_db).ChapterReads);
        Assert.Equal(0.0, r2.Progress, 1);                               // 1 / 1189 = 0.08% → 소수 첫째 자리 0.0
        await As(_me).SetVersesAsync(1, 24, 2, 2, on: false);
        Assert.Empty(InMemoryDb.Raw(_db).ChapterReads);
        Assert.False((await As(_me).ChapterAsync(1, 24))!.Done);
    }

    [Fact]
    public async Task R160_없는_절은_표시하지_않는다()
    {
        var r = await As(_me).SetVersesAsync(1, 24, 2, 9, on: true);
        Assert.False(r.Ok);
        Assert.False((await As(_me).CompleteChapterAsync(1, 99)).Ok);
        Assert.Empty(InMemoryDb.Raw(_db).RoundVerseReads);
    }

    [Fact]
    public async Task R153_1189장을_다_읽으면_회차가_완료되고_다음_회차가_열린다()
    {
        await As(_me).SummaryAsync();   // 1회차 열기
        using (var raw = InMemoryDb.Raw(_db))
        {
            foreach (var b in BibleBooks.All())
                for (short c = 1; c <= b.ChapterCount; c++)
                    if (!(b.Id == 66 && c == 22)) raw.ChapterReads.Add(new ChapterRead { AccountId = _me, RoundNo = 1, BookId = b.Id, Chapter = c, ReadUtc = DateTime.UtcNow });
            raw.SaveChanges();
        }
        var r = await As(_me).CompleteChapterAsync(66, 22);   // 요한계시록 22장이 마지막
        Assert.True(r.RoundCompleted);
        Assert.Equal(2, r.NewRoundNo);
        var s = await As(_me).SummaryAsync();
        Assert.Equal(2, s.RoundNo);
        Assert.Equal(1, s.CompletedRounds);
        Assert.Equal(0, s.ReadChapters);
    }

    [Fact]
    public async Task R154_새_회차를_시작하면_이전_회차는_미완료로_닫히고_기록은_남는다()
    {
        await As(_me).CompleteChapterAsync(1, 1);
        Assert.Equal(2, await As(_me).StartNewRoundAsync());
        using var raw = InMemoryDb.Raw(_db);
        var first = raw.ReadingRounds.Single(r => r.RoundNo == 1);
        Assert.NotNull(first.ClosedUtc);
        Assert.Null(first.CompletedUtc);
        Assert.Single(raw.ChapterReads, c => c.RoundNo == 1);   // 기록은 남는다
        Assert.Equal(0, (await As(_me).SummaryAsync()).CompletedRounds);
    }

    [Fact]
    public async Task R155_진도율은_소수_첫째_자리까지_구약_신약을_나눠_센다()
    {
        Assert.Equal(1.9, ReadingSummary.Percent(23, 1189));   // 1.934… → 1.9 (버림)
        for (short c = 1; c <= 23; c++) await As(_me).CompleteChapterAsync(1, c);
        await As(_me).CompleteChapterAsync(40, 1);
        var s = await As(_me).SummaryAsync();
        Assert.Equal(24, s.ReadChapters);
        Assert.Equal(23, s.OtRead);
        Assert.Equal(1, s.NtRead);
        Assert.Equal(23, s.Books.Single(b => b.BookId == 1).Read);
    }

    [Fact]
    public async Task R156_오늘_읽은_장과_연속_읽은_날()
    {
        // 서울 날짜: 1/3, 1/4, 1/5(오늘) 연속, 1/1 은 떨어져 있음
        foreach (var (day, ch) in new[] { (1, 1), (3, 2), (4, 3), (5, 4), (5, 5) })
        {
            _time.Now = new DateTimeOffset(2027, 1, day, 0, 0, 0, TimeSpan.Zero);   // UTC 0시 = 서울 9시
            await As(_me).CompleteChapterAsync(1, (short)ch);
        }
        var s = await As(_me).SummaryAsync();
        Assert.Equal(2, s.TodayChapters);
        Assert.Equal(3, s.Streak);
        Assert.Equal(4, s.DaysRead);
        _time.Now = new DateTimeOffset(2027, 1, 6, 0, 0, 0, TimeSpan.Zero);          // 다음 날 아직 안 읽음 → 어제까지 이어진 3일
        Assert.Equal(3, (await As(_me).SummaryAsync()).Streak);
    }

    [Fact]
    public async Task R161_이어_읽기는_보던_곳_없으면_마지막으로_끝낸_장의_다음_장()
    {
        await As(_me).CompleteChapterAsync(1, 50);                       // 창세기 끝 → 출애굽기 1장
        var s1 = await As(_me).SummaryAsync();
        Assert.Equal(((byte)2, (short)1), (s1.Resume.BookId, s1.Resume.Chapter));
        Assert.True(await As(_me).SavePositionAsync(1, 24, 3));          // 보던 위치는 읽음과 무관
        var s2 = await As(_me).SummaryAsync();
        Assert.Equal(((byte)1, (short)24, (short)3), (s2.Resume.BookId, s2.Resume.Chapter, s2.Resume.Verse));
        Assert.Empty(InMemoryDb.Raw(_db).RoundVerseReads.Where(v => v.Chapter == 24));
        Assert.False(await As(_me).SavePositionAsync(1, 24, 9));         // 없는 절
    }

    [Fact]
    public async Task R158_통독_기록은_계정마다_따로이고_남의_것은_보이지_않는다()
    {
        await As(_other).CompleteChapterAsync(1, 1);
        var mine = await As(_me).SummaryAsync();
        Assert.Equal(0, mine.ReadChapters);
        Assert.False((await As(_me).ChapterAsync(1, 1))!.Done);
        await As(_me).UndoChapterAsync(1, 1);                            // 내 회차 기준이라 남의 것은 안 지워진다
        Assert.Single(InMemoryDb.Raw(_db).ChapterReads);
    }

    [Fact]
    public void 다음_장과_이전_장은_책을_넘어간다()
    {
        Assert.Equal(((byte)2, (short)1), ReadingService.NextChapter(1, 50));
        Assert.Equal(((byte)1, (short)1), ReadingService.NextChapter(66, 22));
        Assert.Equal(((byte)1, (short)50), ReadingService.PrevChapter(2, 1));
        Assert.Null(ReadingService.PrevChapter(1, 1));
    }

    [Fact]
    public async Task R153_R154_R155_회차_기록은_새_회차부터_닫은_회차와_읽은_장을_보여_준다()
    {
        // 처음에는 저장하지 않은 0장 1회차
        var empty = await As(_me).RoundsAsync();
        Assert.Equal((1, RoundState.Reading, 0), (empty.Single().RoundNo, empty.Single().State, empty.Single().Read));
        Assert.Empty(InMemoryDb.Raw(_db).ReadingRounds);   // 보기만으로는 회차를 열지 않음

        await As(_me).CompleteChapterAsync(1, 24);
        await As(_me).CompleteChapterAsync(1, 25);
        Assert.Equal(2, await As(_me).StartNewRoundAsync());   // R-154
        await As(_me).CompleteChapterAsync(1, 1);

        var rows = await As(_me).RoundsAsync();
        Assert.Equal([2, 1], rows.Select(r => r.RoundNo));
        Assert.Equal((RoundState.Reading, 1, (DateOnly?)null), (rows[0].State, rows[0].Read, rows[0].Ended));
        Assert.Equal((RoundState.Closed, 2), (rows[1].State, rows[1].Read));
        Assert.NotNull(rows[1].Ended);
        Assert.Null(rows[1].Days);                              // 걸린 날은 완독일 때만
        Assert.Equal(0.1, rows[1].Percent);                     // 2 / 1,189 → 0.1%(R-155 소수 첫째 자리)
        Assert.Equal([1], (await As(_other).RoundsAsync()).Select(r => r.RoundNo));   // 다른 계정의 회차는 안 보임(R-158)
    }
}
