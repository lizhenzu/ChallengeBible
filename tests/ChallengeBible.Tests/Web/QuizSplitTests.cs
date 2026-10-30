using ChallengeBible.Web.Pages.Mission;

namespace ChallengeBible.Tests.Web;

/// <summary>퀴즈 문제 글 가르기 — 옛 사이트(DailyMission4 Quiz)와 같은 화면이 되게(2026-10-31 사용자).</summary>
public class QuizSplitTests
{
    [Fact]
    public void 줄_맨_앞_Q_표식에서_배경과_질문을_가르고_표식은_뗀다()
    {
        var (bg, q) = QuizModel.SplitQuestion("시편 19편은 크게 두 부분으로 나뉩니다.\r\n\r\n자연은 하나님이 계시다는 것까지 알려 줘요.\r\n\r\nQ. 이 구성이 알려 주는 것은? (시 19:1, 19:7)");
        Assert.Equal("시편 19편은 크게 두 부분으로 나뉩니다.\n\n자연은 하나님이 계시다는 것까지 알려 줘요.", bg);
        Assert.Equal("이 구성이 알려 주는 것은? (시 19:1, 19:7)", q);
    }

    [Theory]
    [InlineData("다음 중 맞는 것은?")]                  // 표식 없음 → 전체가 질문
    [InlineData("Q. 맨 처음부터 질문이에요")]           // 줄 맨 앞이어야 하므로 첫 줄은 가르지 않음(옛 사이트와 같음)
    public void 가를_곳이_없으면_배경은_비고_전체가_질문(string raw)
    {
        var (bg, q) = QuizModel.SplitQuestion(raw);
        Assert.Equal("", bg);
        Assert.Equal(raw, q);
    }

    [Fact]
    public void 다른_표식도_같다()
    {
        Assert.Equal(("배경", "질문이에요"), QuizModel.SplitQuestion("배경\n[문제] 질문이에요"));
        Assert.Equal(("배경", "질문이에요"), QuizModel.SplitQuestion("배경\n질문. 질문이에요"));
    }
}
