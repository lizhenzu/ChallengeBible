using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>부서 하나의 밀린 일(보기만): 선물 사기 · 전하기 · 고르는 중 · 승인 · 진급 · (주일) 출석한 반.</summary>
public sealed record PlatformTodoDept(int Id, string Name, int Buy, int BuyLate, int Give, int Picking, int Signups, int Promotions, int? ClassesDone, int? Classes)
{
    public int Late => BuyLate;
    public bool Any => Buy + Give + Picking + Signups + Promotions > 0 || (Classes is int c && ClassesDone < c);
}

/// <summary>
/// 플랫폼 관리자의 오늘 할 일(ADR 0100, 확정 시안 오늘할일 ③): 내 할 일(플랫폼 홈의 챙길 일 — 곧 비는 공용 콘텐츠 · 관리자 없는 부서 등)은 처리하고,
/// 부서 일은 부서별로 "어디서 밀렸나" 보기만(R-312). 밀림 = 3일 넘게 아무도 사지 않은 선물. 시스템 연결, 플랫폼 관리자만.
/// </summary>
public sealed class PlatformTodoService(ISystemDb systemDb, PlatformHomeService home, TimeProvider time)
{
    public async Task<(IReadOnlyList<PlatformTodo> Mine, IReadOnlyList<PlatformTodoDept> Depts)> GetAsync(string actor, CancellationToken ct = default)
    {
        var mine = (await home.GetAsync(actor, ct)).Todos;   // 플랫폼 관리자인지는 여기서 다시 확인한다
        await using var ctx = systemDb.Create(actor);
        var now = time.GetUtcNow().UtcDateTime;
        var late = now.AddDays(-TodoService.LateDays);
        var tenants = await ctx.Tenants.AsNoTracking().Where(t => !t.IsLobby && t.Status == TenantStatus.Active).Select(t => new { t.Id, t.Name, t.TimeZoneId }).ToListAsync(ct);
        var gifts = await ctx.Gifts.AsNoTracking().Where(g => g.State == GiftStates.Picked || g.State == GiftStates.Purchased || g.State == GiftStates.Open || g.State == GiftStates.Expired)
            .Select(g => new { g.TenantId, g.State, g.PickedUtc }).ToListAsync(ct);
        var signups = await ctx.SignupApplications.AsNoTracking().Where(a => a.Status == SignupStatus.Pending).GroupBy(a => a.TenantId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var promos = await ctx.PromotionRequests.AsNoTracking().Where(r => r.Status == PromotionStatus.Pending).GroupBy(r => r.TargetTenantId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var classes = await ctx.Classes.AsNoTracking().Where(c => !c.IsNewcomer).Select(c => new { c.TenantId, c.Id }).ToListAsync(ct);
        var kids = await ctx.ClassMembers.AsNoTracking().Where(m => !m.IsTeacher).Select(m => new { m.TenantId, m.ClassId, m.PersonId }).ToListAsync(ct);
        var rows = new List<PlatformTodoDept>();
        foreach (var t in tenants)
        {
            var today = TenantClock.ToTenantDate(time.GetUtcNow(), t.TimeZoneId);
            int? done = null, total = null;
            if (today.DayOfWeek == DayOfWeek.Sunday)   // 출석은 주일 당일만
            {
                var checkedIds = (await ctx.AttendanceChecks.AsNoTracking().Where(a => a.TenantId == t.Id && a.Sunday == today).Select(a => a.PersonId).ToListAsync(ct)).ToHashSet();
                var withKids = classes.Where(c => c.TenantId == t.Id && kids.Any(k => k.TenantId == t.Id && k.ClassId == c.Id)).ToList();
                total = withKids.Count;
                done = withKids.Count(c => kids.Any(k => k.TenantId == t.Id && k.ClassId == c.Id && checkedIds.Contains(k.PersonId)));
            }
            var mineGifts = gifts.Where(g => g.TenantId == t.Id).ToList();
            rows.Add(new(t.Id, t.Name, mineGifts.Count(g => g.State == GiftStates.Picked), mineGifts.Count(g => g.State == GiftStates.Picked && g.PickedUtc < late),
                mineGifts.Count(g => g.State == GiftStates.Purchased), mineGifts.Count(g => g.State is GiftStates.Open or GiftStates.Expired),
                signups.GetValueOrDefault(t.Id), promos.GetValueOrDefault(t.Id), done, total));
        }
        return (mine, rows.OrderBy(r => DeptOrder.Key(r.Name)).ThenBy(r => r.Name).ToList());
    }

    private DateOnly Today => TenantClock.ToTenantDate(time.GetUtcNow(), PlatformHomeService.PlatformTimeZone);

    /// <summary>안 보기 기간인가(플랫폼 날짜 = 한국 시간).</summary>
    public async Task<bool> SnoozedAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        var until = await ctx.UserSettings.AsNoTracking().Where(s => s.UserName == actor).Select(s => s.TodoSnoozedUntil).FirstOrDefaultAsync(ct);
        return until is { } u && u > Today;
    }

    /// <summary>안 보기(R-309): days 일 동안 자동 이동하지 않는다.</summary>
    public async Task SnoozeAsync(string actor, int days, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        var s = await ctx.UserSettings.SingleOrDefaultAsync(x => x.UserName == actor, ct);
        if (s is null) { s = new UserSetting { UserName = actor }; ctx.UserSettings.Add(s); }
        s.TodoSnoozedUntil = Today.AddDays(Math.Clamp(days, 1, 31));
        await ctx.SaveChangesAsync(ct);
    }
}
