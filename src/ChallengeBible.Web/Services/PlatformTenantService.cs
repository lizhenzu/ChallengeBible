using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record TenantRow(int Id, string Name, string Slug, string JoinCode, string ThemeColor, bool MissionEnabled, bool ReadingEnabled,
    string Status, int MemberCount, IReadOnlyList<(int AccountId, string Name, string Email)> Admins, DateTime CreatedUtc, string TimeZoneId)
{
    public bool JoinListed { get; init; }
}

public sealed record NewTenant(string Name, string Slug, string TimeZoneId, string ThemeColor, bool Mission, bool Reading, string? AdminEmail);

/// <summary>
/// 플랫폼 관리자의 부서 관리(API 계약 4-5 · 5-4b): 만들기, 프로그램, 참여 코드, 부서 관리자, 정지.
/// 여러 부서를 다루므로 시스템 연결로만 한다(물리 설계 3-3). 시작마다 플랫폼 관리자인지 DB 로 다시 확인한다.
/// </summary>
public sealed partial class PlatformTenantService(ISystemDb systemDb, TimeProvider time)
{
    public static readonly string[] ThemeColors = ["blue", "green", "red", "violet"];   // 0033 정해 둔 색
    public static readonly string[] TimeZones = ["Asia/Seoul", "America/Los_Angeles", "America/New_York"];
    /// <summary>주소로 쓰면 다른 화면과 겹치는 낱말.</summary>
    public static readonly string[] ReservedSlugs = ["platform", "account", "join", "admin", "api", "css", "js", "lib", "error", "static"];
    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // 0 · O · 1 · I 빼고(0048)

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugRule();

    public static string NewJoinCode() => string.Create(6, 0, (span, _) =>
    {
        for (var i = 0; i < span.Length; i++) span[i] = CodeChars[RandomNumberGenerator.GetInt32(CodeChars.Length)];
    });

    private async Task<AppDbContext> OpenAsync(int actor, CancellationToken ct)
    {
        var ctx = systemDb.Create(actor);
        if (!await ctx.Accounts.AnyAsync(a => a.Id == actor && a.IsPlatformAdmin && a.Status == "Active", ct))
        {
            await ctx.DisposeAsync();
            throw new UnauthorizedAccessException("플랫폼 관리자만 할 수 있어요.");
        }
        return ctx;
    }

    public async Task<List<TenantRow>> ListAsync(int actor, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var tenants = await ctx.Tenants.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
        var counts = await ctx.Memberships.Where(m => m.Status == MembershipStatus.Approved)
            .GroupBy(m => m.TenantId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var admins = await (from r in ctx.MembershipRoles
                            join m in ctx.Memberships on new { r.TenantId, Id = r.MembershipId } equals new { m.TenantId, m.Id }
                            join a in ctx.Accounts on m.AccountId equals a.Id
                            where r.Role == Roles.TenantAdmin && m.Status == MembershipStatus.Approved
                            select new { m.TenantId, a.Id, a.DisplayName, a.Email }).ToListAsync(ct);
        return tenants.Select(t => new TenantRow(t.Id, t.Name, t.Slug, t.JoinCode.Trim(), t.ThemeColor, t.MissionEnabled, t.ReadingEnabled, t.Status,
                counts.GetValueOrDefault(t.Id),
                admins.Where(a => a.TenantId == t.Id).Select(a => (a.Id, a.DisplayName, a.Email)).ToList(), t.CreatedUtc, t.TimeZoneId) { JoinListed = t.JoinListed })
            .ToList();
    }

    public async Task<(Tenant? Tenant, FieldErrors Errors)> CreateAsync(int actor, NewTenant input, CancellationToken ct = default)
    {
        var errors = new FieldErrors();
        var name = input.Name?.Trim() ?? "";
        var slug = (input.Slug ?? "").Trim().ToLowerInvariant();
        if (name.Length is 0 or > 100) errors["Name"] = "부서 이름을 1 ~ 100자로 써 주세요.";
        if (slug.Length is < 2 or > 40 || !SlugRule().IsMatch(slug)) errors["Slug"] = "영어 소문자 · 숫자 · 하이픈으로 2 ~ 40자를 써 주세요.";
        else if (ReservedSlugs.Contains(slug)) errors["Slug"] = "다른 화면 주소와 겹쳐요. 다른 주소를 써 주세요.";
        if (!input.Mission && !input.Reading) errors["Programs"] = "프로그램을 하나는 골라 주세요(R-150).";
        if (!ThemeColors.Contains(input.ThemeColor)) errors["ThemeColor"] = "정해 둔 색 중에서 골라 주세요.";
        if (!TimeZones.Contains(input.TimeZoneId)) errors["TimeZoneId"] = "시간대를 골라 주세요.";
        if (!errors.Ok) return (null, errors);

        await using var ctx = await OpenAsync(actor, ct);
        if (await ctx.Tenants.AnyAsync(t => t.Slug == slug, ct)) { errors["Slug"] = "이미 쓰고 있는 주소예요."; return (null, errors); }
        Account? admin = null;
        if (!string.IsNullOrWhiteSpace(input.AdminEmail))
        {
            var id = AccountService.NormalizeEmail(input.AdminEmail);
            admin = await ctx.Accounts.SingleOrDefaultAsync(a => a.Email == id, ct);
            if (admin is null) { errors["AdminEmail"] = "그 이메일의 계정이 없어요."; return (null, errors); }
        }

        var t = new Tenant
        {
            Name = name, Slug = slug, JoinCode = await UniqueCodeAsync(ctx, ct), TimeZoneId = input.TimeZoneId, ThemeColor = input.ThemeColor,
            MissionEnabled = input.Mission, ReadingEnabled = input.Reading, CreatedUtc = time.GetUtcNow().UtcDateTime,
        };
        ctx.Tenants.Add(t);
        await ctx.SaveChangesAsync(ct);
        if (admin is not null) await GrantAdminAsync(ctx, actor, t.Id, admin.Id, ct);
        return (t, errors);
    }

    private static async Task<string> UniqueCodeAsync(AppDbContext ctx, CancellationToken ct)
    {
        for (var i = 0; i < 20; i++)
        {
            var c = NewJoinCode();
            if (!await ctx.Tenants.AnyAsync(t => t.JoinCode == c, ct)) return c;
        }
        throw new InvalidOperationException("참여 코드를 만들지 못했어요. 다시 해 주세요.");
    }

    public async Task<string> RegenerateCodeAsync(int actor, int tenantId, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var t = await ctx.Tenants.SingleAsync(x => x.Id == tenantId, ct);
        t.JoinCode = await UniqueCodeAsync(ctx, ct);
        await ctx.SaveChangesAsync(ct);
        return t.JoinCode;
    }

    public async Task<string?> SetProgramsAsync(int actor, int tenantId, bool mission, bool reading, CancellationToken ct = default)
    {
        if (!mission && !reading) return "프로그램을 하나는 켜 주세요(R-150).";
        await using var ctx = await OpenAsync(actor, ct);
        var t = await ctx.Tenants.SingleAsync(x => x.Id == tenantId, ct);
        (t.MissionEnabled, t.ReadingEnabled) = (mission, reading);
        await ctx.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>참여 신청 화면의 부서 목록에 보이기 켜기 · 끄기(0059).</summary>
    public async Task SetJoinListedAsync(int actor, int tenantId, bool listed, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var t = await ctx.Tenants.SingleAsync(x => x.Id == tenantId, ct);
        t.JoinListed = listed;
        await ctx.SaveChangesAsync(ct);
    }

    public async Task SetStatusAsync(int actor, int tenantId, string status, CancellationToken ct = default)
    {
        if (status is not (TenantStatus.Active or TenantStatus.Suspended)) throw new ArgumentOutOfRangeException(nameof(status));
        await using var ctx = await OpenAsync(actor, ct);
        var t = await ctx.Tenants.SingleAsync(x => x.Id == tenantId, ct);
        t.Status = status;   // 데이터는 지우지 않는다
        await ctx.SaveChangesAsync(ct);
    }

    /// <summary>부서 관리자 지정. 소속이 없으면 승인된 소속을 함께 만든다(API 계약 5-4b).</summary>
    public async Task<string?> AddAdminAsync(int actor, int tenantId, string email, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var id = AccountService.NormalizeEmail(email ?? "");
        var a = await ctx.Accounts.SingleOrDefaultAsync(x => x.Email == id, ct);
        if (a is null) return "그 이메일의 계정이 없어요.";
        await GrantAdminAsync(ctx, actor, tenantId, a.Id, ct);
        return null;
    }

    private async Task GrantAdminAsync(AppDbContext ctx, int actor, int tenantId, int accountId, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var m = await ctx.Memberships.Include(x => x.Roles).SingleOrDefaultAsync(x => x.TenantId == tenantId && x.AccountId == accountId, ct);
        if (m is null)
        {
            m = new Membership { TenantId = tenantId, AccountId = accountId, RequestedUtc = now };
            ctx.Memberships.Add(m);
        }
        m.Status = MembershipStatus.Approved;
        m.DecidedUtc = now;
        m.DecidedByAccountId = actor;
        foreach (var r in new[] { Roles.Teacher, Roles.TenantAdmin }.Where(r => m.Roles.All(x => x.Role != r)))
            m.Roles.Add(new MembershipRole { TenantId = tenantId, Role = r });
        await ctx.SaveChangesAsync(ct);
    }

    /// <summary>관리자 역할만 뺀다. 소속은 그대로(시안 안내).</summary>
    public async Task RemoveAdminAsync(int actor, int tenantId, int accountId, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var role = await ctx.MembershipRoles.SingleOrDefaultAsync(r => r.TenantId == tenantId && r.Role == Roles.TenantAdmin
            && ctx.Memberships.Any(m => m.TenantId == tenantId && m.Id == r.MembershipId && m.AccountId == accountId), ct);
        if (role is null) return;
        ctx.MembershipRoles.Remove(role);
        await ctx.SaveChangesAsync(ct);
    }
}
