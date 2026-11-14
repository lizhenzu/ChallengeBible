using ChallengeBible.Domain.Rules;

namespace ChallengeBible.Tests.Domain;

/// <summary>선물 크레딧 · 점수 · 가격 계산(ADR 0094 B, R-280 · R-283 · R-285).</summary>
public class GiftMathTests
{
    private static readonly GiftRules R = GiftRules.Default;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(99, 0)]       // 100점 아래는 크레딧 없음
    [InlineData(100, 5000)]   // 처음 100점 = 5,000
    [InlineData(199, 5000)]   // 자투리는 세지 않음
    [InlineData(200, 6000)]   // 그 뒤 100점마다 1,000
    [InlineData(1000, 14000)]
    [InlineData(2590, 29000)]
    [InlineData(2600, 30000)] // 상한 딱
    [InlineData(3100, 30000)] // 상한에서 멈춤
    public void R280_점수를_크레딧으로(int points, int credits) => Assert.Equal(credits, GiftMath.CreditsFor(points, R));

    [Fact]
    public void R280_처음_보너스를_이미_썼으면_100점마다_1000이고_부서_상한을_따른다()
    {
        Assert.Equal(4000, GiftMath.CreditsFor(450, R, firstBonusAvailable: false));
        Assert.Equal(20000, GiftMath.CreditsFor(3100, R, cap: 20000));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1000, 100)]
    [InlineData(5000, 100)]
    [InlineData(5001, 200)]
    [InlineData(7000, 300)]
    [InlineData(10000, 600)]
    [InlineData(30000, 2600)]
    public void R285_아이에게는_크레딧_대신_필요한_점수(int credits, int points) => Assert.Equal(points, GiftMath.PointsFor(credits, R));

    [Fact]
    public void R283_가격을_1000_크레딧_단위로_표가_비면_1크레딧_1원()
    {
        Assert.Equal(10000, GiftMath.CreditsForPrice(9800, null));
        Assert.Equal(1000, GiftMath.CreditsForPrice(1000, null));
        Assert.Equal(0, GiftMath.CreditsForPrice(0, null));
        // 부서가 10,000 크레딧 = 12,000원으로 정하면 11,000원 선물은 10,000 크레딧
        Assert.Equal(10000, GiftMath.CreditsForPrice(11000, new Dictionary<int, int> { [10000] = 12000 }));
        Assert.Equal(600, GiftMath.PointsFor(GiftMath.CreditsForPrice(9800, null), R));   // 시안의 미리 보기: 9,800원 → 600점
    }

    [Theory]
    [InlineData(5000, true, 100)]
    [InlineData(6000, true, 200)]
    [InlineData(5000, false, 500)]   // 처음 보너스를 이미 썼으면 1,000 크레딧마다 100점(쌓는 규칙과 같게)
    [InlineData(1500, false, 200)]
    public void R285_필요한_점수는_처음_보너스를_썼는지에_따라(int credits, bool bonus, int need) => Assert.Equal(need, GiftMath.NeedPoints(credits, R, bonus));

    [Fact]
    public void R280_상한에_닿는_점수()
    {
        Assert.Equal(2600, GiftMath.CapPoints(30000, R, true));   // 100점 = 5,000 + 2,500점 = 25,000
        Assert.Equal(3000, GiftMath.CapPoints(30000, R, false));
    }
}
