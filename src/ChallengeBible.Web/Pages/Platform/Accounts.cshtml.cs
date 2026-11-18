using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>`/platform/accounts?q=&amp;id=` 계정 찾기(확정 시안: 플랫폼-계정찾기, 0035 · 0029). 플랫폼 관리자만.</summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class AccountsModel(PlatformAccountService accounts, ImpersonationService imp, AccountService signed, UserNameGate names) : PageModel, ITitled
{
    public string Title => "계정 찾기";
    private string Me => User.UserName()!;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }
    /// <summary>가장한 뒤 갈 곳(플랫폼 홈 부서 이름 → 그 부서 관리 홈, 2026-11-18). 이 사이트 안 주소만.</summary>
    [BindProperty(SupportsGet = true)] public string? Go { get; set; }

    public List<AccountHit> Hits { get; private set; } = [];
    public AccountDetail? Detail { get; private set; }
    /// <summary>방금 만든 임시 비밀번호 — 이 응답에서 한 번만 보이고 저장하지 않는다.</summary>
    public string? TemporaryPassword { get; private set; }
    public string? Error { get; private set; }
    public bool AskMarks { get; private set; }

    public bool AskImp { get; private set; }

    public bool AskRename { get; private set; }
    public DeptMoveOptions? Move { get; private set; }
    public bool AskMove { get; private set; }

    public async Task OnGetAsync(bool askMarks, bool askImp, bool askRename, bool askMove, CancellationToken ct)
    {
        AskMove = askMove;
        AskMarks = askMarks;
        AskRename = askRename;
        AskImp = askImp;
        await LoadAsync(ct);
    }

    /// <summary>가장 시작(ADR 0067): 확인 뒤 이 계정으로 로그인을 바꾸고 그 계정의 첫 화면으로.</summary>
    public async Task<IActionResult> OnPostImpersonateAsync(CancellationToken ct)
    {
        var (e, target) = Id is { } id ? await imp.StartAsync(Me, id, ct) : (ImpersonateError.NotFound, null);
        if (target is null)
        {
            Error = ImpersonationService.Message(e);
            await LoadAsync(ct);
            return Page();
        }
        await CurrentUser.SignInAsync(HttpContext, await signed.ToSignedInAsync(target, ct), Me);
        if (SafeGo(Go) is { } go) return Redirect(go);   // 부서 관리 홈으로 바로
        TempData[Account.LoginModel.TodoCheckKey] = "1";   // 가장도 로그인 직후처럼: 할 일이 있으면 할 일 화면으로(R-309, 2026-11-16 사용자)
        return Redirect("/");
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";   // 임시 비밀번호가 보이는 응답은 브라우저에 남기지 않는다
        if (Id is { } id) TemporaryPassword = await accounts.ResetPasswordAsync(Me, id, ct);
        if (TemporaryPassword is null) Error = "이 계정은 여기서 초기화할 수 없어요.";
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostResetMarksAsync(string? confirm, CancellationToken ct)
    {
        var n = Id is { } id ? await accounts.ResetBibleMarksAsync(Me, id, confirm, ct) : null;
        if (n is null)
        {
            AskMarks = true;
            Error = "이메일이 맞지 않아요. 똑같이 써 주세요.";
            await LoadAsync(ct);
            return Page();
        }
        return RedirectToPage(new { Q, Id, Ok = "marks" });
    }

    /// <summary>아이디 바꾸기(ADR 0090): 옛 아이디의 로그인은 바로 끊긴다.</summary>
    public async Task<IActionResult> OnPostRenameAsync(string? newName, string? confirm, CancellationToken ct)
    {
        var old = Id;
        var error = old is null ? "계정을 찾을 수 없어요." : await accounts.RenameAsync(Me, old, newName, confirm, ct);
        if (error is not null)
        {
            AskRename = true;
            Error = error;
            await LoadAsync(ct);
            return Page();
        }
        names.Forget(old!);
        var moved = newName!.Trim();
        return RedirectToPage(new { Q = moved, Id = moved, Ok = "renamed" });
    }

    /// <summary>[부서 바로 옮기기](2026-11-18 사용자 확정 시안): 받는 부서 승인 없이 옮긴다.</summary>
    public async Task<IActionResult> OnPostMoveDeptAsync(int target, string? role, CancellationToken ct)
    {
        var (error, moved) = Id is { } id ? await accounts.MoveDeptAsync(Me, id, target, role, ct) : ("계정을 찾을 수 없어요.", 0);
        if (error is not null)
        {
            AskMove = true;
            Error = error;
            await LoadAsync(ct);
            return Page();
        }
        TempData["moved"] = moved;
        return RedirectToPage(new { Q, Id, Ok = "moved" });
    }

    public string MoveUrl(string id) => Url(id) + "&askMove=true#move";

    private async Task LoadAsync(CancellationToken ct)
    {
        Hits = await accounts.SearchAsync(Me, Q, ct);
        var id = Id ?? (Hits.Count > 0 ? Hits[0].Id : null);
        Detail = id is { } x ? await accounts.DetailAsync(Me, x, ct) : null;
        Id = Detail?.Id;
        Move = Detail is null ? null : await accounts.MoveOptionsAsync(Me, Detail.Id, ct);
    }

    public string ImpUrl(string id) => $"/platform/accounts?q={Uri.EscapeDataString(Q ?? "")}&id={Uri.EscapeDataString(id)}&askImp=true#imp";

    /// <summary>플랫폼 홈 부서 이름 → 그 부서 관리자로 가장 확인 → 관리 홈.</summary>
    public static string ImpToAdminUrl(string adminUser, string slug) =>
        $"/platform/accounts?q={Uri.EscapeDataString(adminUser)}&id={Uri.EscapeDataString(adminUser)}&askImp=true&go={Uri.EscapeDataString($"/{slug}/admin")}#imp";

    public static string? SafeGo(string? go) => go is { Length: > 1 } g && g[0] == '/' && !g.StartsWith("//") && !g.StartsWith("/\\") ? g : null;

    public string RenameUrl(string id) => Url(id) + "&askRename=true#rename";

    public string Url(string id, bool askMarks = false) =>
        $"/platform/accounts?q={Uri.EscapeDataString(Q ?? "")}&id={Uri.EscapeDataString(id)}" + (askMarks ? "&askMarks=true#marks" : "");

    public static string StatusLabel(string s) => s switch
    {
        Infrastructure.Data.MembershipStatus.Requested => "승인 기다림",
        Infrastructure.Data.MembershipStatus.Rejected => "거절됨",
        _ => "",
    };
}
