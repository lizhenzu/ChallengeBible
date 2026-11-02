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

    /// <summary>
    /// R-190: 나의 점수가 지금 어느 성장 단계에서 몇 % 자랐나. 단계 끝 점수에 닿으면 다음 단계, 마지막 끝 점수 이상이면 다 자람(100%).
    /// </summary>
    public static Growth GrowthOf(int points, IReadOnlyList<Rules.GrowthStage> stages)
    {
        var p = Math.Max(0, points);
        var last = stages.Count - 1;
        if (p >= stages[last].Until) return new(last, 100, 0, true);
        var i = 0;
        while (p >= stages[i].Until) i++;
        var from = i == 0 ? 0 : stages[i - 1].Until;
        var pct = (int)Math.Floor((p - from) * 100.0 / (stages[i].Until - from));
        return new(i, Math.Clamp(pct, 0, 99), stages[i].Until - p, false);
    }

    /// <summary>R-81: 목표 대비 달성률, 0 ~ 100 으로 자른다.</summary>
    public static int Achievement(int points, int target) =>
        target <= 0 ? 0 : Math.Clamp((int)Math.Floor(points * 100.0 / target), 0, 100);
}

/// <summary>성장 단계 계산 결과: 단계 번호(0부터), 그 단계 안 %, 다음 단계까지 남은 점수, 다 자랐나.</summary>
public sealed record Growth(int Stage, int Percent, int ToNext, bool Done);
