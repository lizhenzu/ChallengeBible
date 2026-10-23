namespace ChallengeBible.Tests.Support;

/// <summary>정해 둔 시각. 기본은 2027-01-05 오전 9시(서울) = 2027-01-05 00:00 UTC.</summary>
public sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;

    public static FixedTime SeoulMorning(int year = 2027, int month = 1, int day = 5) =>
        new(new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero));
}
