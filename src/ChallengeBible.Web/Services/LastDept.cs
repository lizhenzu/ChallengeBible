using System.Text.RegularExpressions;

namespace ChallengeBible.Web.Services;

/// <summary>
/// 마지막으로 본 부서를 쿠키(cb_dept)에 기억한다(2026-11-25 사용자 설계): 앱은 언제나 `/` 로 열리고, `/` 가 이 기억 + DB(소개 화면이 켜져 있는지)를 보고
/// 웰컴(`/{부서}/welcome`)으로 보낼지 정한다. 계정에 저장하는 설정이 아니라 자동 기록이라 사람이 고를 것이 없다.
/// </summary>
public static partial class LastDept
{
    public const string Cookie = "cb_dept";

    [GeneratedRegex("^[a-z0-9-]{2,40}$")]
    private static partial Regex SlugRx();

    /// <summary>쿠키에 든 부서 주소(형식이 맞을 때만).</summary>
    public static string? Get(HttpRequest r) => r.Cookies.TryGetValue(Cookie, out var v) && SlugRx().IsMatch(v) ? v : null;

    /// <summary>이 부서를 마지막으로 본 부서로 기억한다(같으면 다시 쓰지 않는다).</summary>
    public static void Set(HttpContext c, string slug)
    {
        if (!SlugRx().IsMatch(slug) || Get(c.Request) == slug) return;
        c.Response.Cookies.Append(Cookie, slug, new CookieOptions { Path = "/", MaxAge = TimeSpan.FromDays(365), HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = c.Request.IsHttps });
    }
}
