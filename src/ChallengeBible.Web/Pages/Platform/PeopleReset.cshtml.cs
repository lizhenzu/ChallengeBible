using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/people-reset` 관리자만 남기기(확정 시안 플랫폼-관리자만남기기): 확인 글자 DELETE, 모든 부서, 되돌릴 수 없음. 플랫폼 관리자만.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class PeopleResetModel(PeopleResetService reset) : PageModel, ITitled
{
    public string Title => "관리자만 남기기";
    public PeopleResetStatus? Status { get; private set; }
    public PeopleResetResult? Result { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => Status = await reset.StatusAsync(User.UserName()!, ct);

    public async Task<IActionResult> OnPostAsync(string? confirm, CancellationToken ct)
    {
        if (!string.Equals(confirm?.Trim(), PeopleResetService.ConfirmWord, StringComparison.Ordinal))
            Error = $"확인 칸에 {PeopleResetService.ConfirmWord} 를 써야 지워요.";
        else
            Result = await reset.ResetAsync(User.UserName()!, ct);
        Status = await reset.StatusAsync(User.UserName()!, ct);
        return Page();
    }
}
