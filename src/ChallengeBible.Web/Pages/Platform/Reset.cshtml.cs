using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/reset` 완전 초기화(ADR 0102, R-314): 플랫폼 관리자만, 체크한 것만 모든 부서에서 지운다. 확인 글자 RESET. 관리자 · 부서 · 반 · 상품 · 설정 · 콘텐츠는 절대 안 지움.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class ResetModel(ResetService reset) : PageModel, ITitled
{
    public string Title => "완전 초기화";
    public ResetStatus? Status { get; private set; }
    /// <summary>다시 그릴 때 체크 상태(null = 처음 · 모두 체크).</summary>
    public HashSet<string>? Picked { get; private set; }
    public string? Result { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => Status = await reset.StatusAsync(User.UserName()!, ct);

    public async Task<IActionResult> OnPostAsync(string[]? pick, string? confirm, CancellationToken ct)
    {
        Picked = [.. pick ?? []];
        if (!string.Equals(confirm?.Trim(), ResetService.ConfirmWord, StringComparison.Ordinal))
            Error = $"확인 칸에 {ResetService.ConfirmWord} 를 써야 초기화해요.";
        else if (ResetService.Effective(Picked).Count == 0)
            Error = "지울 것을 하나 이상 체크해 주세요.";
        else
        {
            Result = await reset.ResetAsync(User.UserName()!, Picked, ct);
            Picked = null;
        }
        Status = await reset.StatusAsync(User.UserName()!, ct);
        return Page();
    }
}
