using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages;

/// <summary>`/{slug}` 부서 홈 자리. 부서 결정 · 소속 검사는 TenantPageFilter 가 한다. 홈 내용은 미션 단계에서 채운다.</summary>
public class TenantHomeModel(TenantContext tenant, FamilyTenantService family) : PageModel, ITitled
{
    public string Title => tenant.Current.Name;
    public TenantContext Tenant => tenant;
    /// <summary>보호자인데 가족과 함께할 부서를 아직 고르지 않음 → "골라 주세요" 카드(0046 결과).</summary>
    public bool NeedsFamilyChoice { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (tenant.Has(Roles.Guardian)) NeedsFamilyChoice = await family.NeedsChoiceAsync(User.AccountId()!.Value, ct);
    }
}
