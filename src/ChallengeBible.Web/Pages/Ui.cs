namespace ChallengeBible.Web.Pages;

/// <summary>화면에서 쓰는 작은 표시 도우미.</summary>
public static class Ui
{
    /// <summary>받침에 따라 "이" / "가"(예: 말씀이, 퀴즈가).</summary>
    public static string IGa(string word)
    {
        if (string.IsNullOrEmpty(word)) return "이";
        var c = word[^1];
        if (c < '가' || c > '힣') return "이";
        return (c - '가') % 28 == 0 ? "가" : "이";
    }

    /// <summary>부서 표시 글자: 이름 마지막 낱말의 첫 글자(예: "잠실새내교회 유년부" → "유").</summary>
    public static string DeptMark(string name)
    {
        var last = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return string.IsNullOrEmpty(last) ? "?" : last[..1];
    }
}
