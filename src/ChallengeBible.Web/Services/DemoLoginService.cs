using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ChallengeBible.Web.Services;

/// <summary>
/// 영상 촬영용 데모 자동 로그인(2026-11-28 사용자 요청, ADR 0143 — 임시 기능).
/// `GET /demo/admin?key=…` · `GET /demo/user?key=…` — 설정 `DemoLogin { Enabled, Key, ExpiresAt }` 가 모두 맞을 때만 열린다.
/// 꺼져 있거나 · 기한이 없거나 지났거나 · 키가 없거나 틀리면 **404**(존재 자체를 숨긴다). 키는 설정 · 환경변수(`DemoLogin__Key`)에서만 읽는다.
/// 로그인은 기존과 같은 쿠키 인증 그대로이고 권한 우회는 없다 — 데모 계정 자체의 권한(관리자 · 학생)으로 동작한다.
/// 로그인할 때마다 감사 기록(AuditLog `DemoLogin`: 일시 · IP · 계정)을 남긴다.
/// </summary>
public sealed class DemoLoginService(IConfiguration config, TimeProvider time, DemoSeedService seed, AccountService accounts, ISystemDb systemDb, ILogger<DemoLoginService> log)
{
    public const string Action = "DemoLogin";

    public bool Enabled => config.GetValue<bool>("DemoLogin:Enabled");

    /// <summary>기한(ExpiresAt). 비었거나 읽을 수 없으면 null — 그러면 열지 않는다(기한 없는 데모 링크는 만들지 않는다).</summary>
    public DateTimeOffset? ExpiresAt =>
        DateTimeOffset.TryParse(config["DemoLogin:ExpiresAt"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var at) ? at : null;

    /// <summary>켜져 있고, 키가 정해져 있고, 기한 안인가.</summary>
    public bool IsOpen => Enabled && !string.IsNullOrWhiteSpace(config["DemoLogin:Key"]) && ExpiresAt is { } at && time.GetUtcNow() < at;

    /// <summary>키 비교(시간차 공격을 막는 고정 시간 비교).</summary>
    public bool KeyMatches(string? key)
    {
        var want = config["DemoLogin:Key"];
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(want)) return false;
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(key)), SHA256.HashData(Encoding.UTF8.GetBytes(want)));
    }

    /// <summary>who = admin · user. 열 수 없으면 null(→ 404). 열렸으면 쿠키로 로그인하고 이동할 주소를 돌려준다.</summary>
    public async Task<string?> SignInAsync(string who, string? key, HttpContext http, CancellationToken ct = default)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "?";
        if (!IsOpen || who is not ("admin" or "user") || !KeyMatches(key))
        {
            if (IsOpen) log.LogWarning("데모 로그인 거절 ip={Ip} who={Who}", ip, who);   // 키는 기록하지 않는다
            return null;
        }
        var demo = await seed.EnsureAsync(ct);
        var account = who == "admin" ? demo.Admin : demo.Student;
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);   // 다른 계정으로 로그인돼 있으면 먼저 로그아웃
        await CurrentUser.SignInAsync(http, await accounts.ToSignedInAsync(account, ct));

        await using (var ctx = systemDb.Create(account.UserName!))
        {
            ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = account.UserName!, Action = Action, TenantId = demo.Tenant.Id, Detail = $"ip={ip} account={account.UserName}" });
            await ctx.SaveChangesAsync(ct);
        }
        log.LogWarning("데모 로그인 {Account} ip={Ip}", account.UserName, ip);
        return who == "admin" ? $"/{DemoSeedService.Slug}/admin" : $"/{DemoSeedService.Slug}/word-mission";
    }
}
