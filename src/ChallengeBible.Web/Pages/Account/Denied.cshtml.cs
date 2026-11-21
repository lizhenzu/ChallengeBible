using ChallengeBible.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Account;

public class DeniedModel : PageModel, ITitled
{
    public string Title => "볼 수 없음";

    /// <summary>
    /// 가장 중(사람 바꾸기 등)에 바뀐 사람이 볼 수 없는 화면이면 막힌 화면 대신 그 부서 홈으로(2026-11-21 사용자:
    /// 소속 관리에서 학생으로 바꾸면 "볼 수 없는 화면" 이 떴다). 가장이 아니면 그대로 이 화면.
    /// </summary>
    public IActionResult OnGet(string? returnUrl)
    {
        if (User.ImpersonatorId() is not null && Home(returnUrl) is { } home) return LocalRedirect(home);
        return Page();
    }

    public static string? Home(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//")) return null;
        var first = returnUrl.TrimStart('/').Split('/', '?', '#')[0];
        if (first.Length == 0 || first is "account" or "platform" or "test-tools" or "messages") return null;
        return "/" + Uri.EscapeDataString(Uri.UnescapeDataString(first));
    }
}
