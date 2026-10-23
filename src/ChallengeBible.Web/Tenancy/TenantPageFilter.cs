using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ChallengeBible.Web.Tenancy;

/// <summary>
/// `{slug}` 가 있는 화면에서 부서를 정한다(0008).
/// 없거나 정지된 부서 → 404, 승인된 소속이 없으면 그 부서 코드가 채워진 참여 신청으로(R-130, 0045),
/// 역할이 모자라면 403.
/// </summary>
public sealed class TenantPageFilter(TenantResolver resolver, TenantContext tenant) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (context.RouteData.Values["slug"] is not string slug)
        {
            await next();
            return;
        }
        switch (await resolver.ResolveAsync(context.HttpContext, slug))
        {
            case TenantResolution.NotFound:
                context.Result = new NotFoundResult();
                return;
            case TenantResolution.NotMember:
                context.Result = new RedirectResult($"/join?code={tenant.Current.JoinCode.Trim()}");
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
