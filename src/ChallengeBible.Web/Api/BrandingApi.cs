using System.Security;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Api;

/// <summary>
/// 부서별 앱 정보(0038 결정 5): `/{slug}/manifest.webmanifest` · `/{slug}/icon.svg`.
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
                // PNG 192 · 512 는 안드로이드 설치 조건, SVG 는 짧은 이름 글자가 들어간 부서 아이콘
                icons = new[]
                {
                    new { src = BrandingService.IconPng(t.ThemeColor, 192), sizes = "192x192", type = "image/png", purpose = "any" },
                    new { src = BrandingService.IconPng(t.ThemeColor, 512), sizes = "512x512", type = "image/png", purpose = "any" },
                    new { src = $"/{t.Slug}/icon.svg", sizes = "any", type = "image/svg+xml", purpose = "any" },
                },
            };
            return Results.Json(manifest, contentType: "application/manifest+json");
        }).AllowAnonymous();

        // 아이콘: 대표 색 바탕에 짧은 이름 앞 2자(확정: PNG 는 나중)
        app.MapGet("/{slug}/icon.svg", async (string slug, IAppDb db, CancellationToken ct) =>
        {
            if (await FindAsync(db, slug, ct) is not { } t) return Results.NotFound();
            var text = SecurityElement.Escape(BrandingService.IconText(t.AppShortName ?? BrandingService.DefaultShortName));
            var svg = $"""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512"><rect width="512" height="512" rx="112" fill="{BrandingService.ColorHex(t.ThemeColor)}"/><text x="256" y="256" dy=".35em" text-anchor="middle" font-family="Noto Sans KR, Apple SD Gothic Neo, Malgun Gothic, sans-serif" font-size="200" font-weight="900" fill="#FFFFFF" letter-spacing="-8">{text}</text></svg>
                """;
            return Results.Text(svg, "image/svg+xml");
        }).AllowAnonymous();
    }

    private static async Task<Tenant?> FindAsync(IAppDb db, string slug, CancellationToken ct)
    {
        await using var ctx = db.Create();
        return await ctx.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug && x.Status == TenantStatus.Active, ct);
    }
}
