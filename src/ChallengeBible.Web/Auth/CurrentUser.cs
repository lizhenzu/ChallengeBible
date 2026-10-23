using System.Security.Claims;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ChallengeBible.Web.Auth;

/// <summary>쿠키 클레임(0034, X-20): 계정 Id · 이름 · 플랫폼 관리자 여부만 둔다. 영역 · 역할은 요청마다 DB 에서 읽는다.</summary>
public static class CurrentUser
{
    public const string AccountIdClaim = "aid";
    public const string PlatformAdminClaim = "padmin";
    /// <summary>플랫폼 화면 정책. 서비스는 DB 로 한 번 더 확인한다.</summary>
    public const string PlatformAdminPolicy = "PlatformAdmin";

    public static bool IsPlatformAdmin(this ClaimsPrincipal user) => user.HasClaim(PlatformAdminClaim, "1");

    public static int? AccountId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(AccountIdClaim), out var id) ? id : null;

    public static string DisplayName(this ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Name) ?? "";

    public static Task SignInAsync(HttpContext http, Account account)
    {
        var claims = new List<Claim>
        {
            new(AccountIdClaim, account.Id.ToString()),
            new(ClaimTypes.Name, account.DisplayName),
        };
        if (account.IsPlatformAdmin) claims.Add(new(PlatformAdminClaim, "1"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = true });
    }

    /// <summary>요청 처음에 쿠키의 계정을 RequestScope 에 넣는다(이후 DB 세션 값 AccountId).</summary>
    public static IApplicationBuilder UseRequestScope(this IApplicationBuilder app) =>
        app.Use(async (http, next) =>
        {
            if (http.User.AccountId() is int id) http.RequestServices.GetRequiredService<RequestScope>().SetAccount(id);
            await next();
        });
}
