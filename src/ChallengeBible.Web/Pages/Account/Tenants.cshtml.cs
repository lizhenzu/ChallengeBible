using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Account;

/// <summary>`/account/tenants` 부서 고르기(0045) — 승인된 소속이 둘 이상일 때. 1차 간단판(오늘 한 줄은 미션 기능 뒤에).</summary>
public class TenantsModel(AccountService accounts, FamilyTenantService family) : PageModel, ITitled
{
    public string Title => "부서 고르기";
    public List<MyMembership> Approved { get; private set; } = [];
    public List<MyMembership> Waiting { get; private set; } = [];
    /// <summary>보호자 계정이면 가족과 함께할 부서(0046) — 아래 줄 "가족과 함께하는 부서는 … · 바꾸기". 보호자가 아니면 null.</summary>
    public FamilyChoice? Family { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var me = User.AccountId()!.Value;
        var mine = await accounts.MyMembershipsAsync(me, ct);
        Approved = mine.Where(m => m.Status == MembershipStatus.Approved && m.TenantActive).ToList();
        Waiting = mine.Where(m => m.Status == MembershipStatus.Requested).ToList();
        Family = await family.GetAsync(me, ct);
    }
}
