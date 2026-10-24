using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Account;

/// <summary>
/// `/account/family` 가족과 함께할 부서(확정 시안: 가족부서고르기, 0046 · R-168). 보호자 계정만 — 아니면 부서 고르기로.
/// JS 없이: 부서를 고르고 정하기 → 멈추는 곳이 있으면 "바뀌는 점"과 확인 체크를 보여 주고, 체크해야 바꾼다.
/// </summary>
public class FamilyModel(FamilyTenantService family) : PageModel, ITitled
{
    public string Title => "가족과 함께할 부서";

    [BindProperty(SupportsGet = true)] public int? Pick { get; set; }
    [BindProperty(SupportsGet = true)] public int? Done { get; set; }

    public FamilyChoice Choice { get; private set; } = null!;
    public bool Confirming { get; private set; }
    public string? Error { get; private set; }
    private int Me => User.AccountId()!.Value;

    public FamilyOption? Picked => Choice.Options.FirstOrDefault(o => o.TenantId == (Pick ?? Choice.Current));
    public FamilyOption? DoneOption => Done is { } d ? Choice.Options.FirstOrDefault(o => o.TenantId == d) : null;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) => await LoadAsync(ct) ? Page() : RedirectToPage("/Account/Tenants");

    public async Task<IActionResult> OnPostAsync(int pick, bool agree, int? was, CancellationToken ct)
    {
        if (!await LoadAsync(ct)) return RedirectToPage("/Account/Tenants");
        Pick = pick;
        if (Choice.Options.All(o => o.TenantId != pick)) { Error = "고를 수 있는 부서가 아니에요."; return Page(); }
        if (pick == Choice.Current) { Error = "지금 함께하는 부서예요."; return Page(); }
        // 멈추는 곳이 있으면 먼저 알려 주고 확인받는다(0046 결정 4)
        if (Choice.StopsIf(pick).Count > 0 && !agree)
        {
            Confirming = true;
            if (was == pick) Error = "바꾸기 전에 \"위 내용을 확인했어요\"에 체크해 주세요.";
            return Page();
        }
        if (!await family.ChooseAsync(Me, pick, ct)) { Error = "고를 수 있는 부서가 아니에요."; return Page(); }
        return RedirectToPage(new { Done = pick });
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        var c = await family.GetAsync(Me, ct);
        if (c is null) return false;
        Choice = c;
        return true;
    }
}
