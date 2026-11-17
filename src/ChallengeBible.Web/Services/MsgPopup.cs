using Microsoft.AspNetCore.Http;

namespace ChallengeBible.Web.Services;

/// <summary>
/// 새 쪽지 팝업을 언제 보이나(확정 시안 쪽지-팝업, 2026-11-17 사용자 "로그인 다시 안 해도 F5 누르면 보낸 쪽지를 팝업으로"):
/// 로그인 직후 한 번(안 읽은 쪽지가 있으면) + 그 뒤로는 홈을 새로 열 때 아직 팝업으로 보여 주지 않은 새 쪽지가 오면 한 번.
/// 마지막으로 보여 준 쪽지 번호를 이 브라우저 쿠키에 계정별로 남긴다 — 같은 쪽지로 F5 마다 뜨지 않게.
/// </summary>
public static class MsgPopup
{
    public const string Cookie = "cb-msgpop";

    public static async Task<(int Count, IReadOnlyList<MsgRow> List)> DecideAsync(HttpContext http, MessageService msgs, string user, bool afterLogin, CancellationToken ct)
    {
        var n = await msgs.UnreadAsync(user, ct);
        if (n == 0) return (0, []);
        var unread = (await msgs.InboxAsync(user, ct)).Where(r => r.Unread).ToList();
        if (unread.Count == 0) return (0, []);
        var top = unread.Max(r => r.Id);
        var seen = Seen(http, user);
        if (!afterLogin && top <= seen) return (0, []);
        http.Response.Cookies.Append(Cookie, $"{Uri.EscapeDataString(user)}:{top}", new CookieOptions
        {
            HttpOnly = true, Secure = http.Request.IsHttps, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromDays(60), IsEssential = true,
        });
        return (n, unread.Take(3).ToList());
    }

    private static long Seen(HttpContext http, string user)
    {
        if (http.Request.Cookies[Cookie] is not { } v) return 0;
        var i = v.LastIndexOf(':');
        return i > 0 && Uri.UnescapeDataString(v[..i]) == user && long.TryParse(v[(i + 1)..], out var id) ? id : 0;
    }
}
