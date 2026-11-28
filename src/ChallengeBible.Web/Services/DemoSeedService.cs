using System.Security.Cryptography;
using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>
/// 영상 촬영용 데모 영역(2026-11-28 사용자 요청, ADR 0143 — 임시 기능). 처음 데모 링크를 열 때 한 번 만들고, 있으면 그대로 둔다(여러 번 불러도 안전).
/// 데모 전용 부서 하나(`demo-school`)에 데모 관리자 · 데모 학생 · 가상 학생 9명(가상 이름)과 4가지 미션의 다양한 샘플 기록 · 가족팀 · 친구팀을 넣는다.
/// 실제 영역과 완전히 격리된다(영역 = 테넌트). 계정 비밀번호는 긴 랜덤 값이라 데모 링크 말고는 로그인할 수 없다.
/// 지우는 법: `TestTools/demo-remove.sql`.
/// </summary>
public sealed class DemoSeedService(ISystemDb systemDb, TimeProvider time)
{
    public const string Slug = "demo-school";   // `/demo/...` 로그인 주소와 겹치지 않게 「demo」가 아닌 이름
    public const string DeptName = "데모 주일학교";
    public const string AdminUser = "demo_admin";
    public const string StudentUser = "demo_user";
    public const string Actor = "demo-login";
    private const string Domain = "@demo.invalid";

    /// <summary>가상 학생(아이디, 이름). 첫 번째는 데모 일반 사용자(demo_user)다.</summary>
    private static readonly (string UserName, string Name)[] Students =
    [
        (StudentUser, "김하늘"), ("demo_s02", "이바다"), ("demo_s03", "박사랑"), ("demo_s04", "최은총"), ("demo_s05", "정믿음"),
        ("demo_s06", "한소망"), ("demo_s07", "오기쁨"), ("demo_s08", "서평화"), ("demo_s09", "윤다솜"), ("demo_s10", "강보람"),
    ];

    public sealed record DemoAccounts(Tenant Tenant, AppUser Admin, AppUser Student);

    /// <summary>데모 영역 · 계정 · 샘플 데이터가 없으면 만든다.</summary>
    public async Task<DemoAccounts> EnsureAsync(CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(Actor);
        var now = time.GetUtcNow().UtcDateTime;
        var tenant = await ctx.Tenants.SingleOrDefaultAsync(t => t.Slug == Slug, ct);
        var created = tenant is null;
        if (tenant is null)
        {
            tenant = new Tenant
            {
                Name = DeptName, Slug = Slug, JoinCode = await UniqueCodeAsync(ctx, ct), TimeZoneId = "Asia/Seoul", ThemeColor = "blue",
                MissionEnabled = true, ReadingEnabled = true, CopyEnabled = true, JoinListed = false, CreatedUtc = now,   // 가입 목록에 보이지 않는다
            };
            ctx.Tenants.Add(tenant);
            await ctx.SaveChangesAsync(ct);
            UserRole.SeedCodes(ctx, tenant.Id, now);
            await ctx.SaveChangesAsync(ct);
        }

        var admin = await EnsureUserAsync(ctx, AdminUser, "데모 관리자", ct);
        var student = await EnsureUserAsync(ctx, StudentUser, Students[0].Name, ct);
        if (!created && await ctx.DeptRoles.AnyAsync(d => d.TenantId == tenant.Id && d.UserId == admin.Id, ct)) return new(tenant, admin, student);   // 이미 만들었다

        // 관리자: 교사 역할 + 부서 관리자 역할(실제 권한 그대로 — 우회 없음)
        await DeptMembership.PlaceAsync(ctx, admin, tenant.Id, UserRole.TeacherCode, now, ct);
        var adminRole = await ctx.IdentityRoleRows.SingleAsync(r => r.Name == IdentityRoles.Admin, ct);
        if (!await ctx.UserRoles.AnyAsync(r => r.UserId == admin.Id && r.RoleId == adminRole.Id, ct)) ctx.UserRoles.Add(new AppUserRole { UserId = admin.Id, RoleId = adminRole.Id });
        await ctx.SaveChangesAsync(ct);

        // 가상 학생
        var accounts = new List<AppUser> { student };
        await DeptMembership.PlaceAsync(ctx, student, tenant.Id, UserRole.StudentCode, now, ct);
        foreach (var (un, name) in Students.Skip(1))
        {
            var u = await EnsureUserAsync(ctx, un, name, ct);
            await DeptMembership.PlaceAsync(ctx, u, tenant.Id, UserRole.StudentCode, now, ct);
            accounts.Add(u);
        }
        var mom = await EnsureUserAsync(ctx, "demo_parent", "김하늘 어머니", ct);
        await DeptMembership.PlaceAsync(ctx, mom, tenant.Id, null, now, ct);   // 학부모(역할 코드 없음)

        await AddMissionsAsync(ctx, tenant, accounts, now, ct);
        await AddTeamsAsync(ctx, tenant, accounts, mom, now, ct);
        await ctx.SaveChangesAsync(ct);
        return new(tenant, admin, student);
    }

    private static async Task<string> UniqueCodeAsync(AppDbContext ctx, CancellationToken ct)
    {
        for (var i = 0; i < 20; i++)
        {
            var c = "DM" + RandomNumberGenerator.GetInt32(100000, 999999);
            if (!await ctx.Tenants.AnyAsync(t => t.JoinCode == c, ct)) return c;
        }
        throw new InvalidOperationException("데모 참여 코드를 만들지 못했어요.");
    }

    /// <summary>실제 계정이 아닌 데모 계정. 비밀번호는 아무도 모르는 긴 랜덤 값(데모 링크로만 로그인).</summary>
    private static async Task<AppUser> EnsureUserAsync(AppDbContext ctx, string userName, string display, CancellationToken ct)
    {
        var found = await ctx.Users.SingleOrDefaultAsync(u => u.UserName == userName, ct);
        if (found is not null) return found;
        var email = userName + Domain;
        var u = new AppUser
        {
            UserName = userName, NormalizedUserName = AppUser.Normalize(userName), Email = email, NormalizedEmail = AppUser.Normalize(email), EmailConfirmed = true,
            FirstName = display, LastName = "", SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString("N"), InsertDT = DateTime.UtcNow,
        };
        u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));
        ctx.Users.Add(u);
        await ctx.SaveChangesAsync(ct);
        return u;
    }

    /// <summary>최근 14일: 말씀 · 기도 · 퀴즈 · 암송이 사람마다 날마다 다르게(완료 · 일부 · 아직)가 되게 규칙적으로 흩뿌린다.</summary>
    private async Task AddMissionsAsync(AppDbContext ctx, Tenant t, IReadOnlyList<AppUser> students, DateTime now, CancellationToken ct)
    {
        var today = TenantClock.ToTenantDate(new DateTimeOffset(now, TimeSpan.Zero), t.TimeZoneId);
        var weeks = new HashSet<(string, DateOnly)>();
        for (var i = 0; i < students.Count; i++)
        {
            var un = students[i].UserName!;
            for (var d = 0; d < 14; d++)
            {
                var date = today.AddDays(-d);
                var at = new DateTime(date.Year, date.Month, date.Day, 3, 0, 0, DateTimeKind.Utc);
                void Add(string item, DateOnly? week = null) => ctx.MissionActivities.Add(new MissionActivity { TenantId = t.Id, UserName = un, MissionDate = date, MissionItem = item, Points = 1, PerformedAt = at, ReciteWeekStart = week });
                if (d == 0 && i == 0) { Add("Word.Me"); continue; }   // 데모 학생은 오늘 말씀만 한 상태(진행 중)
                if (d == 0 && i % 3 == 2) continue;                    // 오늘 아직 아무것도 안 한 친구
                if ((i + d) % 4 != 0) Add("Word.Me");
                if ((i + d) % 3 == 0) Add("Word.Parent");
                if ((i * 2 + d) % 3 != 0) Add("Prayer.Me");
                if ((i + d) % 5 == 0) Add("Prayer.Others");
                if ((i + d) % 3 == 1) Add("Quiz.Correct");
                var week = Week.Of(date, DayOfWeek.Sunday).Start;
                if (i % 2 == 0 && (i + week.DayNumber / 7) % 3 != 0 && weeks.Add((un, week))) Add("Recite", week);
            }
        }
        await Task.CompletedTask;
    }

    private static Task AddTeamsAsync(AppDbContext ctx, Tenant t, IReadOnlyList<AppUser> students, AppUser mom, DateTime now, CancellationToken ct)
    {
        var family = new FamilyTeam { TenantId = t.Id, Name = "하늘이네 가족", AwardKey = "하늘이네 가족", CreatorUserName = students[0].UserName, InsertDT = now, UpdateDT = now, TeamType = TeamTypes.Family };
        FamilyJoinService.Seat(family, students[0].UserName!, FamilyRelation.Student, now);
        FamilyJoinService.Seat(family, mom.UserName!, FamilyRelation.Mother, now);
        var friends = new FamilyTeam { TenantId = t.Id, Name = "바다와 사랑", AwardKey = "바다와 사랑", CreatorUserName = students[1].UserName, InsertDT = now, UpdateDT = now, TeamType = TeamTypes.Friend };
        FamilyJoinService.Seat(friends, students[1].UserName!, FamilyRelation.Student, now);
        FamilyJoinService.Seat(friends, students[2].UserName!, FamilyRelation.Student, now);
        ctx.FamilyTeams.AddRange(family, friends);
        return Task.CompletedTask;
    }
}
