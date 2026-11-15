using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Tests.Support;
using ChallengeBible.Web.Services;

namespace ChallengeBible.Tests.Web;

/// <summary>
/// 퀴즈 출제 · 말씀 입력에 더한 것(유년부 /Question · /JuManNa 에서 옮김, 확정 시안 퀴즈출제-말씀입력, 2026-11-15):
/// 한 달 입력 · 영상 구간 편집 · 퀴즈 삭제(내림) · AI 프롬프트.
/// </summary>
public class WordToolsTests
{
    private static readonly DateOnly Nov1 = new(2026, 11, 1);
    private readonly string _db = InMemoryDb.NewName();
    private readonly AppUser _op;
    private readonly FixedTime _time = FixedTime.SeoulMorning(2026, 11, 14);
    private ContentAdminService Content => new(new InMemorySystemDb(_db), _time);
    private WordToolsService Tools => new(new InMemorySystemDb(_db), Content, _time);
    private QuizAiPromptService Prompts => new(new InMemorySystemDb(_db), _time);
    private string Op => _op.UserName!;

    public WordToolsTests()
    {
        _op = InMemoryDb.SeedAccount(_db, "operator", "운영자");
        using var raw = InMemoryDb.Raw(_db);
        InMemoryDb.AddRole(_db, raw.Users.Single().Id, IdentityRoles.SuperAdmin);
        raw.SaveChanges();
    }

    private static WordMonthRow Row(int day, string title, short sc, short sv, short ec, short ev, byte book = 47) =>
        new() { Date = new DateOnly(2026, 11, day), Title = title, Book = book, StartChapter = sc, StartVerse = sv, EndChapter = ec, EndVerse = ev, Changed = true };

    [Fact]
    public async Task 한_달_입력_새_날은_초안_공개된_날은_새_개정_바뀌지_않은_날과_빈_줄은_건너뛴다()
    {
        await Content.SaveAsync(Op, ContentKind.Word, new ContentInput { Date = new(2026, 11, 2), Title = "옛 제목", Summary = "이야기", Book = 40, StartChapter = 6, StartVerse = 9, EndChapter = 6, EndVerse = 13 }, publish: true);
        var rows = await Tools.MonthAsync(Op, Nov1);
        Assert.Equal(30, rows.Count);
        rows[0] = Row(1, "빛과 소금", 5, 13, 5, 16, 40);
        rows[1] = Row(2, "주기도문", 6, 9, 6, 13, 40);
        rows[2] = new WordMonthRow { Date = new(2026, 11, 3), Changed = true };   // 빈 줄
        var (added, updated, errors) = await Tools.SaveMonthAsync(Op, Nov1, rows);
        Assert.Equal((1, 1, 0), (added, updated, errors.Count));
        using (var raw = InMemoryDb.Raw(_db))
        {
            var d1 = raw.DailyWords.Single(w => w.ExposeDate == new DateOnly(2026, 11, 1));
            Assert.Equal((ContentStatus.Draft, "빛과 소금", ""), (d1.Status, d1.Title, d1.Summary));
            var d2 = raw.DailyWords.Single(w => w.ExposeDate == new DateOnly(2026, 11, 2) && w.IsCurrent);
            Assert.Equal((ContentStatus.Published, "주기도문", "이야기", 2), (d2.Status, d2.Title, d2.Summary, d2.Revision));   // 이야기는 그대로
        }
        var again = await Tools.MonthAsync(Op, Nov1);
        foreach (var r in again) r.Changed = true;
        Assert.Equal((0, 0), (await Tools.SaveMonthAsync(Op, Nov1, again)) is var x ? (x.Added, x.Updated) : default);   // 바뀐 날 없음
    }

    [Fact]
    public async Task 한_달_입력_틀린_범위는_그_날만_저장하지_않는다()
    {
        var rows = new List<WordMonthRow> { Row(5, "좋은 날", 3, 1, 3, 5), Row(6, "끝이 앞", 3, 9, 3, 2) };
        var (added, _, errors) = await Tools.SaveMonthAsync(Op, Nov1, rows);
        Assert.Equal(1, added);
        Assert.True(errors.ContainsKey(new DateOnly(2026, 11, 6)));
    }

    [Fact]
    public async Task 영상_구간_장을_걸치면_모든_장을_차례대로_저장하고_모두_기본이면_목록을_비운다()
    {
        using (var raw = InMemoryDb.Raw(_db))
        {
            raw.ChapterMedia.AddRange(
                new ChapterMedia { BookId = 47, Chapter = 11, Kind = ChapterMediaKind.Listen, YoutubeVideoId = "aaaaaaaaaaa", DurationSec = 478, UpdatedUtc = DateTime.UtcNow },
                new ChapterMedia { BookId = 47, Chapter = 12, Kind = ChapterMediaKind.Listen, YoutubeVideoId = "bbbbbbbbbbb", DurationSec = 301, UpdatedUtc = DateTime.UtcNow });
            raw.SaveChanges();
        }
        await Tools.SaveMonthAsync(Op, Nov1, [Row(15, "약할 때 강함", 11, 30, 12, 10)]);
        var day = (await Tools.ClipMonthAsync(Op, Nov1)).Single();
        Assert.Equal([(short)11, (short)12], day.Chapters.Select(c => c.Chapter).ToArray());
        Assert.Equal(("30절부터", "10절까지", 478), (day.Chapters[0].Hint, day.Chapters[1].Hint, day.Chapters[0].LengthSec!.Value));

        Assert.True(await Tools.SaveClipAsync(Op, new(2026, 11, 15), 11, "s", 370));
        using (var raw = InMemoryDb.Raw(_db))
        {
            var clips = raw.DailyWordClips.OrderBy(c => c.Seq).ToList();
            Assert.Equal(2, clips.Count);   // 12장도 함께(목록이 있으면 목록의 장만 재생, R-127)
            Assert.Equal(((short)11, (int?)370, (short)12, (int?)null), (clips[0].Chapter, clips[0].StartSec, clips[1].Chapter, clips[1].StartSec));
        }
        Assert.True(await Tools.SaveClipAsync(Op, new(2026, 11, 15), 11, "e", 300));   // 끝이 시작보다 앞 → 끝은 기본
        using (var raw = InMemoryDb.Raw(_db)) Assert.Null(raw.DailyWordClips.Single(c => c.Chapter == 11).EndSec);
        Assert.True(await Tools.SaveClipAsync(Op, new(2026, 11, 15), 11, "s", null));
        using (var raw = InMemoryDb.Raw(_db)) Assert.Empty(raw.DailyWordClips);
        Assert.False(await Tools.SaveClipAsync(Op, new(2026, 11, 15), 13, "s", 10));   // 범위 밖 장
    }

    [Fact]
    public async Task 퀴즈_삭제는_지금_개정을_내린다()
    {
        var input = new ContentInput { Date = new(2026, 11, 14), Question = "문제", Choices = ["가", "나", "다", "라"], CorrectIndex = 1 };
        Assert.True((await Content.SaveAsync(Op, ContentKind.Quiz, input, publish: true)).Ok);
        Assert.True(await Tools.ArchiveQuizAsync(Op, new(2026, 11, 14)));
        using var raw = InMemoryDb.Raw(_db);
        var q = raw.DailyQuizzes.Single();
        Assert.Equal((false, ContentStatus.Archived), (q.IsCurrent, q.Status));
        Assert.Empty(await Content.QuizCheckMonthAsync(Op, Nov1));
        Assert.False(await Tools.ArchiveQuizAsync(Op, new(2026, 11, 14)));
    }

    [Fact]
    public async Task AI_프롬프트_기본은_유년부_문구_고치면_저장_자리_표시가_빠지면_막고_비우면_기본으로()
    {
        var p = await Prompts.GetAsync(Op);
        Assert.True(p.IsDefault);
        Assert.Contains("유년부 문제 출제자", p.Text);
        Assert.Contains(QuizAiPromptService.BibleRangeToken, p.Text);
        Assert.Equal("범위 고후 11:1 날짜 2026-11-14", QuizAiPromptService.Build("범위 {{BibleRange}} 날짜 {{ExposeDate}}", "고후 11:1", "2026-11-14"));

        Assert.NotEmpty(await Prompts.SaveAsync(Op, "자리 표시 없는 문구"));
        Assert.Empty(await Prompts.SaveAsync(Op, "새 문구 {{BibleRange}} {{ExposeDate}}"));
        Assert.Equal(("새 문구 {{BibleRange}} {{ExposeDate}}", false), ((await Prompts.GetAsync(Op)).Text, (await Prompts.GetAsync(Op)).IsDefault));
        Assert.Empty(await Prompts.SaveAsync(Op, ""));
        Assert.True((await Prompts.GetAsync(Op)).IsDefault);
    }

    [Fact]
    public async Task 플랫폼_관리자가_아니면_못_한다()
    {
        InMemoryDb.SeedAccount(_db, "kid", "아이");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Tools.MonthAsync("kid", Nov1));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Prompts.SaveAsync("kid", "x {{BibleRange}} {{ExposeDate}}"));
    }
}
