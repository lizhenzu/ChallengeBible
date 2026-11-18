using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Api;

/// <summary>
/// 부서별 앱 정보(0038 결정 5): `/{slug}/manifest.webmanifest` · `/{slug}/icon.svg`(→ 네잎 아이콘, 0114).
/// 브라우저는 매니페스트를 쿠키 없이 읽으므로 로그인 없이 연다 — 부서 이름 · 색처럼 공개해도 되는 값만 돌려준다.
/// </summary>
public static class BrandingApi
{
    public static void MapBrandingApi(this WebApplication app)
    {
        app.MapGet("/{slug}/manifest.webmanifest", async (string slug, IAppDb db, CancellationToken ct) =>
        {
            if (await FindAsync(db, slug, ct) is not { } t) return Results.NotFound();
            var manifest = new
            {
                name = t.AppName ?? BrandingService.DefaultName,
                short_name = t.AppShortName ?? BrandingService.DefaultShortName,
                start_url = $"/{t.Slug}/",
                scope = $"/{t.Slug}/",
                display = "standalone",
                theme_color = BrandingService.ColorHex(t.ThemeColor),
                background_color = "#FFFFFF",
                lang = "ko",
                // 아이콘은 모든 부서가 같은 네잎(0114). PNG 192 · 512 는 안드로이드 설치 조건, maskable 은 둥근 모양 휴대폰용.
                // 안드로이드 로딩 화면은 background_color(흰색) + 아이콘 + name 으로 휴대폰이 그린다.
                icons = new[]
                {
                    new { src = BrandingService.Icon192, sizes = "192x192", type = "image/png", purpose = "any" },
                    new { src = BrandingService.Icon512, sizes = "512x512", type = "image/png", purpose = "any" },
                    new { src = BrandingService.IconMaskable, sizes = "512x512", type = "image/png", purpose = "maskable" },
                    new { src = BrandingService.IconSvg, sizes = "any", type = "image/svg+xml", purpose = "any" },
                },
            };
            return Results.Json(manifest, contentType: "application/manifest+json");
        }).AllowAnonymous();

        // 예전 부서 아이콘 주소(이미 설치한 앱이 들고 있을 수 있다)는 네잎 아이콘으로 보낸다(0114)
        app.MapGet("/{slug}/icon.svg", (string slug) => Results.Redirect(BrandingService.IconSvg)).AllowAnonymous();
    }

    private static async Task<Tenant?> FindAsync(IAppDb db, string slug, CancellationToken ct)
    {
        await using var ctx = db.Create();
        return await ctx.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug && x.Status == TenantStatus.Active, ct);
    }
}
