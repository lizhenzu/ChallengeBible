using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>
/// 오늘 할 일 "누가 보나" 저장(ADR 0100, 확정 시안 오늘할일 ④): 공통 규칙(플랫폼, TenantId null)과 부서 규칙("우리 부서만 따로"면 모든 종류를 부서 줄로,
/// "공통 규칙 따름"이면 부서 줄을 지운다). 업무 꼬리표(선물 담당) 켜고 끄기도 여기서.
/// </summary>
public sealed class TodoRuleService(IAppDb db, ISystemDb systemDb, TenantContext tenant, TimeProvider time)
{
    /// <summary>공통 규칙(줄이 없으면 기본값).</summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> CommonAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        var rows = await ctx.TodoRules.AsNoTracking().Where(r => r.TenantId == null).ToListAsync(ct);
        return TodoKinds.All.ToDictionary(k => k, k => rows.FirstOrDefault(r => r.Kind == k) is { } r ? TodoService.Split(r.Roles) : TodoRoles.Default(k));
    }

    public async Task SaveCommonAsync(string actor, IReadOnlyDictionary<string, IReadOnlyList<string>> rules, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        if (!await AccountService.IsActivePlatformAdminAsync(ctx, actor, ct)) throw new UnauthorizedAccessException("플랫폼 관리자만 할 수 있어요.");
        await SaveAsync(ctx, null, actor, rules, ct);
    }

    /// <summary>부서 규칙 저장(부서 관리자): custom = false 면 부서 줄을 지워 공통 규칙을 따른다.</summary>
    public async Task SaveDeptAsync(string actor, bool custom, IReadOnlyDictionary<string, IReadOnlyList<string>> rules, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        if (!custom) { ctx.TodoRules.RemoveRange(await ctx.TodoRules.Where(r => r.TenantId == tenant.Current.Id).ToListAsync(ct)); await ctx.SaveChangesAsync(ct); return; }
        await SaveAsync(ctx, tenant.Current.Id, actor, rules, ct);
    }

    private async Task SaveAsync(AppDbContext ctx, int? tenantId, string actor, IReadOnlyDictionary<string, IReadOnlyList<string>> rules, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var have = await ctx.TodoRules.Where(r => r.TenantId == tenantId).ToListAsync(ct);
        foreach (var k in TodoKinds.All)
        {
            var roles = string.Join(",", (rules.GetValueOrDefault(k) ?? []).Where(TodoRoles.All.Contains).Distinct());
            var row = have.FirstOrDefault(r => r.Kind == k);
            if (row is null) ctx.TodoRules.Add(new TodoRule { TenantId = tenantId, Kind = k, Roles = roles, UpdatedUtc = now, UpdatedByUserName = actor });
            else { row.Roles = roles; row.UpdatedUtc = now; row.UpdatedByUserName = actor; }
        }
        await ctx.SaveChangesAsync(ct);
    }

    /// <summary>사람 자세히의 업무(2026-11-16 "역할 하나 + 업무 여러 개"): 업무마다 켜짐 · 관리자 업무인지 + 관리자인 까닭.</summary>
    public async Task<(IReadOnlyList<(string Key, string Name, bool On, bool Admin)> Duties, string? AdminReason)> DutiesAsync(int membershipId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var uid = await ctx.DeptRoles.Where(d => d.Id == membershipId).Select(d => d.UserId).FirstOrDefaultAsync(ct);
        if (uid is null) return ([], null);
        var mine = (await ctx.MemberTags.Where(t => t.UserId == uid).Select(t => t.Tag).ToListAsync(ct)).ToHashSet();
        await using var sys = systemDb.Create(uid);
        var all = await sys.Duties.AsNoTracking().OrderBy(d => d.SortOrder).ThenBy(d => d.Name).ToListAsync(ct);
        var reason = await DeptAdminRuleService.ReasonAsync(sys, tenant.Current.Id, uid, ct);
        return (all.Select(d => (d.Key, d.Name, mine.Contains(d.Key), d.IsAdmin)).ToList(), reason);
    }

    /// <summary>업무 켜기 · 끄기(부서 관리자). 관리자 업무면 부서 관리자 규칙에 따라 바로 관리자가 되거나 빠진다.</summary>
    public async Task<bool> SetDutyAsync(string actor, int membershipId, string key, bool on, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var uid = await ctx.DeptRoles.Where(d => d.Id == membershipId).Select(d => d.UserId).FirstOrDefaultAsync(ct);
        if (uid is null) return false;
        await using (var sys = systemDb.Create(actor))
            if (!await sys.Duties.AnyAsync(d => d.Key == key, ct)) return false;
        var row = await ctx.MemberTags.SingleOrDefaultAsync(t => t.UserId == uid && t.Tag == key, ct);
        if (on && row is null) ctx.MemberTags.Add(new MemberTag { TenantId = tenant.Current.Id, UserId = uid, Tag = key, CreatedUtc = time.GetUtcNow().UtcDateTime, CreatedByUserName = actor });
        if (!on && row is not null) ctx.MemberTags.Remove(row);
        await ctx.SaveChangesAsync(ct);
        await using (var sys = systemDb.Create(actor)) await DeptAdminRuleService.SyncAsync(sys, [uid], ct);
        return true;
    }

    /// <summary>예전 이름(선물 담당 하나): 업무 Gift 켜기 · 끄기.</summary>
    public Task<bool> SetGiftTagAsync(string actor, int membershipId, bool on, CancellationToken ct = default) => SetDutyAsync(actor, membershipId, TodoRoles.GiftTag, on, ct);
}
