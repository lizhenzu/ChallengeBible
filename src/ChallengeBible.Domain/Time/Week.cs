namespace ChallengeBible.Domain.Time;

/// <summary>주(週) 범위. 시작 요일은 규칙 값(R-03, 기본 일요일).</summary>
public readonly record struct Week(DateOnly Start)
{
    public DateOnly End => Start.AddDays(6);

    public bool Contains(DateOnly date) => date >= Start && date <= End;

    public static Week Of(DateOnly date, DayOfWeek weekStart)
    {
        var back = ((int)date.DayOfWeek - (int)weekStart + 7) % 7;
        return new Week(date.AddDays(-back));
    }

    /// <summary>
    /// 그해 몇째 주인지(0052): 그해 첫 시작 요일에 시작하는 주가 1주. 주는 시작일이 속한 해의 주다
    /// (1월 1일 ~ 첫 시작일 전은 지난해 마지막 주). 한 해는 52 또는 53주.
    /// </summary>
    public int NumberInYear => (Start.DayNumber - FirstOfYear(Start.Year, Start.DayOfWeek).DayNumber) / 7 + 1;

    /// <summary>그해 no 번째 주.</summary>
    public static Week NthOfYear(int year, int no, DayOfWeek weekStart) => new(FirstOfYear(year, weekStart).AddDays((no - 1) * 7));

    private static DateOnly FirstOfYear(int year, DayOfWeek weekStart)
    {
        var jan1 = new DateOnly(year, 1, 1);
        return jan1.AddDays(((int)weekStart - (int)jan1.DayOfWeek + 7) % 7);
    }
}
