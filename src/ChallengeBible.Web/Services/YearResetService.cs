using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>지울 것의 건수: 미션 기록(퀴즈 풀이 포함) · 출석(새친구 제외) · 선물(완독 제외) · 시상 기록(지난 기록 포함) · 감사 로그.</summary>
public sealed record YearResetCounts(int Missions, int Attendance, int Gifts, int Awards, int Audits)
{
    public string Detail => $"missions={Missions};attend={Attendance};gifts={Gifts};awards={Awards};audits={Audits}";
}
public sealed record YearResetStatus(int Year, YearResetCounts Counts, DateTime? LastUtc, string? LastBy, IReadOnlyList<(DateTime AtUtc, string Actor, string? Detail)> History);

/// <summary>
/// 새해 초기화(ADR 0094 정한 것 17 — 12 를 바꿈, R-296): 플랫폼 관리자가 **버튼으로 모든 부서를 한 번에, 완전히 지운다**(되돌릴 수 없음, 2026-11-15 사용자).
/// 지우는 것: 미션 기록(점수 · 순위 · 시즌 점수 · 가족팀 · 친구팀 점수 · 개인 선물 점수가 0), 시상 기록 · 지난 시상 기록, **새친구가 아닌 사람의** 출석,
/// **완독을 뺀** 모든 선물(끝난 것 포함)과 그 이력 · 쓴 점수, 감사 로그 전부(지운 뒤 이번 초기화 한 줄만 남김).
/// 그대로: 가족팀 · 친구팀 구성, 새친구 출석 · 새친구 표시, 성경통독 전부(완독 선물 · 이력 포함, 해가 바뀌어도 초기화하지 않음), 명부 · 계정 · 소속 · 상품 · 설정 · 콘텐츠.
/// 초기화 뒤 새친구 · 전도는 초기화 뒤에 4번째 출석을 채운 새친구만, 생일은 고르는 기간이 초기화 뒤에 시작된 아이만 다시 생긴다(LastResetAsync).
/// </summary>
public sealed class YearResetService(ISystemDb systemDb, TimeProvider time)
{
    public const string Action = "GiftYearReset";
    public const string ConfirmWord = "RESET";
    public const string Zone = "Asia/Seoul";

    private int Year => TenantClock.ToTenantDate(time.GetUtcNow(), Zone).Year;

    // 시스템 연결(모든 부서). 새친구 = 명부의 첫 방문일이 있는 사람
    private static IQueryable<AttendanceCheck> Attendance(AppDbContext ctx) =>
        ctx.AttendanceChecks.Where(a => !ctx.People.Any(p => p.TenantId == a.TenantId && p.Id == a.PersonId && p.NewcomerFirstVisit != null));
    private static IQueryable<Gift> Gifts(AppDbContext ctx) => ctx.Gifts.Where(g => g.Kind != GiftKinds.Reading);

    private static async Task<YearResetCounts> CountAsync(AppDbContext ctx, CancellationToken ct) => new(
        await ctx.MissionActivities.CountAsync(ct) + await ctx.QuizAttempts.CountAsync(ct),
        await Attendance(ctx).CountAsync(ct),
        await Gifts(ctx).CountAsync(ct),
        await ctx.TeamAwards.CountAsync(ct) + await ctx.TeamAwardArchives.CountAsync(ct),
        await ctx.AuditLogs.CountAsync(ct));

    public async Task<YearResetStatus> StatusAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        var year = (short)Year;
        var last = await ctx.GiftYearCloses.AsNoTracking().SingleOrDefaultAsync(c => c.Year == year, ct);
        var history = (await ctx.AuditLogs.AsNoTracking().Where(a => a.Action == Action).OrderByDescending(a => a.Id).Take(10)
            .Select(a => new { a.OccurredUtc, a.ActorUserName, a.Detail }).ToListAsync(ct)).Select(a => (a.OccurredUtc, a.ActorUserName, a.Detail)).ToList();
        return new YearResetStatus(Year, await CountAsync(ctx, ct), last?.ClosedUtc, last?.ClosedByUserName, history);
    }

    /// <summary>지우기: DB 에서는 한 번에(ExecuteDelete), 시험용 메모리 DB 는 읽어서 지운다.</summary>
    private static async Task DeleteAsync<T>(AppDbContext ctx, IQueryable<T> q, CancellationToken ct) where T : class
    {
        if (ctx.Database.IsRelational()) await q.ExecuteDeleteAsync(ct);
        else { ctx.Set<T>().RemoveRange(await q.ToListAsync(ct)); await ctx.SaveChangesAsync(ct); }
    }

    public async Task<YearResetCounts> ResetAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        await using var tx = ctx.Database.IsRelational() ? await ctx.Database.BeginTransactionAsync(ct) : null;
        var now = time.GetUtcNow().UtcDateTime;
        var counts = await CountAsync(ctx, ct);
        var gifts = Gifts(ctx).Select(g => g.Id);
        await DeleteAsync(ctx, ctx.GiftLogs.Where(l => gifts.Contains(l.GiftId)), ct);
        await DeleteAsync(ctx, ctx.GiftPointUses.Where(u => gifts.Contains(u.GiftId)), ct);
        await DeleteAsync(ctx, Gifts(ctx), ct);
        await DeleteAsync(ctx, ctx.TeamAwards, ct);
        await DeleteAsync(ctx, ctx.TeamAwardArchives, ct);
        await DeleteAsync(ctx, ctx.QuizAttempts, ct);
        await DeleteAsync(ctx, ctx.MissionActivities, ct);
        await DeleteAsync(ctx, Attendance(ctx), ct);
        await DeleteAsync(ctx, ctx.AuditLogs, ct);

        var year = (short)Year;
        var row = await ctx.GiftYearCloses.SingleOrDefaultAsync(c => c.Year == year, ct);
        if (row is null) ctx.GiftYearCloses.Add(new GiftYearClose { Year = year, ClosedUtc = now, ClosedByUserName = actor });
        else { row.ClosedUtc = now; row.ClosedByUserName = actor; }
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = now, ActorUserName = actor, Action = Action, Detail = counts.Detail });   // 다 지운 뒤 이번 초기화 한 줄만
        await ctx.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return counts;
    }

    /// <summary>그해 마지막 초기화 시각(UTC). 없으면 null — 개인 점수 · 팀 선물이 이 시각 뒤만 본다.</summary>
    public static Task<DateTime?> LastResetAsync(AppDbContext ctx, int year, CancellationToken ct) =>
        ctx.GiftYearCloses.AsNoTracking().Where(c => c.Year == year).Select(c => (DateTime?)c.ClosedUtc).SingleOrDefaultAsync(ct);

    /// <summary>가장 최근 초기화 날(부서 날짜, 해와 상관없이). 새친구 · 전도 · 생일 선물은 이날 뒤에 시작된 것만 다시 생긴다. 없으면 null.</summary>
    public static async Task<DateOnly?> LastResetDateAsync(AppDbContext ctx, string timeZoneId, CancellationToken ct)
    {
        var at = await ctx.GiftYearCloses.AsNoTracking().OrderByDescending(c => c.ClosedUtc).Select(c => (DateTime?)c.ClosedUtc).FirstOrDefaultAsync(ct);
        return at is { } u ? TenantClock.ToTenantDate(new DateTimeOffset(DateTime.SpecifyKind(u, DateTimeKind.Utc)), timeZoneId) : null;
    }
}
