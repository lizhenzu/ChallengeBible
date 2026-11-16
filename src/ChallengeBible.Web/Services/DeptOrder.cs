namespace ChallengeBible.Web.Services;

/// <summary>
/// 부서를 나이순으로(2026-11-15 사용자: 영아부 → 유아부 → 유치부 → 유년부 → 초등부 → 중등부 → 고등부). 이름에 부서 종류가 없으면 맨 뒤(만든 순).
/// </summary>
public static class DeptOrder
{
    private static readonly string[] Ages = ["영아부", "유아부", "유치부", "유년부", "초등부", "중등부", "고등부"];

    public static int Key(string? name)
    {
        for (var i = 0; i < Ages.Length; i++)
            if (name?.Contains(Ages[i], StringComparison.Ordinal) == true) return i;
        return Ages.Length;
    }

    /// <summary>플랫폼 › 부서 카드 순서(2026-11-16 사용자 "연령별, 고등부 맨 나중"): 부서 종류가 없는 이름(처음 오신 분 등)을 먼저, 그다음 영아부 → … → 고등부.</summary>
    public static int AgeLastKey(string? name) { var k = Key(name); return k == Ages.Length ? -1 : k; }
}
