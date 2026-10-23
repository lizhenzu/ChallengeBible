namespace ChallengeBible.Web.Pages;

/// <summary>화면에서 쓰는 작은 표시 도우미.</summary>
public static class Ui
{
    /// <summary>부서 표시 글자: 이름 마지막 낱말의 첫 글자(예: "잠실새내교회 유년부" → "유").</summary>
    public static string DeptMark(string name)
    {
        var last = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return string.IsNullOrEmpty(last) ? "?" : last[..1];
    }
}
