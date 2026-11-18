using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record BrandingView(string? AppName, string? AppShortName, string ThemeColor, string? ReportOrg, IReadOnlyList<AuditLine> Log);

/// <summary>
/// 부서 "이름 · 색"(0033 · 0038, 확정 시안: 관리-이름색). 앱 이름 1~30자 · 짧은 이름 1~12자(비우면 기본값), 대표 색은 정해 둔 4가지.
/// 바꾼 기록은 플랫폼 표 app.AuditLog(RLS 없음)에 남기므로 읽을 때 반드시 지금 부서의 TenantId 로 거른다.
/// </summary>
public sealed class BrandingService(IAppDb db, TenantContext tenant, TimeProvider time)
{
    public const string DefaultName = "도전!!!성경말씀";
    public const string DefaultShortName = "도전성경";
    public const string DefaultReportOrg = "ATTENDANCE REPORT";
    public const string LogAction = "TenantBranding";
    public static readonly string[] Colors = PlatformTenantService.ThemeColors;

    public static string ColorLabel(string c) => c switch { "blue" => "파랑", "green" => "초록", "red" => "주황", "violet" => "보라", _ => c };

    /// <summary>매니페스트 theme_color · 아이콘 바탕(밝은 화면 색, site.css 의 --c-* 와 같다).</summary>
    public static string ColorHex(string c) => c switch { "green" => "#1F7A5C", "red" => "#B2462E", "violet" => "#6A4BB5", _ => "#2B59C3" };

    // ---- 브랜드 그림(0114 「네잎 정돈」): 모든 부서가 같은 네잎 아이콘 · 같은 카톡 대표 이미지 ----
    // 만드는 법: tools/brand/make.js → wwwroot/icons · wwwroot/images/og.png
    public const string IconSvg = "/icons/clover.svg";               // 매니페스트 SVG(흰 바탕 네모)
    public const string FaviconSvg = "/icons/favicon.svg";           // 브라우저 탭(바탕 없이 크게)
    public const string Icon192 = "/icons/icon-192.png";
    public const string Icon512 = "/icons/icon-512.png";
    public const string IconMaskable = "/icons/icon-maskable-512.png"; // 안드로이드 둥근 · 물방울 모양(가운데 80% 안전 영역)
    public const string AppleTouchIcon = "/icons/apple-touch-icon.png"; // 아이폰 180
    public const string OgImage = "/images/og.png";                  // 카톡 · SNS 공유 대표 이미지 1200×630
    public const string OgDescription = "네 가지 도전, 하나의 말씀! 성경미션 · 성경통독 · 성경필사 · 365 기도";

    /// <summary>카톡 미리보기 제목: 앱 이름 뒤에 부서 이름(앱 이름에 이미 들어 있으면 그대로). 부서 밖 화면은 기본 이름.</summary>
    public static string OgTitle(string appName, string? deptName) =>
        string.IsNullOrWhiteSpace(deptName) || appName.Contains(deptName) ? appName : $"{appName} · {deptName}";

    public async Task<BrandingView> GetAsync(CancellationToken ct = default)
    {
        var t = tenant.Current;
        await using var ctx = db.Create();
        return new(t.AppName, t.AppShortName, t.ThemeColor, t.ReportOrg, await AuditLines.ForTenantAsync(ctx, t.Id, LogAction, ct));
    }

    /// <summary>저장. 빈칸(공백뿐 포함)은 기본값(null). 바뀐 것이 없으면 아무것도 하지 않는다. 틀리면 칸별 오류.</summary>
    public async Task<(bool Changed, Dictionary<string, string> Errors)> SaveAsync(string actor, string? appName, string? shortName, string themeColor, string? reportOrg, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        var name = Clean(appName);
        var shortN = Clean(shortName);
        var org = Clean(reportOrg)?.ToUpperInvariant();
        if (name is { Length: > 30 }) errors["AppName"] = "앱 이름은 30자까지예요.";
        if (shortN is { Length: > 12 }) errors["AppShortName"] = "짧은 이름은 12자까지예요.";
        if (org is { Length: > 40 }) errors["ReportOrg"] = "보고서 영문 머리글은 40자까지예요.";
        else if (org is not null && !org.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or ' ' or '-' or '.' or '&' or '\'')) errors["ReportOrg"] = "영문 · 숫자만 써 주세요(예: JAMSIL SAENAE CHURCH).";
        if (!Colors.Contains(themeColor)) errors["ThemeColor"] = "정해 둔 색 중에서 골라 주세요.";
        if (errors.Count > 0) return (false, errors);

        await using var ctx = db.Create();
        var t = await ctx.Tenants.SingleAsync(x => x.Id == tenant.Current.Id, ct);
        var parts = new List<string>();
        if (t.AppName != name) parts.Add($"앱 이름 {t.AppName ?? "기본"} → {name ?? "기본"}");
        if (t.AppShortName != shortN) parts.Add($"짧은 이름 {t.AppShortName ?? "기본"} → {shortN ?? "기본"}");
        if (t.ThemeColor != themeColor) parts.Add($"대표 색 {ColorLabel(t.ThemeColor)} → {ColorLabel(themeColor)}");
        if (t.ReportOrg != org) parts.Add($"보고서 머리글 {t.ReportOrg ?? "기본"} → {org ?? "기본"}");
        if (parts.Count == 0) return (false, errors);

        (t.AppName, t.AppShortName, t.ThemeColor, t.ReportOrg) = (name, shortN, themeColor, org);
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = LogAction, TenantId = t.Id, Detail = string.Join(", ", parts) });
        await ctx.SaveChangesAsync(ct);
        return (true, errors);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
