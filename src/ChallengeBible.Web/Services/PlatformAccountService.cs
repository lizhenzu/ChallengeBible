using System.Security.Cryptography;
using ChallengeBible.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record AccountHit(int Id, string Name, string Email, bool PlatformAdmin, bool TenantAdmin, IReadOnlyList<string> Tenants);
public sealed record AccountMembership(string Tenant, string ThemeColor, string Status, IReadOnlyList<string> Roles, bool Family);
public sealed record AccountDetail(int Id, string Name, string Email, bool PlatformAdmin, DateTime CreatedUtc, string? FamilyTenant,
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
    private static readonly PasswordHasher<Account> Hasher = new();
    private static readonly string[] Words = ["bless", "grace", "faith", "hope", "light", "peace", "love", "joy"];

    /// <summary>읽기 쉬운 임시 비밀번호: 낱말-숫자 4자리(예: grace-4821).</summary>
    public static string NewTemporaryPassword() => $"{Words[RandomNumberGenerator.GetInt32(Words.Length)]}-{RandomNumberGenerator.GetInt32(1000, 10000)}";

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

    private static async Task<List<(int AccountId, string Tenant, string Color, string Status, List<string> Roles, int TenantId)>> MembershipsAsync(
        AppDbContext ctx, IReadOnlyCollection<int> accountIds, CancellationToken ct)
    {
        var rows = await (from m in ctx.Memberships.Include(x => x.Roles)
                          join t in ctx.Tenants on m.TenantId equals t.Id
                          where accountIds.Contains(m.AccountId) && m.Status != MembershipStatus.Rejected
                          orderby t.Name
                          select new { m.AccountId, t.Name, t.ThemeColor, m.Status, Roles = m.Roles.Select(r => r.Role).ToList(), m.TenantId }).ToListAsync(ct);
        return rows.Select(r => (r.AccountId, r.Name, r.ThemeColor, r.Status, r.Roles, r.TenantId)).ToList();
    }

    public async Task<List<AccountHit>> SearchAsync(int actor, string? q, CancellationToken ct = default)
    {
        var term = q?.Trim() ?? "";
        if (term.Length == 0) return [];
        await using var ctx = await OpenAsync(actor, ct);
        var lower = term.ToLowerInvariant();
        var accounts = await ctx.Accounts.AsNoTracking().Where(a => a.Email.Contains(lower) || a.DisplayName.Contains(term))
            .OrderBy(a => a.DisplayName).ThenBy(a => a.Email).Take(30).ToListAsync(ct);
        var mems = await MembershipsAsync(ctx, accounts.Select(a => a.Id).ToList(), ct);
        return accounts.Select(a =>
        {
            var mine = mems.Where(m => m.AccountId == a.Id).ToList();
            return new AccountHit(a.Id, a.DisplayName, a.Email, a.IsPlatformAdmin,
                mine.Any(m => m.Status == MembershipStatus.Approved && m.Roles.Contains(Roles.TenantAdmin)), mine.Select(m => m.Tenant).ToList());
        }).ToList();
    }

    public async Task<AccountDetail?> DetailAsync(int actor, int id, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var a = await ctx.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return null;
        var mems = await MembershipsAsync(ctx, [id], ct);
        var family = a.FamilyTenantId is int f ? await ctx.Tenants.Where(t => t.Id == f).Select(t => t.Name).SingleOrDefaultAsync(ct) : null;

        // 이 계정이 한 일 + 이 계정에 한 일(Detail 에 "account={id} " 로 적힌 기록: 비밀번호 · 읽음 초기화, 진급)
        var marker = $"account={id} ";
        var logs = await (from l in ctx.AuditLogs
                          join actorAcc in ctx.Accounts on l.ActorAccountId equals actorAcc.Id
                          where l.ActorAccountId == id || (l.Detail != null && (l.Detail.StartsWith(marker) || l.Detail.Contains(" " + marker)))
                          orderby l.OccurredUtc descending, l.Id descending
                          select new { l.OccurredUtc, actorAcc.DisplayName, actorAcc.IsPlatformAdmin, l.TenantId, l.Action, l.Detail }).Take(30).ToListAsync(ct);
        var tenants = await ctx.Tenants.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        return new AccountDetail(a.Id, a.DisplayName, a.Email, a.IsPlatformAdmin, a.CreatedUtc, family,
            mems.Select(m => new AccountMembership(m.Tenant, m.Color, m.Status, m.Roles, m.TenantId == a.FamilyTenantId)).ToList(),
            mems.Where(m => m.Status == MembershipStatus.Approved && m.Roles.Contains(Roles.TenantAdmin)).Select(m => m.Tenant).ToList(),
            logs.Select(l => new PlatformLog(l.OccurredUtc, l.IsPlatformAdmin ? $"{l.DisplayName}(운영자)" : l.DisplayName,
                l.TenantId is int t ? tenants.GetValueOrDefault(t) : null, PlatformHomeService.LogText(l.Action, l.Detail))).ToList());
    }

    /// <summary>임시 비밀번호를 만들고 다음 로그인 때 바꾸게 한다(0035). 플랫폼 관리자 계정은 null.</summary>
    public async Task<string?> ResetPasswordAsync(int actor, int id, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var a = await ctx.Accounts.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (a is null || a.IsPlatformAdmin) return null;
        var temp = NewTemporaryPassword();
        a.PasswordHash = Hasher.HashPassword(a, temp);
        a.MustChangePassword = true;
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorAccountId = actor, Action = ResetPasswordAction, Detail = $"account={id} {a.DisplayName}({a.Email})" });
        await ctx.SaveChangesAsync(ct);
        return temp;
    }

    /// <summary>성경 읽음 표시 · 책갈피를 모두 지운다(0029, 되돌릴 수 없음). 확인 이메일이 맞아야 한다. 지운 줄 수, 틀리면 null.</summary>
    public async Task<int?> ResetBibleMarksAsync(int actor, int id, string? confirmEmail, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var a = await ctx.Accounts.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (a is null || a.IsPlatformAdmin || AccountService.NormalizeEmail(confirmEmail ?? "") != a.Email) return null;
        var marks = await ctx.VerseReadMarks.IgnoreQueryFilters().Where(m => m.AccountId == id).ToListAsync(ct);
        var books = await ctx.Bookmarks.IgnoreQueryFilters().Where(b => b.AccountId == id).ToListAsync(ct);
        ctx.VerseReadMarks.RemoveRange(marks);
        ctx.Bookmarks.RemoveRange(books);
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorAccountId = actor, Action = ResetMarksAction,
            Detail = $"account={id} {a.DisplayName}({a.Email}) 읽음 {marks.Count} · 책갈피 {books.Count}" });
        await ctx.SaveChangesAsync(ct);
        return marks.Count + books.Count;
    }
}
