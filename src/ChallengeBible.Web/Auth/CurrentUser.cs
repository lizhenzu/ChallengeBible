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
    /// <summary>임시 비밀번호로 로그인함(0035) — 새 비밀번호를 정하기 전에는 다른 화면을 쓸 수 없다.</summary>
    public const string MustChangePasswordClaim = "mustpw";
    public const string ChangePasswordPath = "/account/change-password";
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
        if (account.MustChangePassword) claims.Add(new(MustChangePasswordClaim, "1"));
        return SignInAsync(http, claims);
    }

    private static Task SignInAsync(HttpContext http, IEnumerable<Claim> claims) =>
        http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = true });

    public static bool MustChangePassword(this ClaimsPrincipal user) => user.HasClaim(MustChangePasswordClaim, "1");

    /// <summary>새 비밀번호를 정한 뒤: 같은 쿠키에서 "바꿔야 함" 표시만 뺀다.</summary>
    public static Task PasswordChangedAsync(HttpContext http) =>
        SignInAsync(http, http.User.Claims.Where(c => c.Type != MustChangePasswordClaim).Select(c => new Claim(c.Type, c.Value)).ToList());

    /// <summary>이름을 바꾼 뒤: 같은 쿠키에서 이름 클레임만 새 이름으로(머리말 "○○님"이 바로 바뀌게).</summary>
    public static Task RenamedAsync(HttpContext http, string name) =>
        SignInAsync(http, http.User.Claims.Where(c => c.Type != ClaimTypes.Name).Select(c => new Claim(c.Type, c.Value)).Append(new Claim(ClaimTypes.Name, name)).ToList());

    /// <summary>
    /// 임시 비밀번호로 로그인했으면(0035) 새 비밀번호를 정하기 전까지 다른 화면 · API 를 막는다.
    /// 화면(GET)은 비밀번호 바꾸기로 보내고, 나머지(API · POST)는 403. 비밀번호 바꾸기 · 로그아웃 · 파일(확장자 있는 주소)은 통과.
    /// </summary>
    public static IApplicationBuilder UsePasswordChangeGate(this IApplicationBuilder app) =>
        app.Use(async (http, next) =>
        {
            if (http.User.MustChangePassword() && !Allowed(http.Request.Path))
            {
                var page = (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method))
                           && !http.Request.Path.StartsWithSegments("/api");
                if (page) http.Response.Redirect(ChangePasswordPath);
                else http.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            await next();
        });

    private static bool Allowed(PathString path) =>
        path.StartsWithSegments(ChangePasswordPath, StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/account/logout", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/error", StringComparison.OrdinalIgnoreCase)
        || Path.HasExtension(path.Value);   // css · js · 아이콘 · 매니페스트

    /// <summary>요청 처음에 쿠키의 계정을 RequestScope 에 넣는다(이후 DB 세션 값 AccountId).</summary>
    public static IApplicationBuilder UseRequestScope(this IApplicationBuilder app) =>
        app.Use(async (http, next) =>
        {
            if (http.User.AccountId() is int id) http.RequestServices.GetRequiredService<RequestScope>().SetAccount(id);
            await next();
        });
}
