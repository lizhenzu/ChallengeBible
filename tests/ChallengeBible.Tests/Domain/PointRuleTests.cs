using ChallengeBible.Domain.Missions;
using ChallengeBible.Domain.Points;
using ChallengeBible.Domain.Rules;

namespace ChallengeBible.Tests.Domain;

public class PointRuleTests
{
    [Theory]
    [InlineData(0, false, QuizState.CanTry)]
    [InlineData(1, false, QuizState.CanTry)]
    [InlineData(2, false, QuizState.OutOfAttempts)]
    [InlineData(1, true, QuizState.Solved)]
    public void R41_퀴즈는_정답이거나_횟수를_다_쓰면_끝(int used, bool solved, QuizState expected) =>
        Assert.Equal(expected, QuizPolicy.State(used, solved, maxAttempts: 2));

    [Theory]
    [InlineData(" 아브라함 ", "아브라함")]
    [InlineData("요 한 복 음", "요한복음")]
    [InlineData("Abraham!", "abraham")]
    public void R43_주관식은_정규화해서_비교(string answer, string accepted) =>
        Assert.True(QuizPolicy.IsCorrectText(answer, [accepted]));

    [Fact]
    public void R83_연속일은_마지막_점수일부터_거꾸로()
    {
        DateOnly D(int d) => new(2027, 1, d);
        Assert.Equal(3, PointMath.Streak([D(1), D(3), D(4), D(5)]));
        Assert.Equal(2, PointMath.Streak([D(1), D(2)]));   // 오늘(5일) 점수가 없어도 마지막 날 기준
        Assert.Equal(0, PointMath.Streak([]));
    }

    [Fact]
    public void R101_새로_넘은_단계만_한_번씩()
    {
        Assert.Equal([3000, 5000], PointMath.NewMilestones(5001, [3000, 5000, 8000], []));
        Assert.Equal([5000], PointMath.NewMilestones(5001, [3000, 5000, 8000], [3000]));
        Assert.Empty(PointMath.NewMilestones(2999, [3000, 5000, 8000], []));
    }

    [Theory]
    [InlineData(24, 8000, 0)]
    [InlineData(4000, 8000, 50)]
    [InlineData(9000, 8000, 100)]
    public void R81_달성률은_0에서_100으로_자름(int points, int target, int expected) =>
        Assert.Equal(expected, PointMath.Achievement(points, target));

    [Fact]
    public void R82_목표_점수는_가장_높은_단계() =>
        Assert.Equal(8000, RuleSet.Default.TargetPoints);

    [Fact]
    public void R60_기본값_하루_최대_6점()
    {
        var max = RuleSet.Default.Items.Where(i => i.Enabled).Sum(i => i.Points);
        Assert.Equal(6, max);
    }

    [Fact]
    public void ADR0032_미션마다_켜진_항목이_하나는_있어야()
    {
        Assert.Empty(RuleSet.Default.Validate());
        var noWord = RuleSet.Default with
        {
            Items = RuleSet.Default.Items.Select(i => i.Item.Mission() == Mission.Word ? i with { Enabled = false } : i).ToList(),
        };
        Assert.Contains(noWord.Validate(), m => m.Contains("Word"));
    }

    [Fact]
    public void 항목_코드는_DB_값과_왕복()
    {
        foreach (var item in Enum.GetValues<MissionItem>())
            Assert.Equal(item, MissionItemCodes.Parse(item.Code()));
        Assert.Equal("Prayer.Others", MissionItem.PrayerOthers.Code());
    }
}
