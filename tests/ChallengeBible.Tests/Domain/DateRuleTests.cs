using ChallengeBible.Domain.Missions;
using ChallengeBible.Domain.Seasons;
using ChallengeBible.Domain.Time;

namespace ChallengeBible.Tests.Domain;

public class DateRuleTests
{
    private static readonly DateOnly Jan5 = new(2027, 1, 5);

    [Fact]
    public void R01_오늘은_영역_시간대_기준()
    {
        // 서울 2027-01-05 08:30 = UTC 2027-01-04 23:30
        var utc = new DateTimeOffset(2027, 1, 4, 23, 30, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2027, 1, 5), TenantClock.ToTenantDate(utc, "Asia/Seoul"));
        Assert.Equal(new DateOnly(2027, 1, 4), TenantClock.ToTenantDate(utc, "America/Los_Angeles"));
    }

    [Theory]
    [InlineData(DayOfWeek.Sunday, "2027-01-03")]
    [InlineData(DayOfWeek.Monday, "2027-01-04")]
    public void R03_주는_시작_요일부터(DayOfWeek start, string expected)
    {
        var week = Week.Of(Jan5, start);
        Assert.Equal(DateOnly.Parse(expected), week.Start);
        Assert.True(week.Contains(Jan5));
        Assert.Equal(week.Start.AddDays(6), week.End);
    }

    [Theory]
    [InlineData("2027-01-03", 1)]    // 2027년 첫 일요일
    [InlineData("2027-01-09", 1)]
    [InlineData("2027-01-10", 2)]
    [InlineData("2027-01-01", 52)]   // 1월 1일(금)은 2026-12-27 에 시작한 지난해 마지막 주
    [InlineData("2028-12-31", 53)]   // 2028년은 일요일이 53번(윤년 · 1월 1일 토요일)
    public void R50_0052_암송_주_번호는_그해_첫_일요일부터(string date, int no) =>
        Assert.Equal(no, Week.Of(DateOnly.Parse(date), DayOfWeek.Sunday).NumberInYear);

    [Fact]
    public void 그해_N번째_주() =>
        Assert.Equal(new DateOnly(2028, 1, 9), Week.NthOfYear(2028, 2, DayOfWeek.Sunday).Start);

    [Theory]
    [InlineData("2027-01-05", WindowResult.Open)]
    [InlineData("2027-01-04", WindowResult.Open)]
    [InlineData("2027-01-03", WindowResult.Open)]
    [InlineData("2027-01-02", WindowResult.TooOld)]
    public void R10_오늘_포함_최근_N일만(string date, WindowResult expected) =>
        Assert.Equal(expected, MissionWindow.Check(Jan5, DateOnly.Parse(date), graceDays: 3, seasonStart: new DateOnly(2027, 1, 1)));

    [Fact]
    public void R11_미래_날짜는_안됨() =>
        Assert.Equal(WindowResult.Future, MissionWindow.Check(Jan5, Jan5.AddDays(1), 3, new DateOnly(2027, 1, 1)));

    [Fact]
    public void 시즌_시작일_전은_인정_기간_안이라도_안됨_0028() =>
        Assert.Equal(WindowResult.BeforeSeason, MissionWindow.Check(Jan5, new DateOnly(2027, 1, 3), 3, seasonStart: new DateOnly(2027, 1, 4)));

    [Fact]
    public void R62_시즌_점수_범위는_시작일부터_오늘까지_끝은_12월31일()
    {
        var s = Season.Create(2027, new DateOnly(2027, 3, 7));
        Assert.Equal((new DateOnly(2027, 3, 7), new DateOnly(2027, 5, 1)), s.ScoringRange(new DateOnly(2027, 5, 1)));
        Assert.Equal(new DateOnly(2027, 12, 31), s.ScoringRange(new DateOnly(2028, 2, 1)).To);
    }

    [Fact]
    public void 시즌_시작일은_그해_날짜만_0028() =>
        Assert.Throws<ArgumentException>(() => Season.Create(2027, new DateOnly(2026, 12, 31)));
}
