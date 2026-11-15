using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/year-reset` 새해 초기화(ADR 0094 정한 것 17): 플랫폼 관리자 버튼, 확인 글자 RESET, 모든 부서를 한 번에 완전히 지운다(되돌릴 수 없음). 아무 때나 몇 번이든.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class YearResetModel(YearResetService reset) : PageModel, ITitled
{
    public string Title => "새해 초기화";
    public YearResetStatus? Status { get; private set; }
    public YearResetCounts? Result { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => Status = await reset.StatusAsync(User.UserName()!, ct);

    public async Task<IActionResult> OnPostAsync(string? confirm, CancellationToken ct)
    {
        if (!string.Equals(confirm?.Trim(), YearResetService.ConfirmWord, StringComparison.Ordinal))
            Error = $"확인 칸에 {YearResetService.ConfirmWord} 를 써야 초기화해요.";
        else
            Result = await reset.ResetAsync(User.UserName()!, ct);
        Status = await reset.StatusAsync(User.UserName()!, ct);
        return Page();
    }
}
