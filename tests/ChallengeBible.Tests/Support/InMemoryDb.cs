using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Tests.Support;

/// <summary>
/// 서비스 테스트용 DB. 같은 이름이면 여러 컨텍스트가 같은 데이터를 본다.
/// 앱 전역 필터는 그대로 걸리고, DB 의 RLS 는 없다(RLS 는 SQL Server 통합 테스트에서).
/// </summary>
public sealed class InMemoryDb(string name, RequestScope scope) : IAppDb
{
    public AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options;
        return new AppDbContext(options, scope.TenantId) { CurrentAccountId = scope.AccountId };
    }

    public static string NewName() => "cb-" + Guid.NewGuid().ToString("N");

    /// <summary>규칙 읽기(ADR 0060). 규칙 표는 플랫폼 표라 영역 없이 읽는다. 테스트마다 새 기억 칸.</summary>
    public static ChallengeBible.Web.Services.RuleService Rules(string name) =>
        new(new InMemoryDb(name, new RequestScope()), new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));

    public static Account SeedAccount(string name, string email, string displayName, int? familyTenantId = null)
    {
        using var db = Raw(name);
        var a = new Account { Email = email, DisplayName = displayName, PasswordHash = "-", FamilyTenantId = familyTenantId };
        db.Accounts.Add(a);
        db.SaveChanges();
        return a;
    }

    public static Membership SeedMembership(string name, int tenantId, int accountId, string status, params string[] roles)
    {
        using var db = Raw(name);
        var m = new Membership
        {
            TenantId = tenantId, AccountId = accountId, Status = status,
            Roles = roles.Select(r => new MembershipRole { TenantId = tenantId, Role = r }).ToList(),
        };
        db.Memberships.Add(m);
        db.SaveChanges();
        return m;
    }

    /// <summary>필터 없이 바로 넣고 보는 컨텍스트(준비 · 확인용).</summary>
    public static AppDbContext Raw(string name) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options, tenantId: null) { FilterTenants = false };

    public static Tenant SeedTenant(string name, string slug, string code, string status = TenantStatus.Active, int id = 0)
    {
        using var db = Raw(name);
        var t = new Tenant
        {
            Id = id,
            Name = slug == "jamsil-kids" ? "잠실새내교회 유년부" : $"{slug} 부서",
            Slug = slug,
            JoinCode = code,
            TimeZoneId = "Asia/Seoul",
            ThemeColor = "blue",
            MissionEnabled = true,
            ReadingEnabled = true,
            Status = status,
            CreatedUtc = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc),
        };
        db.Tenants.Add(t);
        db.SaveChanges();
        return t;
    }
}
