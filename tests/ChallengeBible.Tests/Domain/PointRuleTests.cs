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

    [Theory]
    [InlineData(0, 0, 0, 400, false)]       // 씨앗 시작
    [InlineData(399, 0, 99, 1, false)]
    [InlineData(400, 1, 0, 500, false)]     // 끝 점수에 닿으면 다음 단계
    [InlineData(1232, 2, 83, 68, false)]    // 시안: 나무 83%, 숲까지 68점
    [InlineData(1300, 3, 0, 400, false)]
    [InlineData(1700, 3, 100, 0, true)]     // 마지막 끝 점수 이상 = 다 자람
    [InlineData(-5, 0, 0, 400, false)]
    public void R190_성장_단계는_지금_단계_안에서_자란_퍼센트와_남은_점수(int points, int stage, int pct, int toNext, bool done) =>
        Assert.Equal(new ChallengeBible.Domain.Points.Growth(stage, pct, toNext, done), PointMath.GrowthOf(points, ChallengeBible.Domain.Rules.RuleSet.DefaultGrowth));

    [Fact]
    public void R190_예전_규칙_JSON_에_성장_단계가_없으면_기본값() =>
        Assert.Equal(ChallengeBible.Domain.Rules.RuleSet.DefaultGrowth,
            ChallengeBible.Web.Services.RuleService.FromJson("{\"Version\":2,\"WeekStart\":\"Sunday\",\"GraceDays\":3,\"QuizMaxAttempts\":2,\"Items\":[],\"Milestones\":[3000],\"FamilyMilestones\":[],\"Features\":{\"Ranking\":true,\"FamilyTeam\":true,\"TranslationCompare\":true}}").GrowthStages);

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
