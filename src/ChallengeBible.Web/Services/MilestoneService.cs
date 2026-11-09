using System.Globalization;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>관리 › 시즌 "우리 부서 축하 단계" 화면 값. Own = 부서가 정한 값을 쓰는 중, Passed = 단계마다 이미 넘은 참여자 수.</summary>
public sealed record MilestoneView(IReadOnlyList<int> Current, bool Own, IReadOnlyList<int> Platform, IReadOnlyDictionary<int, int> Passed, IReadOnlyList<AuditLine> Log);

/// <summary>
/// 부서마다 축하 단계(0077, R-100). 값은 부서 표 `Milestones`("1000,2000,3000"), 비우면 플랫폼 기본 규칙 값.
/// 규칙 읽기(<see cref="RuleService.ForDateAsync"/>)가 부서 값으로 덮으므로 축하 창 · 다음 축하 · 목표 점수가 저절로 따른다.
/// 바꿀 때 이미 넘은 단계는 "받은 것"으로 기록해서 축하 창이 한꺼번에 뜨지 않게 한다(R-104).
/// </summary>
public sealed class MilestoneService(IAppDb db, TenantContext tenant, RuleService rules, TenantClock clock, TimeProvider time)
{
    public const string LogAction = "TenantMilestones";
    public const int MaxCount = 10, MinPoints = 100, MaxPoints = 1_000_000;

    /// <summary>"1000,2000" → [1000, 2000]. 비었거나 숫자가 아니면 null(플랫폼 값을 쓴다).</summary>
    public static IReadOnlyList<int>? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var list = new List<int>();
        foreach (var part in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var n)) return null;
            list.Add(n);
        }
        return list.Count == 0 ? null : list.Distinct().Order().ToList();
    }

    public static string Format(IEnumerable<int> list) => string.Join(",", list);
    public static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>틀린 곳이 있으면 안내 문장, 괜찮으면 null.</summary>
    public static string? Check(IReadOnlyList<int> list)
    {
        if (list.Count == 0) return "축하 단계는 하나 이상 있어야 해요.";
        if (list.Count > MaxCount) return $"축하 단계는 {MaxCount}개까지예요.";
        if (list.Any(n => n < MinPoints)) return $"단계 점수는 {N(MinPoints)}점 이상이에요.";
        if (list.Any(n => n > MaxPoints)) return $"단계 점수는 {N(MaxPoints)}점까지예요.";
        return null;
    }

    private DateOnly Today => clock.Today(tenant.Current.TimeZoneId);

    private async Task<IReadOnlyList<int>> PlatformAsync(CancellationToken ct) => RuleService.Pick(await rules.VersionsAsync(ct), Today).Milestones;

    /// <summary>올해 시즌 점수(참여자별).</summary>
    private async Task<Dictionary<int, int>> SeasonPointsAsync(AppDbContext ctx, CancellationToken ct)
    {
        var today = Today;
        var start = await ctx.TenantSeasons.Where(s => s.Year == today.Year).Select(s => (DateOnly?)s.StartDate).SingleOrDefaultAsync(ct);
        var season = start is { } d ? Domain.Seasons.Season.Create(today.Year, d) : Domain.Seasons.Season.DefaultFor(today.Year);
        var (from, to) = season.ScoringRange(today);
        if (from > to) return [];
        return await ctx.PointLedgers.Where(l => l.MissionDate >= from && l.MissionDate <= to)
            .GroupBy(l => l.ParticipantId).Select(g => new { g.Key, P = g.Sum(l => l.Points) }).ToDictionaryAsync(x => x.Key, x => x.P, ct);
    }

    /// <summary>화면 값. draft 를 주면 그 목록으로 미리 본다(저장 전).</summary>
    public async Task<MilestoneView> GetAsync(IReadOnlyList<int>? draft = null, CancellationToken ct = default)
    {
        var own = Parse(tenant.Current.Milestones);
        var platform = await PlatformAsync(ct);
        var current = draft ?? own ?? platform;
        await using var ctx = db.Create();
        var points = await SeasonPointsAsync(ctx, ct);
        var passed = current.ToDictionary(m => m, m => points.Values.Count(p => p >= m));
        return new(current, own is not null, platform, passed, await AuditLines.ForTenantAsync(ctx, tenant.Current.Id, LogAction, ct));
    }

    /// <summary>저장(null = 플랫폼 기본으로). 바뀐 것이 없으면 아무것도 하지 않는다.</summary>
    public async Task<(bool Changed, string? Error)> SaveAsync(string actor, IReadOnlyList<int>? list, CancellationToken ct = default)
    {
        var clean = list?.Distinct().Order().ToList();
        if (clean is not null && Check(clean) is { } err) return (false, err);

        await using var ctx = db.Create();
        var t = await ctx.Tenants.SingleAsync(x => x.Id == tenant.Current.Id, ct);
        var before = Parse(t.Milestones);
        var value = clean is null ? null : Format(clean);
        if (t.Milestones == value) return (false, null);

        var platform = await PlatformAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        var year = (short)Today.Year;
        var after = clean ?? platform;
        t.Milestones = value;

        // R-104: 새 단계 가운데 이미 넘은 것은 받은 것으로 기록 — 다음 미션 때 축하 창이 한꺼번에 뜨지 않게
        var points = await SeasonPointsAsync(ctx, ct);
        var done = await ctx.MilestonesReached.Where(m => m.Year == year).Select(m => new { m.ParticipantId, m.Threshold }).ToListAsync(ct);
        var have = done.Select(x => (x.ParticipantId, x.Threshold)).ToHashSet();
        foreach (var (pid, p) in points)
            foreach (var m in after.Where(m => p >= m && !have.Contains((pid, m))))
                ctx.MilestonesReached.Add(new MilestoneReached { TenantId = t.Id, ParticipantId = pid, Year = year, Threshold = m, ReachedUtc = now });

        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = now, ActorUserName = actor, Action = LogAction, TenantId = t.Id,
            Detail = $"축하 단계 {Label(before)} → {Label(clean)}" });
        await ctx.SaveChangesAsync(ct);
        return (true, null);
    }

    private static string Label(IReadOnlyList<int>? l) => l is null ? "플랫폼 기본" : string.Join(" · ", l.Select(N));
}
