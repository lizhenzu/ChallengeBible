namespace ChallengeBible.Domain.Seasons;

/// <summary>
/// 시즌 = 달력 연도. 끝은 늘 12월 31일, 시작일만 영역이 정한다(ADR 0028).
/// 연도 문자열을 코드 · 표에 쓰지 않는다(X-02).
/// </summary>
public sealed record Season(int Year, DateOnly StartDate)
{
    public DateOnly EndDate => new(Year, 12, 31);

    public static Season DefaultFor(int year) => new(year, new DateOnly(year, 1, 1));

    public static Season Create(int year, DateOnly startDate)
    {
        if (startDate.Year != year) throw new ArgumentException("시즌 시작일은 그해 날짜여야 해요(0028).", nameof(startDate));
        return new Season(year, startDate);
    }

    public bool HasStarted(DateOnly today) => today >= StartDate;

    /// <summary>시즌 점수에 들어가는 날짜 범위(R-62): 시작일 ~ (진행 중이면 오늘, 지났으면 12월 31일).</summary>
    public (DateOnly From, DateOnly To) ScoringRange(DateOnly today) =>
        (StartDate, today < EndDate ? today : EndDate);
}
