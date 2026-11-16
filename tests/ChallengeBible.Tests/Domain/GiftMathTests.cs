using ChallengeBible.Domain.Rules;

namespace ChallengeBible.Tests.Domain;

/// <summary>선물 크레딧 · 점수 · 가격 계산(ADR 0094 B → ADR 0104, R-280 · R-283 · R-285): 1점 = 15 크레딧, 처음 보너스 없음, 한 해 상한 30,000.</summary>
public class GiftMathTests
{
    private static readonly GiftRules R = GiftRules.Default;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 15)]        // 1점부터 센다(100점 단위가 아님)
    [InlineData(99, 1485)]
    [InlineData(100, 1500)]    // 처음 보너스 없음
    [InlineData(667, 10005)]
    [InlineData(1877, 28155)]  // 기본 규칙으로 1년 매일 했을 때
    [InlineData(2000, 30000)]  // 상한 딱
    [InlineData(3100, 30000)]  // 상한에서 멈춤
    public void R280_1점마다_15_크레딧이고_한_해_상한에서_멈춘다(int points, int credits) => Assert.Equal(credits, GiftMath.CreditsFor(points, R));

    [Fact]
    public void R280_부서_상한을_따른다() => Assert.Equal(20000, GiftMath.CreditsFor(3100, R, cap: 20000));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1000, 67)]     // 66.7 → 올림
    [InlineData(5000, 334)]
    [InlineData(9000, 600)]
    [InlineData(10000, 667)]
    [InlineData(30000, 2000)]
    public void R285_아이에게는_크레딧_대신_필요한_점수(int credits, int points) => Assert.Equal(points, GiftMath.PointsFor(credits, R));

    [Fact]
    public void R283_가격을_1000_크레딧_단위로_표가_비면_1크레딧_1원()
    {
        Assert.Equal(10000, GiftMath.CreditsForPrice(9800, null));
        Assert.Equal(1000, GiftMath.CreditsForPrice(1000, null));
        Assert.Equal(0, GiftMath.CreditsForPrice(0, null));
        // 부서가 10,000 크레딧 = 12,000원으로 정하면 11,000원 선물은 10,000 크레딧
        Assert.Equal(10000, GiftMath.CreditsForPrice(11000, new Dictionary<int, int> { [10000] = 12000 }));
        Assert.Equal(667, GiftMath.PointsFor(GiftMath.CreditsForPrice(9800, null), R));   // 9,800원 → 10,000 크레딧 → 667점
    }

    [Fact]
    public void R280_한_해_상한에_닿는_점수()
    {
        Assert.Equal(2000, GiftMath.CapPoints(30000, R));
        Assert.Equal(1333, GiftMath.CapPoints(20000, R));
        Assert.Equal(0, GiftMath.CapPoints(0, R));
    }
}
