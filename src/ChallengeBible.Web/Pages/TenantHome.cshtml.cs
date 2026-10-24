using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages;

/// <summary>`/{slug}` 부서 홈 메인(확정 시안: 홈메인, 0044). 부서 결정 · 소속 검사는 TenantPageFilter 가 한다.</summary>
public class TenantHomeModel(TenantContext tenant, FamilyTenantService family, FamilyTeamService teams, HomeService home, AccountService accounts) : PageModel, ITitled
{
    public string Title => "";
    public TenantContext Tenant => tenant;
    /// <summary>보호자인데 가족과 함께할 부서를 아직 고르지 않음 → "골라 주세요" 카드(0046 결과).</summary>
    public bool NeedsFamilyChoice { get; private set; }
    /// <summary>"우리 가족팀" 칸(확정 시안 홈메인) — 내가 든 활성 가족팀이 없으면 null.</summary>
    public FamilyHome? Family { get; private set; }
    /// <summary>부모님(보호자)에게만: 가족과 함께하는 부서 이름 · 바꾸기(0046).</summary>
    public string? FamilyDept { get; private set; }
    public bool IsGuardian => tenant.Has(Roles.Guardian);
    public HomeView Data { get; private set; } = null!;
    /// <summary>승인된 소속이 둘 이상이면 머리말 "부서 ▼" → 부서 고르기(확정 6).</summary>
    public bool ManyTenants { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var me = User.AccountId()!.Value;
        if (IsGuardian) NeedsFamilyChoice = await family.NeedsChoiceAsync(me, ct);
        if (tenant.Current.MissionEnabled) Family = await teams.MyFamilyAsync(ct);
        if (Family is not null && IsGuardian) FamilyDept = await family.CurrentNameAsync(me, ct);
        Data = await home.GetAsync(ct);
        ManyTenants = (await accounts.WhereToGoAsync(me, ct)).ApprovedCount > 1;
    }
}
