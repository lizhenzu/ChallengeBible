using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;

namespace ChallengeBible.Tests.Web;

/// <summary>절 읽음 표시 · 책갈피(R-112, 0029): 계정 범위, 켜기 · 끄기 멱등(X-17).</summary>
public class BibleMarkServiceTests
{
    private readonly string _db = InMemoryDb.NewName();
    private readonly FixedTime _time = FixedTime.SeoulMorning();
    private readonly int _me, _other;
    private static readonly BibleRef Gen24 = new(1, 24, 1, 24, 9);

    public BibleMarkServiceTests()
    {
        ContentSeed.Bible(_db);   // 창세기 24:1~9 등
        _me = InMemoryDb.SeedAccount(_db, "cheolsu@example.com", "김철수").Id;
        _other = InMemoryDb.SeedAccount(_db, "younghee@example.com", "김영희").Id;
    }

    private BibleMarkService As(int accountId)
    {
        var scope = new RequestScope();
        scope.SetAccount(accountId);
        return new BibleMarkService(new InMemoryDb(_db, scope), scope, _time);
    }

    [Fact]
    public async Task R112_읽음은_두_번_켜도_하나이고_처음_읽은_시각이_남는다()
    {
        Assert.True(await As(_me).ReadAsync(1, 24, 3));
        var first = InMemoryDb.Raw(_db).VerseReadMarks.Single().ReadUtc;
        _time.Now = _time.Now.AddHours(1);
        Assert.True(await As(_me).ReadAsync(1, 24, 3));   // 다시 켜도(재시도 · 두 번 누름) 그대로
        var row = InMemoryDb.Raw(_db).VerseReadMarks.Single();
        Assert.Equal(first, row.ReadUtc);
        Assert.Equal(_me, row.AccountId);
    }

    [Fact]
    public async Task R112_읽음_끄기는_없는_것을_다시_꺼도_괜찮다()
    {
        await As(_me).ReadAsync(1, 24, 3);
        await As(_me).UnreadAsync(1, 24, 3);
        await As(_me).UnreadAsync(1, 24, 3);              // 토글이 아니라 다시 켜지지 않는다(X-17)
        Assert.Empty(InMemoryDb.Raw(_db).VerseReadMarks);
    }

    [Fact]
    public async Task R112_범위_전체_읽음과_취소()
    {
        await As(_me).ReadAsync(1, 24, 2);
        Assert.Equal(8, await As(_me).ReadRangeAsync(Gen24));   // 9절 중 이미 읽은 1절 빼고
        Assert.Equal(9, (await As(_me).MarksAsync(Gen24)).Read.Count);
        Assert.Equal(0, await As(_me).ReadRangeAsync(Gen24));   // 다시 해도 그대로
        Assert.Equal(3, await As(_me).UnreadRangeAsync(new BibleRef(1, 24, 1, 24, 3)));
        Assert.Equal(6, (await As(_me).MarksAsync(Gen24)).Read.Count);
    }

    [Fact]
    public async Task 없는_절은_읽음이나_책갈피로_표시하지_않는다()
    {
        Assert.False(await As(_me).ReadAsync(1, 24, 99));
        Assert.False(await As(_me).BookmarkAsync(1, 99, 1));
        Assert.Empty(InMemoryDb.Raw(_db).VerseReadMarks);
        Assert.Empty(InMemoryDb.Raw(_db).Bookmarks);
    }

    [Fact]
    public async Task R112_책갈피_켜기_끄기()
    {
        Assert.True(await As(_me).BookmarkAsync(1, 24, 7));
        Assert.True(await As(_me).BookmarkAsync(1, 24, 7));
        var marks = await As(_me).MarksAsync(Gen24);
        Assert.Equal((short)7, Assert.Single(marks.Bookmarked).Verse);
        await As(_me).UnbookmarkAsync(1, 24, 7);
        Assert.Empty((await As(_me).MarksAsync(Gen24)).Bookmarked);
    }

    [Fact]
    public async Task R113_읽음_표시는_점수를_만들지_않는다()
    {
        await As(_me).ReadRangeAsync(Gen24);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Empty(raw.Activities);
        Assert.Empty(raw.PointLedgers);
    }

    [Fact]
    public async Task 다른_계정의_읽음_책갈피는_보이지_않고_지울_수도_없다_0029()
    {
        await As(_other).ReadAsync(1, 24, 1);
        await As(_other).BookmarkAsync(1, 24, 1);
        var mine = await As(_me).MarksAsync(Gen24);
        Assert.Empty(mine.Read);
        Assert.Empty(mine.Bookmarked);
        await As(_me).UnreadAsync(1, 24, 1);
        await As(_me).UnreadRangeAsync(Gen24);
        await As(_me).UnbookmarkAsync(1, 24, 1);
        using var raw = InMemoryDb.Raw(_db);
        Assert.Single(raw.VerseReadMarks);                 // 영희 것은 그대로
        Assert.Single(raw.Bookmarks);
    }
}
