using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record BrandingLog(DateTime OccurredUtc, string Actor, string Detail);
public sealed record BrandingView(string? AppName, string? AppShortName, string ThemeColor, IReadOnlyList<BrandingLog> Log);

/// <summary>
/// 부서 "이름 · 색"(0033 · 0038, 확정 시안: 관리-이름색). 앱 이름 1~30자 · 짧은 이름 1~12자(비우면 기본값), 대표 색은 정해 둔 4가지.
/// 바꾼 기록은 플랫폼 표 app.AuditLog(RLS 없음)에 남기므로 읽을 때 반드시 지금 부서의 TenantId 로 거른다.
/// </summary>
public sealed class BrandingService(IAppDb db, TenantContext tenant, TimeProvider time)
{
    public const string DefaultName = "도전!!!성경말씀";
    public const string DefaultShortName = "도전성경";
    public const string LogAction = "TenantBranding";
    public static readonly string[] Colors = PlatformTenantService.ThemeColors;

    public static string ColorLabel(string c) => c switch { "blue" => "파랑", "green" => "초록", "red" => "주황", "violet" => "보라", _ => c };

    /// <summary>매니페스트 theme_color · 아이콘 바탕(밝은 화면 색, site.css 의 --c-* 와 같다).</summary>
    public static string ColorHex(string c) => c switch { "green" => "#1F7A5C", "red" => "#B2462E", "violet" => "#6A4BB5", _ => "#2B59C3" };

    /// <summary>홈 화면 아이콘 글자: 짧은 이름에서 한글 · 영문 · 숫자 앞 2자.</summary>
    public static string IconText(string shortName)
    {
        var s = new string(shortName.Where(char.IsLetterOrDigit).Take(2).ToArray());
        return s.Length == 0 ? "도전" : s;
    }

    public async Task<BrandingView> GetAsync(CancellationToken ct = default)
    {
        var t = tenant.Current;
        await using var ctx = db.Create();
        var log = await (from l in ctx.AuditLogs
                         join a in ctx.Accounts on l.ActorAccountId equals a.Id
                         where l.TenantId == t.Id && l.Action == LogAction
                         orderby l.OccurredUtc descending, l.Id descending
                         select new BrandingLog(l.OccurredUtc, a.DisplayName, l.Detail ?? "")).Take(30).ToListAsync(ct);
        return new(t.AppName, t.AppShortName, t.ThemeColor, log);
    }

    /// <summary>저장. 빈칸(공백뿐 포함)은 기본값(null). 바뀐 것이 없으면 아무것도 하지 않는다. 틀리면 칸별 오류.</summary>
    public async Task<(bool Changed, Dictionary<string, string> Errors)> SaveAsync(int actor, string? appName, string? shortName, string themeColor, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        var name = Clean(appName);
        var shortN = Clean(shortName);
        if (name is { Length: > 30 }) errors["AppName"] = "앱 이름은 30자까지예요.";
        if (shortN is { Length: > 12 }) errors["AppShortName"] = "짧은 이름은 12자까지예요.";
        if (!Colors.Contains(themeColor)) errors["ThemeColor"] = "정해 둔 색 중에서 골라 주세요.";
        if (errors.Count > 0) return (false, errors);

        await using var ctx = db.Create();
        var t = await ctx.Tenants.SingleAsync(x => x.Id == tenant.Current.Id, ct);
        var parts = new List<string>();
        if (t.AppName != name) parts.Add($"앱 이름 {t.AppName ?? "기본"} → {name ?? "기본"}");
        if (t.AppShortName != shortN) parts.Add($"짧은 이름 {t.AppShortName ?? "기본"} → {shortN ?? "기본"}");
        if (t.ThemeColor != themeColor) parts.Add($"대표 색 {ColorLabel(t.ThemeColor)} → {ColorLabel(themeColor)}");
        if (parts.Count == 0) return (false, errors);

        (t.AppName, t.AppShortName, t.ThemeColor) = (name, shortN, themeColor);
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorAccountId = actor, Action = LogAction, TenantId = t.Id, Detail = string.Join(", ", parts) });
        await ctx.SaveChangesAsync(ct);
        return (true, errors);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
