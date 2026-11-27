using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages;

/// <summary>
/// `/install` 앱 설치 안내(ADR 0115, 확정 시안 설치안내). 브라우저로 들어온 사람을 install-gate.js 가 이리로 보낸다.
/// 기기 판별(안드로이드 · 아이폰 · 카톡 같은 앱 안 · PC)과 설치는 install.js 가 한다. 다 보면 `back`(같은 사이트 주소만)으로 돌아간다.
/// </summary>
public class InstallModel : PageModel, ITitled
{
    public string Title => "앱 설치";
    public string Back { get; private set; } = "/";

    public void OnGet(string? back) => Back = SafeBack(back);

    /// <summary>돌아갈 주소: 이 사이트 안의 주소(/로 시작, // · /\ 아님)만, /install 자신은 첫 화면으로(R-330).</summary>
    public static string SafeBack(string? back)
    {
        if (string.IsNullOrEmpty(back) || back[0] != '/' || back.StartsWith("//") || back.StartsWith("/\\")) return "/";
        return back.StartsWith("/install", StringComparison.OrdinalIgnoreCase) ? "/" : back;
    }
}
