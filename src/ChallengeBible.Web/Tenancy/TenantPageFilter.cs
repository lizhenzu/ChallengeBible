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
            case TenantResolution.NotMember when context.HttpContext.User.Identity?.IsAuthenticated != true && context.HandlerInstance is Pages.TenantHomeModel && HttpMethods.IsGet(context.HttpContext.Request.Method):
                break;   // 로그인 안 한 사람의 부서 홈 둘러보기(R-336) — 개인 정보 없는 화면은 TenantHomeModel 이 고른다
            case TenantResolution.NotMember when context.HandlerInstance is IPublicTenantPage && HttpMethods.IsGet(context.HttpContext.Request.Method):
                break;   // 누구나 보는 부서 화면(주보 보기 · 파일, ADR 0117) — 그 부서 데이터만 보인다(RLS + 전역 필터)
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

/// <summary>소속이 없어도(로그인 안 해도) GET 으로 볼 수 있는 부서 화면 표시(ADR 0117 주보). 고치기(POST)는 소속 · 역할을 그대로 확인한다.</summary>
public interface IPublicTenantPage;
