using ChallengeBible.Web.Services;

namespace ChallengeBible.Tests.Domain;

/// <summary>R-118 해설 카드: 범위 글자, 빈 줄 빼기, 어느 절 위에 그릴지(ADR 0056).</summary>
public class BibleNoteTests
{
    private const byte Numbers = 4;   // 민수기

    private static BibleNote Note(int id, int sc, int sv, int ec, int ev, string? key = "제목", string? body = "본문") =>
        new(id, "민수기", key, body, sc, sv, ec, ev);

    private static IReadOnlyList<VerseLine> Lines(short chapter, int count) =>
        Enumerable.Range(1, count).Select(v => new VerseLine(chapter, (short)v, new Dictionary<string, string> { ["KRV"] = $"{v}절" })).ToList();

    private static BibleRef Chapter(short ch, short last) => new(Numbers, ch, 1, ch, last);

    [Fact]
    public void R118_범위는_시작과_끝이_다를_때만_물결을_붙인다()
    {
        Assert.Equal("민수기 35:1 ~ 35:8", Note(1, 35, 1, 35, 8).Range);
        Assert.Equal("민수기 34:20 ~ 35:3", Note(1, 34, 20, 35, 3).Range);
        Assert.Equal("민수기 23:1", Note(1, 23, 1, 23, 1).Range);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n ")]
    public void R118_빈_제목_본문은_줄을_그리지_않는다(string? empty)
    {
        var n = Note(1, 35, 1, 35, 8, key: empty, body: "본문");
        Assert.Null(n.Title);
        Assert.Equal("본문", n.Body);
        Assert.Null(Note(1, 35, 1, 35, 8, key: "제목", body: empty).Body);
    }

    [Fact]
    public void R118_특수문자는_값을_바꾸지_않는다() =>   // 이스케이프는 Razor 출력에서 한다
        Assert.Equal("<b>&\"'", Note(1, 35, 1, 35, 1, key: " <b>&\"' ").Title);

    [Theory]
    [InlineData("예레미야애가", "예레미야 애가")]
    [InlineData("요한일서", "요한1서")]
    [InlineData("요한이서", "요한2서")]
    [InlineData("요한삼서", "요한3서")]
    public void R118_해설_표의_다른_표기도_함께_찾는다(string app, string source) =>
        Assert.Equal([app, source], BibleNoteService.SourceNames(app));

    [Fact]
    public void R118_다른_표기가_없는_책은_이름_하나만() =>
        Assert.Equal(["민수기"], BibleNoteService.SourceNames(" 민수기 "));

    [Fact]
    public void R118_여러_해설은_각자_시작_절_위에_순서대로()
    {
        var placed = BibleNoteService.Place([Note(3, 35, 9, 35, 15), Note(1, 35, 1, 35, 8), Note(2, 35, 1, 35, 1)], Chapter(35, 34), Lines(35, 34));
        Assert.Equal([1, 2], placed[(35, 1)].Select(n => n.Id));
        Assert.Equal([3], placed[(35, 9)].Select(n => n.Id));
        Assert.Equal(2, placed.Count);
    }

    [Fact]
    public void R118_앞_장에서_시작한_해설은_1절_위()
    {
        var placed = BibleNoteService.Place([Note(1, 34, 16, 35, 5), Note(2, 33, 1, 36, 13)], Chapter(35, 34), Lines(35, 34));
        Assert.Equal([2, 1], placed[(35, 1)].Select(n => n.Id));   // 시작 장 · 절 순
    }

    [Fact]
    public void R118_해설이_없으면_빈_묶음() =>
        Assert.Empty(BibleNoteService.Place([], Chapter(35, 34), Lines(35, 34)));

    [Fact]
    public void R118_그릴_절이_본문에_없으면_건너뛴다()
    {
        var placed = BibleNoteService.Place([Note(1, 35, 40, 35, 41), Note(2, 35, 2, 35, 3)], Chapter(35, 60), Lines(35, 34));
        Assert.Equal([2], placed.Values.SelectMany(x => x).Select(n => n.Id));
    }

    [Fact]
    public void R118_구절_범위로_열면_범위와_겹치는_해설만_첫_절_위부터()
    {
        // 미션 범위 35:5~35:12 — 35:1 에서 시작한 해설은 35:5 위, 범위 밖 해설은 그리지 않는다
        var r = new BibleRef(Numbers, 35, 5, 35, 12);
        var lines = Lines(35, 12).Where(l => l.Verse >= 5).ToList();
        var placed = BibleNoteService.Place([Note(1, 35, 1, 35, 8), Note(2, 35, 9, 35, 15), Note(3, 35, 1, 35, 3), Note(4, 35, 13, 35, 20)], r, lines);
        Assert.Equal([1], placed[(35, 5)].Select(n => n.Id));
        Assert.Equal([2], placed[(35, 9)].Select(n => n.Id));
        Assert.Equal(2, placed.Count);
    }
}
