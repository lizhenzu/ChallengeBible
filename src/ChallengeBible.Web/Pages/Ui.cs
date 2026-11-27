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

    /// <summary>받침에 따라 "을" / "를".</summary>
    public static string EulReul(string word) => IGa(word) == "가" ? "를" : "을";

    /// <summary>받침에 따라 "으로" / "로"(받침이 없거나 ㄹ 받침이면 "로").</summary>
    public static string EuRo(string word)
    {
        if (string.IsNullOrEmpty(word)) return "로";
        var c = word[^1];
        if (c < '가' || c > '힣') return "로";
        var jong = (c - '가') % 28;
        return jong == 0 || jong == 8 ? "로" : "으로";
    }

    /// <summary>받침에 따라 "은" / "는".</summary>
    public static string EunNeun(string word) => IGa(word) == "가" ? "는" : "은";

    /// <summary>부서 표시 글자: 이름 마지막 낱말의 첫 글자(예: "잠실새내교회 유년부" → "유").</summary>
    public static string DeptMark(string name)
    {
        var last = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return string.IsNullOrEmpty(last) ? "?" : last[..1];
    }

    /// <summary>이름 뒤 "님"(이미 "선생님"처럼 님으로 끝나면 그대로 — 2026-11-17 "선생님님" 고침).</summary>
    public static string Nim(string? name) => string.IsNullOrEmpty(name) ? "" : name.EndsWith('님') ? name : name + "님";

    /// <summary>인사말에서 이름만 크게 쓰려고 끝의 "님"을 뗀다(이름 + 작은 「님」 = Nim 과 같은 결과, 2026-11-25).</summary>
    public static string NameOnly(string? name) => string.IsNullOrEmpty(name) ? "" : (name.EndsWith('님') ? name[..^1] : name);

    /// <summary>프로필 동그라미 글자: 표시 이름 첫 글자(사진이 없을 때 — 2026-11-23 머리말 "000님").</summary>
    public static string Initial(string? name) => string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1];
}
