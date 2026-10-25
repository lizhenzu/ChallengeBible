using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Account;

/// <summary>
/// `/account/family` 온가족(확정 시안: 온가족, 0065 · R-175 ~ R-179). 로그인한 누구나.
/// 온가족이 있으면 누적 총점 · 구성원(보기만), 없으면 신청 · 신청 기다림. 보호자면 시상 부서를 보기만 한다(플랫폼 관리자가 정함).
/// (0046 의 "가족과 함께할 부서" 고르기 · 바꾸기 화면은 이것으로 바뀌었다.)
/// </summary>
public class FamilyModel(OnFamilyService families) : PageModel, ITitled
{
    public string Title => "온가족";
    private int Me => User.AccountId()!.Value;

    public MyOnFamily View { get; private set; } = null!;
    [BindProperty] public string? Body { get; set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => View = await families.MyAsync(Me, ct);

    public async Task<IActionResult> OnPostRequestAsync(CancellationToken ct)
    {
        var e = await families.RequestAsync(Me, Body, ct);
        if (e == OnFamilyError.None) return RedirectToPage();
        Error = OnFamilyService.Message(e);
        View = await families.MyAsync(Me, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(CancellationToken ct)
    {
        await families.CancelAsync(Me, ct);
        return RedirectToPage();
    }

    /// <summary>둥근 표시 글자: 이름의 두 번째 글자(김철수 → 철), 한 글자 이름이면 그 글자.</summary>
    public static string Initial(string name) => name.Length >= 2 ? name[1..2] : name.Length == 1 ? name : "?";

    /// <summary>마지막 참여일 → "오늘" · "어제" · "N일 전". 점수가 없으면 null.</summary>
    public static string? Ago(DateOnly? last, DateOnly today) => last switch
    {
        null => null,
        { } d when d >= today => "오늘",
        { } d when d == today.AddDays(-1) => "어제",
        { } d => $"{today.DayNumber - d.DayNumber}일 전",
    };
}
