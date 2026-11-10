using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Tests.Support;

/// <summary>
/// 서비스 테스트용 DB. 같은 이름이면 여러 컨텍스트가 같은 데이터를 본다.
/// 앱 전역 필터는 그대로 걸리고, DB 의 RLS 는 없다(RLS 는 SQL Server 통합 테스트에서).
/// </summary>
/// <summary>시험에서 만든 소속. Id = DeptRoleUserAssoc.Id(신청이면 SignupApplication.Id).</summary>
public sealed record SeededMember(int Id, int TenantId, string UserName, string UserId);

public sealed class InMemoryDb(string name, RequestScope scope) : IAppDb
{
    public AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options;
        return new AppDbContext(options, scope.TenantId) { CurrentUserName = scope.UserName };
    }

    public static string NewName() => "cb-" + Guid.NewGuid().ToString("N");

    /// <summary>규칙 읽기(ADR 0060). 규칙 표는 플랫폼 표라 영역 없이 읽는다. 테스트마다 새 기억 칸.</summary>
    public static ChallengeBible.Web.Services.RuleService Rules(string name) =>
        new(new InMemoryDb(name, new RequestScope()), new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));

    /// <summary>회원 한 명. 아이디 = 이메일 @ 앞부분. Id 는 아이디와 달라서, 둘을 섞어 쓰면 테스트가 잡는다.</summary>
    public static AppUser SeedAccount(string name, string email, string displayName, int? familyTenantId = null)
    {
        using var db = Raw(name);
        var userName = email.Split('@')[0];
        var a = new AppUser
        {
            Id = "id-" + userName, UserName = userName, NormalizedUserName = AppUser.Normalize(userName),
            Email = email, NormalizedEmail = AppUser.Normalize(email), FirstName = displayName, LastName = "", PasswordHash = "-",
        };
        db.Users.Add(a);
        db.SaveChanges();
        return a;
    }

    /// <summary>플랫폼 관리자로 만든다.</summary>
    public static void MakePlatformAdmin(string name, AppUser user) => AddRole(name, user.Id, IdentityRoles.SuperAdmin);

    public static void AddRole(string name, string userId, string role)
    {
        using var db = Raw(name);
        var r = db.IdentityRoleRows.FirstOrDefault(x => x.Name == role);
        if (r is null) db.IdentityRoleRows.Add(r = new AppRole { Name = role, NormalizedName = role.ToUpperInvariant() });
        db.UserRoles.Add(new AppUserRole { UserId = userId, RoleId = r.Id });
        db.SaveChanges();
    }

    /// <summary>
    /// 소속 한 줄. 옛 시험의 역할 이름을 역할 코드로 바꾼다: Participant = 학생, Teacher = 교사, TenantAdmin = 교사 + 관리자, Guardian = 학부모(코드 없음).
    /// Requested 는 신청(SignupApplication)을 만든다.
    /// </summary>
    public static SeededMember SeedMembership(string name, int tenantId, string userName, string status, params string[] roles)
    {
        using var db = Raw(name);
        var user = db.Users.Single(u => u.UserName == userName);
        var code = roles.Contains(Roles.Teacher) || roles.Contains(Roles.TenantAdmin) ? "T1002"
            : roles.Contains(Roles.Guardian) ? null : "T1001";
        SeedCodes(db, tenantId);
        if (status == MembershipStatus.Approved)
        {
            var d = new DeptRoleUserAssoc { TenantId = tenantId, UserId = user.Id, RoleCode = code };
            db.DeptRoles.Add(d);
            db.SaveChanges();
            if (roles.Contains(Roles.TenantAdmin)) AddRole(name, user.Id, IdentityRoles.Admin);
            return new SeededMember(d.Id, tenantId, userName, user.Id);
        }
        var a = new SignupApplication
        {
            TenantId = tenantId, UserId = user.Id, UserName = userName, FullName = user.DisplayName,
            RoleKind = code == "T1002" ? SignupRoleKind.Teacher : code is null ? SignupRoleKind.Parent : SignupRoleKind.Student,
            Status = status == MembershipStatus.Rejected ? SignupStatus.Rejected : SignupStatus.Pending,
        };
        db.SignupApplications.Add(a);
        db.SaveChanges();
        return new SeededMember(a.Id, tenantId, userName, user.Id);
    }

    private static void SeedCodes(AppDbContext db, int tenantId)
    {
        if (db.CodeData.Any(c => c.TenantId == tenantId)) return;
        ChallengeBible.Web.Services.UserRole.SeedCodes(db, tenantId, new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc));
        db.SaveChanges();
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
