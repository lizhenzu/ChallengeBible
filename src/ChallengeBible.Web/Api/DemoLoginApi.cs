using ChallengeBible.Web.Services;

namespace ChallengeBible.Web.Api;

/// <summary>
/// 영상 촬영용 임시 데모 로그인(ADR 0143): GET /demo/admin?key=… · /demo/user?key=…
/// 설정이 꺼져 있거나 기한이 지났거나 키가 틀리면 404. 끝나면 appsettings 의 DemoLogin:Enabled 를 false 로 하거나 이 파일 · 서비스 두 개 · Program.cs 두 줄을 지운다.
/// </summary>
public static class DemoLoginApi
{
    public static void MapDemoLogin(this WebApplication app)
    {
        app.MapGet("/demo/{who}", async (string who, string? key, DemoLoginService demo, HttpContext h) =>
        {
            h.Response.Headers.CacheControl = "no-store";
            var to = await demo.SignInAsync(who, key, h, h.RequestAborted);
            return to is null ? Results.NotFound() : Results.Redirect(to);
        }).ExcludeFromDescription();
    }
}
