using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Account;

/// <summary>
/// `/account` 내 계정(확정 시안: 내계정): 이름 바꾸기 · 이메일 보기 · 내 부서 · 비밀번호 바꾸기(지금 비밀번호 확인) · 로그아웃.
/// 이메일은 로그인 주소라 바꾸지 않는다(확정).
/// </summary>
public class IndexModel(AccountService accounts, MyAccountService mine) : PageModel, ITitled
{
    public string Title => "내 계정";

    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }
    [BindProperty] public string? DisplayName { get; set; }
    [BindProperty] public string Current { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string PasswordConfirm { get; set; } = "";

    public string Name { get; private set; } = "";
    public string Email { get; private set; } = "";
    public DateOnly Joined { get; private set; }
    public IReadOnlyList<MyDept> Depts { get; private set; } = [];
    public FieldErrors Errors { get; private set; } = new();
    /// <summary>오류가 난 칸은 펼친 채로 다시 보여 준다.</summary>
    public string? Open { get; private set; }
    private int Me => User.AccountId()!.Value;

    public bool IsGuardian => Depts.Any(d => d.Roles.Contains(Roles.Guardian));

    public string OkMessage => Ok switch { "name" => "이름을 바꿨어요", "pw" => "비밀번호를 바꿨어요", _ => "" };

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostNameAsync(CancellationToken ct)
    {
        if (User.ImpersonatorId() is not null) return Forbid();   // 가장 중에는 이름 · 비밀번호를 바꾸지 않는다(0067)
        var (name, errors) = await accounts.RenameAsync(Me, DisplayName, ct);
        if (name is null) { Errors = errors; Open = "name"; await LoadAsync(ct); return Page(); }
        await CurrentUser.RenamedAsync(HttpContext, name);
        return RedirectToPage(new { Ok = "name" });
    }

    public async Task<IActionResult> OnPostPasswordAsync(CancellationToken ct)
    {
        if (User.ImpersonatorId() is not null) return Forbid();
        Errors = await accounts.ChangeOwnPasswordAsync(Me, Current, Password, PasswordConfirm, ct);
        if (!Errors.Ok) { Open = "pw"; await LoadAsync(ct); return Page(); }
        return RedirectToPage(new { Ok = "pw" });
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var (name, email, created) = await accounts.ProfileAsync(Me, ct);
        (Name, Email) = (name, email);
        Joined = TenantClock.ToTenantDate(new DateTimeOffset(DateTime.SpecifyKind(created, DateTimeKind.Utc)), "Asia/Seoul");
        Depts = await mine.DeptsAsync(Me, ct);
    }

    public static string RoleLabel(string r) => r switch
    {
        Roles.Participant => "참여자", Roles.Guardian => "보호자", Roles.Teacher => "교사", Roles.TenantAdmin => "관리자", _ => r,
    };
}
