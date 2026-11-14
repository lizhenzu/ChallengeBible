using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record YearResetStatus(int Year, int Unfinished, int Awards, DateTime? LastUtc, string? LastBy, IReadOnlyList<(DateTime AtUtc, string Actor, string? Detail)> History);
public sealed record YearResetResult(int Gifts, int Awards);

/// <summary>
/// 새해 초기화(ADR 0094 정한 것 12, R-296 고침): 플랫폼 관리자가 **버튼으로** 모든 부서를 한 번에 초기화한다(저절로 하지 않음, 2026-11-14 사용자).
/// 시험 기간이라 아무 때나 몇 번이든 누를 수 있다. 하는 일: ① 끝나지 않은 선물(고를 수 있음 · 고름 · 구매 완료 · 기한 지남) → 해 넘어 닫힘
/// ② 가족팀 · 친구팀 시상 기록 → 지난 기록(TeamAwardArchive)으로 옮기고 비움(잠금이 풀리고, 같은 단계를 다시 받을 수 있음)
/// ③ 개인 선물 점수는 누른 순간부터 0에서 다시 센다(GiftYearClose.ClosedUtc 이후의 미션 점수 · 쓴 점수만). 미션 · 출석 · 통독 기록은 그대로.
/// </summary>
public sealed class YearResetService(ISystemDb systemDb, TimeProvider time)
{
    public const string Action = "GiftYearReset";
    public const string ConfirmWord = "RESET";
    public const string Zone = "Asia/Seoul";

    private int Year => TenantClock.ToTenantDate(time.GetUtcNow(), Zone).Year;

    public async Task<YearResetStatus> StatusAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        var unfinished = await ctx.Gifts.CountAsync(g => g.State == GiftStates.Open || g.State == GiftStates.Picked || g.State == GiftStates.Purchased || g.State == GiftStates.Expired, ct);
        var awards = await ctx.TeamAwards.CountAsync(ct);
        var year = (short)Year;
        var last = await ctx.GiftYearCloses.AsNoTracking().SingleOrDefaultAsync(c => c.Year == year, ct);
        var history = (await ctx.AuditLogs.AsNoTracking().Where(a => a.Action == Action).OrderByDescending(a => a.Id).Take(10)
            .Select(a => new { a.OccurredUtc, a.ActorUserName, a.Detail }).ToListAsync(ct)).Select(a => (a.OccurredUtc, a.ActorUserName, a.Detail)).ToList();
        return new YearResetStatus(Year, unfinished, awards, last?.ClosedUtc, last?.ClosedByUserName, history);
    }

    public async Task<YearResetResult> ResetAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        await using var tx = ctx.Database.IsRelational() ? await ctx.Database.BeginTransactionAsync(ct) : null;
        var now = time.GetUtcNow().UtcDateTime;
        var gifts = await ctx.Gifts.Where(g => g.State == GiftStates.Open || g.State == GiftStates.Picked || g.State == GiftStates.Purchased || g.State == GiftStates.Expired).ToListAsync(ct);
        foreach (var g in gifts)
        {
            g.State = GiftStates.YearClosed;
            ctx.GiftLogs.Add(new GiftLog { TenantId = g.TenantId, GiftId = g.Id, Action = GiftActions.YearClose, ActorUserName = actor, AtUtc = now, ItemName = g.ItemName, ItemPrice = g.ItemPrice });
        }
        var awards = await ctx.TeamAwards.ToListAsync(ct);
        foreach (var a in awards)
            ctx.TeamAwardArchives.Add(new TeamAwardArchive { TenantId = a.TenantId, Year = (short)a.AwardedAt.Year, AwardId = a.AwardId, Kind = a.Kind, AwardKey = a.AwardKey, Threshold = a.Threshold, AwardedAt = a.AwardedAt });
        ctx.TeamAwards.RemoveRange(awards);
        var year = (short)Year;
        var row = await ctx.GiftYearCloses.SingleOrDefaultAsync(c => c.Year == year, ct);
        if (row is null) ctx.GiftYearCloses.Add(new GiftYearClose { Year = year, ClosedUtc = now, ClosedByUserName = actor });
        else { row.ClosedUtc = now; row.ClosedByUserName = actor; }
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = now, ActorUserName = actor, Action = Action, Detail = $"gifts={gifts.Count};awards={awards.Count}" });
        await ctx.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new YearResetResult(gifts.Count, awards.Count);
    }

    /// <summary>그해 마지막 초기화 시각(UTC). 없으면 null — 개인 점수 · 팀 선물이 이 시각 뒤만 본다.</summary>
    public static Task<DateTime?> LastResetAsync(AppDbContext ctx, int year, CancellationToken ct) =>
        ctx.GiftYearCloses.AsNoTracking().Where(c => c.Year == year).Select(c => (DateTime?)c.ClosedUtc).SingleOrDefaultAsync(ct);
}
