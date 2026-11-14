namespace ChallengeBible.Domain.Rules;

/// <summary>
/// 선물 크레딧 · 점수 · 가격 계산(ADR 0094 B, R-280 · R-283 · R-285). 값은 모두 규칙(GiftRules)과 부서 표에서 받는다.
/// </summary>
public static class GiftMath
{
    /// <summary>
    /// 점수 → 크레딧(R-280): StepPoints 단위로만 센다. 그해 처음 보너스를 아직 안 썼으면 첫 단위 = FirstCredits, 나머지 단위 = StepCredits. 상한에서 자른다.
    /// </summary>
    public static int CreditsFor(int points, GiftRules r, bool firstBonusAvailable = true, int? cap = null)
    {
        if (points < r.StepPoints || r.StepPoints <= 0) return 0;
        var steps = points / r.StepPoints;
        var credits = firstBonusAvailable ? r.FirstCredits + (steps - 1) * r.StepCredits : steps * r.StepCredits;
        return Math.Min(credits, cap ?? r.PersonalCap);
    }

    /// <summary>
    /// 크레딧 → 아이에게 보일 필요한 점수(R-285, ADR 0094 정한 것 2): 처음 FirstCredits = StepPoints 점, 그 뒤 StepCredits 마다 StepPoints 점(올림).
    /// </summary>
    public static int PointsFor(int credits, GiftRules r)
    {
        if (credits <= 0) return 0;
        if (credits <= r.FirstCredits) return r.StepPoints;
        var extra = (credits - r.FirstCredits + r.StepCredits - 1) / r.StepCredits;
        return r.StepPoints + extra * r.StepPoints;
    }

    /// <summary>
    /// 가격(원) → 필요한 크레딧(R-283): 1,000 크레딧 단위로, 그 크레딧의 금액이 가격 이상인 가장 작은 크레딧.
    /// 부서 표(크레딧 → 금액)에 없는 크레딧은 1크레딧 = 1원.
    /// </summary>
    public static int CreditsForPrice(int price, IReadOnlyDictionary<int, int>? rates, int unit = 1000)
    {
        if (price <= 0) return 0;
        var top = Math.Max(price, rates is { Count: > 0 } ? rates.Keys.Max() : 0);
        for (var c = unit; c <= top + unit; c += unit)
            if ((rates is not null && rates.TryGetValue(c, out var amount) ? amount : c) >= price) return c;
        return (price + unit - 1) / unit * unit;
    }
}
