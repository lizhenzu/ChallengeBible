using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/accounts?q=&amp;id=` 계정 찾기(확정 시안: 플랫폼-계정찾기, 0035 · 0029). 플랫폼 관리자만.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class AccountsModel(PlatformAccountService accounts, ImpersonationService imp) : PageModel, ITitled
{
    public string Title => "계정 찾기";
    private int Me => User.AccountId()!.Value;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }

    public List<AccountHit> Hits { get; private set; } = [];
    public AccountDetail? Detail { get; private set; }
    /// <summary>방금 만든 임시 비밀번호 — 이 응답에서 한 번만 보이고 저장하지 않는다.</summary>
    public string? TemporaryPassword { get; private set; }
    public string? Error { get; private set; }
    public bool AskMarks { get; private set; }

    public bool AskImp { get; private set; }

    public async Task OnGetAsync(bool askMarks, bool askImp, CancellationToken ct)
    {
        AskMarks = askMarks;
        AskImp = askImp;
        await LoadAsync(ct);
    }

    /// <summary>가장 시작(ADR 0067): 확인 뒤 이 계정으로 로그인을 바꾸고 그 계정의 첫 화면으로.</summary>
    public async Task<IActionResult> OnPostImpersonateAsync(CancellationToken ct)
    {
        var (e, target) = Id is int id ? await imp.StartAsync(Me, id, ct) : (ImpersonateError.NotFound, null);
        if (target is null)
        {
            Error = ImpersonationService.Message(e);
            await LoadAsync(ct);
            return Page();
        }
        await CurrentUser.SignInAsync(HttpContext, target, Me);
        return Redirect("/");
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";   // 임시 비밀번호가 보이는 응답은 브라우저에 남기지 않는다
        if (Id is int id) TemporaryPassword = await accounts.ResetPasswordAsync(Me, id, ct);
        if (TemporaryPassword is null) Error = "이 계정은 여기서 초기화할 수 없어요.";
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostResetMarksAsync(string? confirm, CancellationToken ct)
    {
        var n = Id is int id ? await accounts.ResetBibleMarksAsync(Me, id, confirm, ct) : null;
        if (n is null)
        {
            AskMarks = true;
            Error = "이메일이 맞지 않아요. 똑같이 써 주세요.";
            await LoadAsync(ct);
            return Page();
        }
        return RedirectToPage(new { Q, Id, Ok = "marks" });
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Hits = await accounts.SearchAsync(Me, Q, ct);
        var id = Id ?? (Hits.Count > 0 ? Hits[0].Id : null);
        Detail = id is int x ? await accounts.DetailAsync(Me, x, ct) : null;
        Id = Detail?.Id;
    }

    public string ImpUrl(int id) => $"/platform/accounts?q={Uri.EscapeDataString(Q ?? "")}&id={id}&askImp=true#imp";

    public string Url(int id, bool askMarks = false) =>
        $"/platform/accounts?q={Uri.EscapeDataString(Q ?? "")}&id={id}" + (askMarks ? "&askMarks=true#marks" : "");

    public static string StatusLabel(string s) => s switch
    {
        Infrastructure.Data.MembershipStatus.Requested => "승인 기다림",
        Infrastructure.Data.MembershipStatus.Suspended => "정지",
        Infrastructure.Data.MembershipStatus.PromotionPending => "진급 중",
        Infrastructure.Data.MembershipStatus.Released => "나감",
        _ => "",
    };
}
