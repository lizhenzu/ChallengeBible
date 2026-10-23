using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Account;

/// <summary>`/account/tenants` 부서 고르기(0045) — 승인된 소속이 둘 이상일 때. 1차 간단판(오늘 한 줄은 미션 기능 뒤에).</summary>
public class TenantsModel(AccountService accounts) : PageModel, ITitled
{
    public string Title => "부서 고르기";
    public List<MyMembership> Approved { get; private set; } = [];
    public List<MyMembership> Waiting { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        var mine = await accounts.MyMembershipsAsync(User.AccountId()!.Value, ct);
        Approved = mine.Where(m => m.Status == MembershipStatus.Approved && m.TenantActive).ToList();
        Waiting = mine.Where(m => m.Status == MembershipStatus.Requested).ToList();
    }
}
