using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Web.Auth;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Tenancy;

public enum TenantResolution { Ok, NotFound, NotMember }

/// <summary>
/// 주소의 부서 코드로 이 요청의 부서와 내 소속을 정한다(API 계약 1-2). 화면(TenantPageFilter)과
/// JSON API(TenantEndpointFilter)가 같이 쓴다.
/// </summary>
public sealed class TenantResolver(IAppDb db, RequestScope scope, TenantContext tenant)
{
    public async Task<TenantResolution> ResolveAsync(HttpContext http, string slug)
    {
        var ct = http.RequestAborted;
        Tenant? t;
        await using (var ctx = db.Create())
            t = await ctx.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug && x.Status == TenantStatus.Active, ct);
        if (t is null) return TenantResolution.NotFound;

        scope.SetTenant(t.Id);
        Membership? me = null;
        if (http.User.AccountId() is int accountId)
        {
            await using var ctx = db.Create();   // 영역을 정한 뒤 새 연결(세션 값 TenantId)
            me = await ctx.Memberships.AsNoTracking().Include(m => m.Roles).SingleOrDefaultAsync(m => m.AccountId == accountId, ct);
        }
        tenant.Set(t, me);
        return tenant.IsApproved ? TenantResolution.Ok : TenantResolution.NotMember;
    }
}
