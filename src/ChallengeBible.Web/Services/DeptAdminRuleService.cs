using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>부서 관리자 규칙 화면의 한 줄: 역할 코드(또는 업무 열쇠) · 이름 · 켜짐 · 지금 그 역할(업무)인 사람 수 · 고정(어드민).</summary>
public sealed record AdminRuleRow(string Code, string Name, bool On, int People, bool Fixed = false);

public sealed record AdminRuleView(IReadOnlyList<AdminRuleRow> Roles, IReadOnlyList<AdminRuleRow> Duties, int ByRule, int Manual);

/// <summary>
/// 부서 관리자 규칙(ADR 0101, 2026-11-16 사용자): 권한은 사람이 아니라 **역할 · 업무**에 붙는다.
/// 체크한 역할(한 사람에 하나)이거나 체크한 업무(한 사람에 여러 개)를 맡은 사람은 그 부서에서 바로 부서 관리자(Identity Admin)가 된다.
/// "어드민" 역할(T1030)은 늘 관리자(풀 수 없음). 사람마다 관리자를 켜는 기능은 없다 — 규칙 밖 사람은 관리자가 될 수 없다.
/// 역할 체크는 공통 할 일 규칙 표(TodoRule, TenantId NULL) Kind = "deptadmin" 한 줄, 업무 체크는 업무 목록(Duty.IsAdmin).
/// 규칙으로 준 관리자에게 꼬리표 AutoAdmin 을 붙여, 체크를 풀거나 역할 · 업무가 바뀌면 그 사람만 뺀다. 계정은 지우지 않는다(CLAUDE.md).
/// </summary>
public sealed class DeptAdminRuleService(ISystemDb systemDb, TimeProvider time)
{
    public const string Kind = "deptadmin";
    public const string AutoTag = "AutoAdmin";
    /// <summary>고를 수 있는 역할: 어드민(고정) · 교장 · 지도 · 부장 · 부감 · 서기 · 회계.</summary>
    public static readonly string[] Choices = [UserRole.AdminCode, "T1040", "T1004", "T1005", "T1006", "T1008", "T1009"];
    /// <summary>처음 기본값: 지도 · 부장 · 부감 · 서기(2026-11-16 사용자). 어드민은 늘 켜짐.</summary>
    public static readonly string[] Default = ["T1004", "T1005", "T1006", "T1008"];

    public static string Name(string code) => UserRole.Codes.FirstOrDefault(c => c.Code == code).Name ?? code;

    /// <summary>관리자가 되는 역할 코드(어드민 포함).</summary>
    public static async Task<IReadOnlyList<string>> CodesAsync(AppDbContext ctx, CancellationToken ct)
    {
        var row = await ctx.TodoRules.AsNoTracking().Where(r => r.TenantId == null && r.Kind == Kind).Select(r => r.Roles).FirstOrDefaultAsync(ct);
        var codes = row is null ? Default.ToList() : row.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(Choices.Contains).ToList();
        if (!codes.Contains(UserRole.AdminCode)) codes.Insert(0, UserRole.AdminCode);
        return codes;
    }

    public static async Task<HashSet<string>> AdminDutiesAsync(AppDbContext ctx, CancellationToken ct) =>
        (await ctx.Duties.AsNoTracking().Where(d => d.IsAdmin).Select(d => d.Key).ToListAsync(ct)).ToHashSet();

    public async Task<AdminRuleView> ViewAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        if (!await AccountService.IsActivePlatformAdminAsync(ctx, actor, ct)) throw new UnauthorizedAccessException();
        var codes = await CodesAsync(ctx, ct);
        var lobbies = await ctx.Tenants.Where(t => t.IsLobby).Select(t => t.Id).ToListAsync(ct);
        var members = await ctx.DeptRoles.AsNoTracking().Where(d => d.UserId != null && !lobbies.Contains(d.TenantId)).Select(d => new { d.UserId, d.RoleCode }).ToListAsync(ct);
        var tags = await ctx.MemberTags.AsNoTracking().Select(t => new { t.UserId, t.Tag }).ToListAsync(ct);
        var admins = await DeptMembership.AdminIdsAsync(ctx, ct);
        var auto = tags.Where(t => t.Tag == AutoTag).Select(t => t.UserId).ToHashSet();
        var roles = Choices.Select(c => new AdminRuleRow(c, Name(c), codes.Contains(c), members.Count(m => (m.RoleCode ?? "").Trim() == c), c == UserRole.AdminCode)).ToList();
        var duties = (await ctx.Duties.AsNoTracking().OrderBy(d => d.SortOrder).ThenBy(d => d.Name).ToListAsync(ct))
            .Select(d => new AdminRuleRow(d.Key, d.Name, d.IsAdmin, tags.Count(t => t.Tag == d.Key))).ToList();
        var inDept = members.Select(m => m.UserId!).ToHashSet();
        return new(roles, duties, auto.Count(a => admins.Contains(a)), admins.Count(a => inDept.Contains(a) && !auto.Contains(a)));
    }

    /// <summary>저장 · 바로 적용: 역할 · 업무 체크를 바꾸고 모든 사람을 맞춘다. 따로 정한 관리자는 어드민 역할로 옮긴다(2026-11-16 사용자).</summary>
    public async Task<(int Granted, int Revoked, int ToAdminRole)> SaveAsync(string actor, IEnumerable<string> codes, IEnumerable<string> duties, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        if (!await AccountService.IsActivePlatformAdminAsync(ctx, actor, ct)) throw new UnauthorizedAccessException("플랫폼 관리자만 할 수 있어요.");
        var picked = codes.Append(UserRole.AdminCode).ToHashSet();
        var csv = string.Join(",", Choices.Where(picked.Contains));
        var now = time.GetUtcNow().UtcDateTime;
        var row = await ctx.TodoRules.SingleOrDefaultAsync(r => r.TenantId == null && r.Kind == Kind, ct);
        if (row is null) ctx.TodoRules.Add(new TodoRule { TenantId = null, Kind = Kind, Roles = csv, UpdatedUtc = now, UpdatedByUserName = actor });
        else { row.Roles = csv; row.UpdatedUtc = now; row.UpdatedByUserName = actor; }
        var dset = duties.ToHashSet();
        foreach (var d in await ctx.Duties.ToListAsync(ct)) d.IsAdmin = dset.Contains(d.Key);
        await ctx.SaveChangesAsync(ct);
        await EnsureAdminCodeAsync(ctx, now, ct);
        var moved = await ManualToAdminRoleAsync(ctx, ct);
        var (g, r) = await SyncAsync(ctx, null, ct);
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = now, ActorUserName = actor, Action = "DeptAdminRule",
            Detail = $"{string.Join(" · ", Choices.Where(picked.Contains).Select(Name))} · 업무 {dset.Count}개 · 받음 {g} · 빠짐 {r} · 어드민으로 {moved}" });
        await ctx.SaveChangesAsync(ct);
        return (g, r, moved);
    }

    /// <summary>[업무 추가]: 이름이 겹치지 않으면 새 업무(처음엔 관리자 아님).</summary>
    public async Task<bool> AddDutyAsync(string actor, string? name, CancellationToken ct = default)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 30) return false;
        await using var ctx = systemDb.Create(actor);
        if (!await AccountService.IsActivePlatformAdminAsync(ctx, actor, ct)) throw new UnauthorizedAccessException();
        if (await ctx.Duties.AnyAsync(d => d.Name == name, ct)) return false;
        var order = (await ctx.Duties.MaxAsync(d => (int?)d.SortOrder, ct) ?? 0) + 1;
        ctx.Duties.Add(new Duty { Key = "D" + Guid.NewGuid().ToString("N")[..8], Name = name, IsAdmin = false, SortOrder = order, CreatedUtc = time.GetUtcNow().UtcDateTime });
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>부서마다 역할 코드 "어드민"(T1030)이 없으면 넣는다(역할 고르기 목록에 보이게).</summary>
    private static async Task EnsureAdminCodeAsync(AppDbContext ctx, DateTime now, CancellationToken ct)
    {
        var have = await ctx.CodeData.Where(c => c.MajorCD == CodeData.RoleMajor && c.MinorCD == UserRole.AdminCode).Select(c => c.TenantId).ToListAsync(ct);
        var with = await ctx.CodeData.Where(c => c.MajorCD == CodeData.RoleMajor).Select(c => c.TenantId).Distinct().ToListAsync(ct);
        foreach (var t in with.Except(have))
            ctx.CodeData.Add(new CodeData { TenantId = t, MajorCD = CodeData.RoleMajor, MinorCD = UserRole.AdminCode, MinorNM = Name(UserRole.AdminCode), InsertDT = now, UpdateDT = now, DisplayOrder = 0 });
        await ctx.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 따로 정한 부서 관리자(규칙 꼬리표 없음 — 역할이 부장이어도)는 역할을 "어드민"으로 옮긴다 — 관리자 그대로, 앞으로 규칙이 바뀌어도 빠지지 않는다
    /// (2026-11-16 사용자: 시험용 관리자 · 관리영~관리고는 어드민 역할로, 건드리지 마). 플랫폼 관리자는 뺀다.
    /// </summary>
    private static async Task<int> ManualToAdminRoleAsync(AppDbContext ctx, CancellationToken ct)
    {
        var codes = await CodesAsync(ctx, ct);
        var duties = await AdminDutiesAsync(ctx, ct);
        var admins = await DeptMembership.AdminIdsAsync(ctx, ct);
        var supers = await SupersAsync(ctx, ct);
        var lobbies = await ctx.Tenants.Where(t => t.IsLobby).Select(t => t.Id).ToListAsync(ct);
        var auto = (await ctx.MemberTags.Where(t => t.Tag == AutoTag).Select(t => t.UserId).ToListAsync(ct)).ToHashSet();
        var tags = await ctx.MemberTags.Where(t => duties.Contains(t.Tag)).Select(t => new { t.UserId, t.TenantId }).ToListAsync(ct);
        var rows = await ctx.DeptRoles.Where(d => d.UserId != null && !lobbies.Contains(d.TenantId) && admins.Contains(d.UserId!)).ToListAsync(ct);
        var n = 0;
        foreach (var d in rows.Where(d => !auto.Contains(d.UserId!) && !supers.Contains(d.UserId!)))
        {
            if ((d.RoleCode ?? "").Trim() == UserRole.AdminCode) continue;
            d.RoleCode = UserRole.AdminCode;   // 부장 역할이어도 부서 관리자 계정이면 어드민(2026-11-16 사용자)
            d.UpdateDT = DateTime.UtcNow;
            n++;
        }
        await ctx.SaveChangesAsync(ct);
        return n;
    }

    private static async Task<HashSet<string>> SupersAsync(AppDbContext ctx, CancellationToken ct) =>
        (await (from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id where r.Name == IdentityRoles.SuperAdmin select ur.UserId).ToListAsync(ct)).ToHashSet();

    /// <summary>
    /// 규칙에 맞춘다(userIds 가 있으면 그 사람만): 체크된 역할 · 업무인데 관리자가 아니면 관리자 + AutoAdmin, AutoAdmin 인데 더는 맞지 않으면 관리자에서 뺀다.
    /// 이미 관리자인 사람(어드민 역할 등)은 그대로. 플랫폼 관리자 · 처음 오신 분(임시 부서)은 뺀다. 계정은 지우지 않는다.
    /// </summary>
    public static async Task<(int Granted, int Revoked)> SyncAsync(AppDbContext ctx, IReadOnlyCollection<string>? userIds, CancellationToken ct)
    {
        var codes = await CodesAsync(ctx, ct);
        var duties = await AdminDutiesAsync(ctx, ct);
        var lobbies = await ctx.Tenants.Where(t => t.IsLobby).Select(t => t.Id).ToListAsync(ct);
        var supers = await SupersAsync(ctx, ct);
        var deptQ = ctx.DeptRoles.Where(d => d.UserId != null && !lobbies.Contains(d.TenantId));
        var tagQ = ctx.MemberTags.Where(t => t.Tag == AutoTag || duties.Contains(t.Tag));
        if (userIds is not null) { deptQ = deptQ.Where(d => userIds.Contains(d.UserId!)); tagQ = tagQ.Where(t => userIds.Contains(t.UserId)); }
        var depts = await deptQ.Select(d => new { UserId = d.UserId!, d.TenantId, d.RoleCode }).ToListAsync(ct);
        var all = await tagQ.ToListAsync(ct);
        var tags = all.Where(t => t.Tag == AutoTag).ToList();
        var dutyTags = all.Where(t => t.Tag != AutoTag).ToList();
        var due = depts.Where(d => !supers.Contains(d.UserId)
                && (codes.Contains((d.RoleCode ?? "").Trim()) || dutyTags.Any(t => t.UserId == d.UserId && t.TenantId == d.TenantId)))
            .GroupBy(d => d.UserId).ToDictionary(g => g.Key, g => g.First().TenantId);
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

    /// <summary>이 사람이 관리자인 까닭(역할 · 업무 이름), 규칙 밖이면 null — 사람 자세히에 보인다.</summary>
    public static async Task<string?> ReasonAsync(AppDbContext ctx, int tenantId, string userId, CancellationToken ct)
    {
        var codes = await CodesAsync(ctx, ct);
        var role = await ctx.DeptRoles.Where(d => d.UserId == userId && d.TenantId == tenantId).Select(d => d.RoleCode).FirstOrDefaultAsync(ct);
        if (codes.Contains((role ?? "").Trim())) return $"역할 \"{Name(role!.Trim())}\"";
        var duties = await ctx.Duties.AsNoTracking().Where(d => d.IsAdmin).ToListAsync(ct);
        var keys = duties.Select(d => d.Key).ToList();
        var mine = await ctx.MemberTags.Where(t => t.UserId == userId && t.TenantId == tenantId && keys.Contains(t.Tag)).Select(t => t.Tag).ToListAsync(ct);
        return mine.Count == 0 ? null : "업무 " + string.Join(" · ", duties.Where(d => mine.Contains(d.Key)).Select(d => $"\"{d.Name}\""));
    }
}
