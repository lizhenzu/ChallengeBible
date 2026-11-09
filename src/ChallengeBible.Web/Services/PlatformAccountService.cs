using System.Security.Cryptography;
using ChallengeBible.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record AccountHit(string Id, string Name, string Email, bool PlatformAdmin, bool TenantAdmin, IReadOnlyList<string> Tenants);
public sealed record AccountMembership(string Tenant, string ThemeColor, string Status, IReadOnlyList<string> Roles);
public sealed record AccountDetail(string Id, string Name, string Email, bool PlatformAdmin, DateTime CreatedUtc,
    IReadOnlyList<AccountMembership> Memberships, IReadOnlyList<string> AdminOf, IReadOnlyList<PlatformLog> Log);

/// <summary>
/// 플랫폼 · 계정 찾기(확정 시안: 플랫폼-계정찾기, 0035 · 0029 · 0050). 여러 부서 계정을 보므로 시스템 연결로만, 시작마다 플랫폼 관리자 확인.
/// 임시 비밀번호는 서버가 만들어 한 번만 돌려준다(원문은 저장하지 않음). 플랫폼 관리자 계정은 초기화하지 않는다.
/// 기록은 AuditLog 에 "account={id} …" 로 대상 계정을 적어 남긴다(이 계정 관련 기록에서 찾는다).
/// </summary>
public sealed class PlatformAccountService(ISystemDb systemDb, TimeProvider time)
{
    public const string ResetPasswordAction = "PasswordReset";
    public const string ResetMarksAction = "BibleMarksReset";
    private static readonly PasswordHasher<AppUser> Hasher = new();
    private static readonly string[] Words = ["bless", "grace", "faith", "hope", "light", "peace", "love", "joy"];

    /// <summary>읽기 쉬운 임시 비밀번호: 낱말-숫자 4자리(예: grace-4821).</summary>
    public static string NewTemporaryPassword() => $"{Words[RandomNumberGenerator.GetInt32(Words.Length)]}-{RandomNumberGenerator.GetInt32(1000, 10000)}";

    /// <summary>계정에 임시 비밀번호를 걸고(다음 로그인 때 바꾸게) 원문을 돌려준다. 저장은 부르는 쪽이.</summary>
    public static string ApplyTemporaryPassword(AppUser a)
    {
        var temp = NewTemporaryPassword();
        a.PasswordHash = Hasher.HashPassword(a, temp);
        return temp;
    }

    /// <summary>임시 비밀번호를 건 계정은 다음 로그인에서 비밀번호를 바꾸게 한다(0035). 저장은 부르는 쪽이.</summary>
    public static async Task MarkMustChangeAsync(AppDbContext ctx, string userName, CancellationToken ct)
    {
        var s = await ctx.UserSettings.SingleOrDefaultAsync(x => x.UserName == userName, ct);
        if (s is null) ctx.UserSettings.Add(s = new UserSetting { UserName = userName });
        s.MustChangePassword = true;
    }

    /// <summary>계정의 성경 읽음 표시 · 책갈피를 모두 지운다(0029). 시스템 연결에서만. 저장은 부르는 쪽이.</summary>
    public static async Task<(int Marks, int Bookmarks)> ClearBibleMarksAsync(AppDbContext ctx, string userName, CancellationToken ct)
    {
        var marks = await ctx.ReadingVerses.IgnoreQueryFilters().Where(m => m.UserName == userName).ToListAsync(ct);
        var books = await ctx.ReadingBookmarks.IgnoreQueryFilters().Where(b => b.UserName == userName).ToListAsync(ct);
        ctx.ReadingVerses.RemoveRange(marks);
        ctx.ReadingBookmarks.RemoveRange(books);
        return (marks.Count, books.Count);
    }

    private async Task<AppDbContext> OpenAsync(string actor, CancellationToken ct)
    {
        var ctx = systemDb.Create(actor);
        var me = await ctx.Users.AsNoTracking().SingleOrDefaultAsync(a => a.UserName == actor, ct);
        if (me is null || me.IsDisabled(time.GetUtcNow()) || !await AccountService.IsPlatformAdminAsync(ctx, me.Id, ct))
        {
            await ctx.DisposeAsync();
            throw new UnauthorizedAccessException("플랫폼 관리자만 할 수 있어요.");
        }
        return ctx;
    }

    private static async Task<List<(string UserId, string Tenant, string Color, string Status, List<string> Roles, int TenantId)>> MembershipsAsync(
        AppDbContext ctx, IReadOnlyCollection<string> userIds, CancellationToken ct)
    {
        var admins = (await (from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id
                             where r.Name == IdentityRoles.Admin select ur.UserId).ToListAsync(ct)).ToHashSet();
        var rows = await (from d in ctx.DeptRoles
                          join t in ctx.Tenants on d.TenantId equals t.Id
                          where userIds.Contains(d.UserId!)
                          orderby t.Name
                          select new { d.UserId, t.Name, t.ThemeColor, d.RoleCode, d.TenantId }).ToListAsync(ct);
        return rows.Select(r => (r.UserId!, r.Name, r.ThemeColor, MembershipStatus.Approved, UserRole.ScreenRoles(r.RoleCode, admins.Contains(r.UserId!)).ToList(), r.TenantId)).ToList();
    }

    public async Task<List<AccountHit>> SearchAsync(string actor, string? q, CancellationToken ct = default)
    {
        var term = q?.Trim() ?? "";
        if (term.Length == 0) return [];
        await using var ctx = await OpenAsync(actor, ct);
        var lower = term.ToLowerInvariant();
        var accounts = (await ctx.Users.AsNoTracking().Where(a => a.Email!.Contains(lower) || a.UserName!.Contains(term) || (a.LastName! + a.FirstName!).Contains(term))
            .Take(100).ToListAsync(ct)).OrderBy(a => a.DisplayName).ThenBy(a => a.Email).Take(30).ToList();
        var ids = accounts.Select(a => a.Id).ToList();
        var mems = await MembershipsAsync(ctx, ids, ct);
        var platform = (await (from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id
                               where r.Name == IdentityRoles.SuperAdmin && ids.Contains(ur.UserId) select ur.UserId).ToListAsync(ct)).ToHashSet();
        return accounts.Select(a =>
        {
            var mine = mems.Where(m => m.UserId == a.Id).ToList();
            return new AccountHit(a.UserName!, a.DisplayName, a.Email ?? "", platform.Contains(a.Id),
                mine.Any(m => m.Roles.Contains(Roles.TenantAdmin)), mine.Select(m => m.Tenant).ToList());
        }).ToList();
    }

    public async Task<AccountDetail?> DetailAsync(string actor, string id, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var a = await ctx.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserName == id, ct);
        if (a is null) return null;
        var mems = await MembershipsAsync(ctx, [a.Id], ct);

        // 이 계정이 한 일 + 이 계정에 한 일(Detail 에 "account={id} " 로 적힌 기록: 비밀번호 · 읽음 초기화, 진급)
        var marker = $"account={id} ";
        var logs = (await (from l in ctx.AuditLogs
                           join actorAcc in ctx.Users on l.ActorUserName equals actorAcc.UserName
                           where l.ActorUserName == id || (l.Detail != null && (l.Detail.StartsWith(marker) || l.Detail.Contains(" " + marker)))
                           orderby l.OccurredUtc descending, l.Id descending
                           select new { l.OccurredUtc, actorAcc.Id, actorAcc.LastName, actorAcc.FirstName, l.TenantId, l.Action, l.Detail }).Take(30).ToListAsync(ct))
            .Select(l => new { l.OccurredUtc, DisplayName = AppUser.FullName(l.LastName, l.FirstName), l.Id, l.TenantId, l.Action, l.Detail }).ToList();
        var platformIds = (await (from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id where r.Name == IdentityRoles.SuperAdmin select ur.UserId).ToListAsync(ct)).ToHashSet();
        var tenants = await ctx.Tenants.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        return new AccountDetail(a.UserName!, a.DisplayName, a.Email ?? "", platformIds.Contains(a.Id), a.InsertDT,
            mems.Select(m => new AccountMembership(m.Tenant, m.Color, m.Status, m.Roles)).ToList(),
            mems.Where(m => m.Roles.Contains(Roles.TenantAdmin)).Select(m => m.Tenant).ToList(),
            logs.Select(l => new PlatformLog(l.OccurredUtc, platformIds.Contains(l.Id) ? $"{l.DisplayName}(운영자)" : l.DisplayName,
                l.TenantId is int t ? tenants.GetValueOrDefault(t) : null, PlatformHomeService.LogText(l.Action, l.Detail))).ToList());
    }

    /// <summary>임시 비밀번호를 만들고 다음 로그인 때 바꾸게 한다(0035). 플랫폼 관리자 계정은 null.</summary>
    public async Task<string?> ResetPasswordAsync(string actor, string id, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var a = await ctx.Users.SingleOrDefaultAsync(x => x.UserName == id, ct);
        if (a is null || await AccountService.IsPlatformAdminAsync(ctx, a.Id, ct)) return null;
        var temp = ApplyTemporaryPassword(a);
        await MarkMustChangeAsync(ctx, id, ct);
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = ResetPasswordAction, Detail = $"account={id} {a.DisplayName}({a.Email})" });
        await ctx.SaveChangesAsync(ct);
        return temp;
    }

    /// <summary>성경 읽음 표시 · 책갈피를 모두 지운다(0029, 되돌릴 수 없음). 확인 이메일이 맞아야 한다. 지운 줄 수, 틀리면 null.</summary>
    public async Task<int?> ResetBibleMarksAsync(string actor, string id, string? confirmEmail, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var a = await ctx.Users.SingleOrDefaultAsync(x => x.UserName == id, ct);
        if (a is null || await AccountService.IsPlatformAdminAsync(ctx, a.Id, ct) || AccountService.NormalizeEmail(confirmEmail ?? "") != (a.Email ?? "")) return null;
        var (marks, books) = await ClearBibleMarksAsync(ctx, id, ct);
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = ResetMarksAction,
            Detail = $"account={id} {a.DisplayName}({a.Email}) 읽음 {marks} · 책갈피 {books}" });
        await ctx.SaveChangesAsync(ct);
        return marks + books;
    }
}
