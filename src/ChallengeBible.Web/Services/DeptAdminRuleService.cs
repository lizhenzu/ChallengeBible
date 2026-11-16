using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>부서 관리자 규칙 화면의 한 줄: 직분 코드 · 이름 · 켜짐 · 지금 그 직분인 계정 수.</summary>
public sealed record AdminRuleRow(string Code, string Name, bool On, int People);

public sealed record AdminRuleView(IReadOnlyList<AdminRuleRow> Rows, int ByRule, int Manual);

/// <summary>
/// 부서 관리자 규칙(2026-11-16 사용자, 확정 시안 관리자규칙): 체크한 직분인 사람은 모든 부서에서 바로 부서 관리자(Identity Admin)가 된다.
/// 공통 규칙 하나뿐(플랫폼 관리자만). 저장 값은 공통 할 일 규칙 표(TodoRule, TenantId NULL)의 Kind = "deptadmin" 한 줄(직분 코드 csv) — 표를 새로 만들지 않는다.
/// 규칙으로 준 관리자에게는 업무 꼬리표 AutoAdmin 을 붙여, 체크를 풀거나 직분이 바뀌면 그 사람만 관리자에서 뺀다. 따로 정한 관리자 · 계정은 건드리지 않는다(CLAUDE.md).
/// </summary>
public sealed class DeptAdminRuleService(ISystemDb systemDb, TimeProvider time)
{
    public const string Kind = "deptadmin";
    public const string AutoTag = "AutoAdmin";
    /// <summary>고를 수 있는 직분(임원): 교장 · 지도 · 부장 · 부감 · 서기 · 회계.</summary>
    public static readonly string[] Choices = ["T1040", "T1004", "T1005", "T1006", "T1008", "T1009"];
    /// <summary>처음 기본값: 부장 · 부감 · 서기(2026-11-16 사용자).</summary>
    public static readonly string[] Default = ["T1005", "T1006", "T1008"];

    public static string Name(string code) => code switch
    {
        "T1040" => "교장", "T1004" => "지도", "T1005" => "부장", "T1006" => "부감", "T1008" => "서기", "T1009" => "회계", _ => code,
    };

    public static async Task<IReadOnlyList<string>> CodesAsync(AppDbContext ctx, CancellationToken ct)
    {
        var row = await ctx.TodoRules.AsNoTracking().Where(r => r.TenantId == null && r.Kind == Kind).Select(r => r.Roles).FirstOrDefaultAsync(ct);
        return row is null ? Default : row.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(Choices.Contains).ToList();
    }

    public async Task<AdminRuleView> ViewAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        if (!await AccountService.IsActivePlatformAdminAsync(ctx, actor, ct)) throw new UnauthorizedAccessException();
        var codes = await CodesAsync(ctx, ct);
        var lobbies = await ctx.Tenants.Where(t => t.IsLobby).Select(t => t.Id).ToListAsync(ct);
        var members = await ctx.DeptRoles.AsNoTracking().Where(d => d.UserId != null && !lobbies.Contains(d.TenantId)).Select(d => new { d.UserId, d.RoleCode }).ToListAsync(ct);
        var admins = await DeptMembership.AdminIdsAsync(ctx, ct);
        var auto = (await ctx.MemberTags.AsNoTracking().Where(t => t.Tag == AutoTag).Select(t => t.UserId).ToListAsync(ct)).ToHashSet();
        var rows = Choices.Select(c => new AdminRuleRow(c, Name(c), codes.Contains(c), members.Count(m => (m.RoleCode ?? "").Trim() == c))).ToList();
        var inDept = members.Select(m => m.UserId!).ToHashSet();
        return new(rows, auto.Count(a => admins.Contains(a)), admins.Count(a => inDept.Contains(a) && !auto.Contains(a)));
    }

    /// <summary>저장 · 바로 적용: 규칙을 바꾸고 모든 사람을 맞춘다. 바뀐 수(받음, 빠짐)를 돌려준다.</summary>
    public async Task<(int Granted, int Revoked)> SaveAsync(string actor, IEnumerable<string> codes, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        if (!await AccountService.IsActivePlatformAdminAsync(ctx, actor, ct)) throw new UnauthorizedAccessException("플랫폼 관리자만 할 수 있어요.");
        var csv = string.Join(",", Choices.Where(codes.Contains));
        var row = await ctx.TodoRules.SingleOrDefaultAsync(r => r.TenantId == null && r.Kind == Kind, ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (row is null) ctx.TodoRules.Add(new TodoRule { TenantId = null, Kind = Kind, Roles = csv, UpdatedUtc = now, UpdatedByUserName = actor });
        else { row.Roles = csv; row.UpdatedUtc = now; row.UpdatedByUserName = actor; }
        await ctx.SaveChangesAsync(ct);
        var r = await SyncAsync(ctx, null, ct);
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = "DeptAdminRule",
            Detail = $"{string.Join(" · ", Choices.Where(codes.Contains).Select(Name))} · 받음 {r.Granted} · 빠짐 {r.Revoked}" });
        await ctx.SaveChangesAsync(ct);
        return r;
    }

    /// <summary>
    /// 규칙에 맞춘다(userIds 가 있으면 그 사람만): 체크된 직분인데 관리자가 아니면 관리자 + AutoAdmin, AutoAdmin 인데 더는 맞지 않으면 관리자에서 뺀다.
    /// 이미 따로 정한 관리자는 그대로 둔다. 플랫폼 관리자 · 처음 오신 분(임시 부서)은 뺀다. 계정은 지우지 않는다.
    /// </summary>
    public static async Task<(int Granted, int Revoked)> SyncAsync(AppDbContext ctx, IReadOnlyCollection<string>? userIds, CancellationToken ct)
    {
        var codes = await CodesAsync(ctx, ct);
        var lobbies = await ctx.Tenants.Where(t => t.IsLobby).Select(t => t.Id).ToListAsync(ct);
        var supers = (await (from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id where r.Name == IdentityRoles.SuperAdmin select ur.UserId).ToListAsync(ct)).ToHashSet();
        var deptQ = ctx.DeptRoles.Where(d => d.UserId != null && !lobbies.Contains(d.TenantId));
        var tagQ = ctx.MemberTags.Where(t => t.Tag == AutoTag);
        if (userIds is not null) { deptQ = deptQ.Where(d => userIds.Contains(d.UserId!)); tagQ = tagQ.Where(t => userIds.Contains(t.UserId)); }
        var depts = await deptQ.Select(d => new { UserId = d.UserId!, d.TenantId, d.RoleCode }).ToListAsync(ct);
        var tags = await tagQ.ToListAsync(ct);
        var due = depts.Where(d => codes.Contains((d.RoleCode ?? "").Trim()) && !supers.Contains(d.UserId)).GroupBy(d => d.UserId).ToDictionary(g => g.Key, g => g.First().TenantId);
        if (due.Count == 0 && tags.Count == 0) return (0, 0);
        var role = await ctx.IdentityRoleRows.FirstOrDefaultAsync(r => r.Name == IdentityRoles.Admin, ct);
        if (role is null)
        {
            ctx.IdentityRoleRows.Add(role = new AppRole { Name = IdentityRoles.Admin, NormalizedName = IdentityRoles.Admin.ToUpperInvariant() });
            await ctx.SaveChangesAsync(ct);
        }
        var ids = due.Keys.ToList();
        var admins = (await ctx.UserRoles.Where(r => r.RoleId == role.Id && ids.Contains(r.UserId)).Select(r => r.UserId).ToListAsync(ct)).ToHashSet();
        var now = DateTime.UtcNow;
        int granted = 0, revoked = 0;
        foreach (var (uid, tid) in due)
        {
            var mine = tags.Where(t => t.UserId == uid).ToList();
            if (!admins.Contains(uid))
            {
                ctx.UserRoles.Add(new AppUserRole { UserId = uid, RoleId = role.Id });
                granted++;
                ctx.MemberTags.RemoveRange(mine);
                ctx.MemberTags.Add(new MemberTag { TenantId = tid, UserId = uid, Tag = AutoTag, CreatedUtc = now });
            }
            else if (mine.Count > 0 && mine.All(t => t.TenantId != tid))   // 부서를 옮겼으면 꼬리표도 따라간다
            {
                ctx.MemberTags.RemoveRange(mine);
                ctx.MemberTags.Add(new MemberTag { TenantId = tid, UserId = uid, Tag = AutoTag, CreatedUtc = now });
            }
        }
        foreach (var g in tags.GroupBy(t => t.UserId).Where(g => !due.ContainsKey(g.Key)))
        {
            ctx.MemberTags.RemoveRange(g);
            var row = await ctx.UserRoles.SingleOrDefaultAsync(r => r.UserId == g.Key && r.RoleId == role.Id, ct);
            if (row is not null) { ctx.UserRoles.Remove(row); revoked++; }
        }
        await ctx.SaveChangesAsync(ct);
        return (granted, revoked);
    }

    /// <summary>이 사람이 규칙으로 관리자인가(직분이 체크돼 있다) — 관리 › 사람에서 관리자 해제를 막는 데 쓴다.</summary>
    public static async Task<bool> ByRuleAsync(AppDbContext ctx, string userId, CancellationToken ct)
    {
        var codes = await CodesAsync(ctx, ct);
        var lobbies = await ctx.Tenants.Where(t => t.IsLobby).Select(t => t.Id).ToListAsync(ct);
        var mine = await ctx.DeptRoles.Where(d => d.UserId == userId && !lobbies.Contains(d.TenantId)).Select(d => d.RoleCode).ToListAsync(ct);
        return mine.Any(c => codes.Contains((c ?? "").Trim()));
    }
}
