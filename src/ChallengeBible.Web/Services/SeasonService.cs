using System.Globalization;
using ChallengeBible.Domain.Seasons;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>시작일을 바꾸면 시즌 점수에서 빠지는 것(0028: 늦출 때만). Future = 새 시작일이 오늘보다 뒤 → 미션이 닫힌다.</summary>
public sealed record SeasonImpact(DateOnly NewStart, bool Later, bool Future, DateOnly From, DateOnly To, int Participants, int Points);
public sealed record PastSeason(int Year, DateOnly Start, int Scorers, int Points);
public sealed record SeasonView(DateOnly Today, Season Current, bool CurrentIsDefault, Season Next, bool NextIsDefault,
    int ParticipantCount, int SeasonPoints, int Scorers, IReadOnlyList<AuditLine> Log, IReadOnlyList<PastSeason> Past);

/// <summary>
/// 부서 시즌(0028, 확정 시안: 관리-시즌). 시즌은 달력 연도, 부서는 그해 시작일만 정한다(행이 없으면 1월 1일).
/// 시즌 점수는 원장에서 바로 계산하므로(R-62) 시작일을 바꾸면 따로 다시 계산할 것이 없다. 원장 · 수행 기록은 지우지 않는다.
/// </summary>
public sealed class SeasonService(IAppDb db, TenantContext tenant, TenantClock clock, TimeProvider time)
{
    public const string LogAction = "SeasonStart";
    private static readonly CultureInfo Ko = new("ko-KR");
    public static string Md(DateOnly d) => d.ToString("M월 d일", Ko);

    /// <summary>빠른 고르기: 그해 1월 1일과 첫 주일.</summary>
    public static (DateOnly NewYear, DateOnly FirstSunday) QuickDates(int year)
    {
        var jan1 = new DateOnly(year, 1, 1);
        return (jan1, jan1.AddDays(((int)DayOfWeek.Sunday - (int)jan1.DayOfWeek + 7) % 7));
    }

    private DateOnly Today => clock.Today(tenant.Current.TimeZoneId);

    private static async Task<(Season Season, bool IsDefault)> SeasonAsync(AppDbContext ctx, int year, CancellationToken ct)
    {
        var start = await ctx.TenantSeasons.Where(s => s.Year == year).Select(s => (DateOnly?)s.StartDate).SingleOrDefaultAsync(ct);
        return start is { } d ? (Season.Create(year, d), false) : (Season.DefaultFor(year), true);
    }

    /// <summary>참여자별 점수 합(그 날짜 범위). 범위가 비면 빈 목록.</summary>
    private static async Task<List<int>> PointsByParticipantAsync(AppDbContext ctx, DateOnly from, DateOnly to, CancellationToken ct) =>
        from > to ? [] : await ctx.PointLedgers.Where(l => l.MissionDate >= from && l.MissionDate <= to)
            .GroupBy(l => l.ParticipantId).Select(g => g.Sum(l => l.Points)).Where(p => p != 0).ToListAsync(ct);

    public async Task<SeasonView> GetAsync(CancellationToken ct = default)
    {
        var today = Today;
        await using var ctx = db.Create();
        var (cur, curDefault) = await SeasonAsync(ctx, today.Year, ct);
        var (next, nextDefault) = await SeasonAsync(ctx, today.Year + 1, ct);
        var (from, to) = cur.ScoringRange(today);
        var points = await PointsByParticipantAsync(ctx, from, to, ct);
        var participants = await ctx.Participants.CountAsync(p => p.IsActive, ct);

        var past = new List<PastSeason>();
        var firstYear = await ctx.PointLedgers.Where(l => l.MissionDate < new DateOnly(today.Year, 1, 1))
            .OrderBy(l => l.MissionDate).Select(l => (DateOnly?)l.MissionDate).FirstOrDefaultAsync(ct);
        if (firstYear is { } fy)
            for (var y = today.Year - 1; y >= fy.Year; y--)
            {
                var (s, _) = await SeasonAsync(ctx, y, ct);
                var p = await PointsByParticipantAsync(ctx, s.StartDate, s.EndDate, ct);
                if (p.Count > 0) past.Add(new PastSeason(y, s.StartDate, p.Count, p.Sum()));
            }

        return new(today, cur, curDefault, next, nextDefault, participants, points.Sum(), points.Count,
            await AuditLines.ForTenantAsync(ctx, tenant.Current.Id, LogAction, ct), past);
    }

    /// <summary>올해 시작일을 newStart 로 바꾸면 빠지는 점수(늦출 때: 지금 시작일 ~ min(새 시작일 전날, 오늘)).</summary>
    public async Task<SeasonImpact?> ImpactAsync(DateOnly newStart, CancellationToken ct = default)
    {
        var today = Today;
        if (newStart.Year != today.Year) return null;
        await using var ctx = db.Create();
        var (cur, _) = await SeasonAsync(ctx, today.Year, ct);
        if (newStart <= cur.StartDate) return new(newStart, false, newStart > today, cur.StartDate, cur.StartDate, 0, 0);
        var to = newStart.AddDays(-1) < today ? newStart.AddDays(-1) : today;
        var points = await PointsByParticipantAsync(ctx, cur.StartDate, to, ct);
        return new(newStart, true, newStart > today, cur.StartDate, to, points.Count, points.Sum());
    }

    /// <summary>올해 또는 다음 해 시작일을 정한다. 틀리면 오류 문구, 같으면 아무것도 하지 않는다(Changed=false).</summary>
    public async Task<(bool Changed, string? Error)> SaveAsync(int actor, DateOnly newStart, CancellationToken ct = default)
    {
        var today = Today;
        if (newStart.Year != today.Year && newStart.Year != today.Year + 1)
            return (false, "올해나 다음 해 날짜만 고를 수 있어요.");
        await using var ctx = db.Create();
        var row = await ctx.TenantSeasons.SingleOrDefaultAsync(s => s.Year == newStart.Year, ct);
        var old = row?.StartDate ?? new DateOnly(newStart.Year, 1, 1);
        if (old == newStart && row is not null) return (false, null);
        var now = time.GetUtcNow().UtcDateTime;
        if (row is null)
            ctx.TenantSeasons.Add(new TenantSeason { TenantId = tenant.Current.Id, Year = (short)newStart.Year, StartDate = newStart, ChangedUtc = now, ChangedByAccountId = actor });
        else
            (row.StartDate, row.ChangedUtc, row.ChangedByAccountId) = (newStart, now, actor);
        var detail = old == newStart ? $"{newStart.Year} 시즌 시작일 {Md(newStart)}로 정함(기본값)" : $"{newStart.Year} 시즌 시작일 {Md(old)} → {Md(newStart)}";
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = now, ActorAccountId = actor, Action = LogAction, TenantId = tenant.Current.Id, Detail = detail });
        await ctx.SaveChangesAsync(ct);
        return (true, null);
    }
}
