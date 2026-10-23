namespace ChallengeBible.Domain.Points;

public static class PointMath
{
    /// <summary>
    /// R-83: 연속 참여일 = 마지막 점수 획득일부터 거꾸로 하루도 빠짐없이 점수가 있었던 날 수.
    /// 마지막 점수일이 오늘이 아니어도 그 날 기준으로 센다.
    /// </summary>
    public static int Streak(IEnumerable<DateOnly> pointDays)
    {
        var days = pointDays.ToHashSet();
        if (days.Count == 0) return 0;
        var d = days.Max();
        var n = 0;
        while (days.Contains(d)) { n++; d = d.AddDays(-1); }
        return n;
    }

    /// <summary>R-101: 이번에 새로 넘은 축하 단계(이미 도달한 단계는 빼고, 낮은 것부터).</summary>
    public static IReadOnlyList<int> NewMilestones(int seasonPoints, IEnumerable<int> milestones, IEnumerable<int> alreadyReached)
    {
        var reached = alreadyReached.ToHashSet();
        return milestones.Where(m => seasonPoints >= m && !reached.Contains(m)).Order().ToList();
    }

    /// <summary>R-81: 목표 대비 달성률, 0 ~ 100 으로 자른다.</summary>
    public static int Achievement(int points, int target) =>
        target <= 0 ? 0 : Math.Clamp((int)Math.Floor(points * 100.0 / target), 0, 100);
}
