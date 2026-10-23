using System.Reflection;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Tenancy;

/// <summary>
/// `{slug}` 가 있는 화면에서 부서를 정한다(0008).
/// 없거나 정지된 부서 → 404, 승인된 소속이 없으면 그 부서 코드가 채워진 참여 신청으로(R-130, 0045),
/// 역할이 모자라면 403.
/// </summary>
public sealed class TenantPageFilter(IAppDb db, RequestScope scope, TenantContext tenant) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (context.RouteData.Values["slug"] is not string slug)
        {
            await next();
            return;
        }

        var ct = context.HttpContext.RequestAborted;
        Tenant? t;
        await using (var ctx = db.Create())
            t = await ctx.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug && x.Status == TenantStatus.Active, ct);
        if (t is null)
        {
            context.Result = new NotFoundResult();
            return;
        }

        scope.SetTenant(t.Id);
        Membership? me = null;
        if (context.HttpContext.User.AccountId() is int accountId)
        {
            await using var ctx = db.Create();   // 영역을 정한 뒤 새 연결(세션 값 TenantId)
            me = await ctx.Memberships.AsNoTracking().Include(m => m.Roles).SingleOrDefaultAsync(m => m.AccountId == accountId, ct);
        }
        tenant.Set(t, me);

        if (!tenant.IsApproved)
        {
            context.Result = new RedirectResult($"/join?code={t.JoinCode.Trim()}");
            return;
        }
        var need = context.HandlerInstance.GetType().GetCustomAttribute<TenantRolesAttribute>();
        if (need is not null && !need.Roles.Any(tenant.Has))
        {
            context.Result = new ForbidResult();
            return;
        }
        await next();
    }
}
