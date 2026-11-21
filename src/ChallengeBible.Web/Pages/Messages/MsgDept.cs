using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using ChallengeBible.Web.Tenancy;

namespace ChallengeBible.Web.Pages.Messages;

/// <summary>쪽지 화면 머리말(R-360, 2026-11-21 사용자 · 확정 시안 쪽지읽기-취소): 부서가 있으면 ☰ 메뉴판 머리말(_GenTop), 없으면 옛 머리말(_Top).</summary>
public static class MsgDept
{
    public static async Task<bool> UseAsync(HttpContext http, MessageService messages, TenantResolver resolver, CancellationToken ct)
    {
        if (http.User.UserName() is not { } me || await messages.HomeSlugAsync(me, ct) is not { } slug) return false;
        return await resolver.ResolveAsync(http, slug) == TenantResolution.Ok;
    }
}
