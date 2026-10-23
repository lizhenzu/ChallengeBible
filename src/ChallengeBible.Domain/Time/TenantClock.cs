namespace ChallengeBible.Domain.Time;

/// <summary>
/// 영역 시간대 기준 "오늘"(R-01). 저장은 UTC, 날짜 판단은 이 클래스로만 한다.
/// DB 의 현재 시각 함수는 쓰지 않는다(X-09).
/// </summary>
public sealed class TenantClock(TimeProvider time)
{
    public DateTimeOffset UtcNow => time.GetUtcNow();

    public DateOnly Today(string timeZoneId) => ToTenantDate(UtcNow, timeZoneId);

    public static DateOnly ToTenantDate(DateTimeOffset utc, string timeZoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, zone).DateTime);
    }
}
