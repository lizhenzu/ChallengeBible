using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Account;

/// <summary>
/// 사람 바꾸기(확정 시안 사람바꾸기, ADR 0096). 가장 중일 때만(쿠키의 ImpersonatorId), 가장한 관리자가 지금도 플랫폼 관리자일 때만.
/// GET = 목록(frag=1 이면 창 안 내용만 — imp-switch.js), POST = 바꾸기 → 같은 부서 화면이면 그대로, 아니면 그 부서 홈.
/// </summary>
public class ImpersonateSwitchModel(UserSwitchService switcher, AccountService accounts) : PageModel, ITitled
{
    public string Title => "사람 바꾸기";
    [BindProperty(SupportsGet = true)] public int? Dept { get; set; }
    [BindProperty(SupportsGet = true)] public string? Role { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int N { get; set; } = UserSwitchService.PageSize;
    [BindProperty(SupportsGet = true)] public string? Back { get; set; }
    [BindProperty(SupportsGet = true)] public bool Frag { get; set; }
    public SwitchList? List { get; private set; }
    public string? Error { get; private set; }
    public string Me => User.UserName() ?? "";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (User.ImpersonatorId() is not { } admin) return NotFound();
        List = await switcher.ListAsync(admin, Me, Dept, Role, Q, Math.Clamp(N, UserSwitchService.PageSize, 2000), ct);
        if (List is null) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string to, CancellationToken ct)
    {
        if (User.ImpersonatorId() is not { } admin) return NotFound();
        var (e, target, slug) = await switcher.SwitchAsync(admin, Me, to, ct);
        if (target is null)
        {
            Error = ImpersonationService.Message(e);
            List = await switcher.ListAsync(admin, Me, Dept, Role, Q, UserSwitchService.PageSize, ct);
            if (List is null) return NotFound();
            Frag = false;
            return Page();
        }
        await CurrentUser.SignInAsync(HttpContext, await accounts.ToSignedInAsync(target, ct), admin);
        return LocalRedirect(UserSwitchService.Next(Back, slug));
    }

    public string Url(int? dept = null, string? role = null, string? q = null, int? n = null, bool frag = false)
    {
        var p = new List<string>();
        if (dept is int d) p.Add($"dept={d}");
        if (!string.IsNullOrEmpty(role) && role != "all") p.Add($"role={role}");
        if (!string.IsNullOrEmpty(q)) p.Add($"q={Uri.EscapeDataString(q)}");
        if (n is int k && k > UserSwitchService.PageSize) p.Add($"n={k}");
        if (!string.IsNullOrEmpty(Back)) p.Add($"back={Uri.EscapeDataString(Back)}");
        if (frag) p.Add("frag=true");
        return "/account/impersonate/switch" + (p.Count == 0 ? "" : "?" + string.Join("&", p));
    }
}
