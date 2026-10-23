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
}
