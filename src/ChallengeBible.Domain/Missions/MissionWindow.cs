namespace ChallengeBible.Domain.Missions;

public enum WindowResult { Open, Future, TooOld, BeforeSeason }

/// <summary>
/// 그 날짜의 미션을 지금 할 수 있는지(R-10 · R-11 · R-13, 시즌 시작일 0028).
/// 화면은 이 결과로 버튼만 막고, 서버도 같은 판단으로 거절한다.
/// </summary>
public static class MissionWindow
{
    public static WindowResult Check(DateOnly today, DateOnly missionDate, int graceDays, DateOnly seasonStart)
    {
        if (missionDate > today) return WindowResult.Future;
        if (missionDate < seasonStart) return WindowResult.BeforeSeason;
        if (missionDate < today.AddDays(-(graceDays - 1))) return WindowResult.TooOld;
        return WindowResult.Open;
    }
}
