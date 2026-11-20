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
public class IndexModel(AccountService accounts, MyAccountService mine, PhotoService photos, LeaveService leave) : PageModel, ITitled
{
    /// <summary>내 사진 파일 이름(0078).</summary>
    public string? Photo { get; private set; }
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
    private string Me => User.UserName()!;

    public bool IsGuardian => Depts.Any(d => d.Roles.Contains(Roles.Guardian));

    public string OkMessage => Ok switch { "name" => "이름을 바꿨어요", "pw" => "비밀번호를 바꿨어요", "bd" => "생일 · 연락처를 저장했어요", "leave" => "탈퇴를 신청했어요", "stay" => "탈퇴 신청을 취소했어요", _ => "" };

    /// <summary>탈퇴(ADR 0119): 관리자 계정은 버튼이 없다. 신청 중이면 신청한 때.</summary>
    public bool IsAdmin { get; private set; }
    public DateTime? LeavePending { get; private set; }

    public async Task<IActionResult> OnPostLeaveAsync(string? leavePw, string? leaveWord, CancellationToken ct)
    {
        if (User.ImpersonatorId() is not null) return Forbid();
        var e = await leave.RequestAsync(Me, leavePw, leaveWord, ct);
        if (e == LeaveError.None) return RedirectToPage(new { Ok = "leave" });
        Errors = new FieldErrors { ["Leave"] = LeaveService.Message(e) };
        Open = "leave";
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostStayAsync(CancellationToken ct)
    {
        if (User.ImpersonatorId() is not null) return Forbid();
        await leave.CancelAsync(Me, ct);
        return RedirectToPage(new { Ok = "stay" });
    }

    /// <summary>내 명부 줄(0093). 일반 부서에 들어가지 않았으면 null.</summary>
    public Person? Profile { get; private set; }

    public async Task<IActionResult> OnPostProfileAsync(int? month, int? day, string? phone1, string? phone2, [FromServices] TimeProvider time, CancellationToken ct)
    {
        var e = await mine.SaveProfileAsync(Me, month, day, phone1, phone2, time.GetUtcNow().UtcDateTime, ct);
        if (e == PeopleError.None) return RedirectToPage(new { Ok = "bd" });
        Errors = new FieldErrors { ["Profile"] = e == PeopleError.NotFound ? "부서에 들어가면 넣을 수 있어요." : PeopleService.Message(e) };
        Open = "bd";
        await LoadAsync(ct);
        return Page();
    }

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
        Photo = await photos.FileOfAsync(Me, ct);
        Profile = await mine.ProfileAsync(Me, ct);
        IsAdmin = await leave.IsAdminAsync(Me, ct);
        LeavePending = IsAdmin ? null : await leave.PendingAsync(Me, ct);
    }

    public static string RoleLabel(string r) => r switch
    {
        Roles.Participant => "참여자", Roles.Guardian => "보호자", Roles.Teacher => "교사", Roles.TenantAdmin => "관리자", _ => r,
    };
}
