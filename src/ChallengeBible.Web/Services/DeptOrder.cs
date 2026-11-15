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
}
