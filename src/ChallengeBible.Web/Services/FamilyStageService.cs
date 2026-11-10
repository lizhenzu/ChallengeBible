using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>시상 단계 한 줄. Id 가 null 이면 플랫폼 기본값(읽기만). Given = 이 단계를 받은 팀 수, Locked = 점수 · 지우기 잠금(R-219).</summary>
public sealed record StageRow(int? Id, int Points, string Type, string Title, string Emoji, bool On, int Given, bool Locked);

/// <summary>Own = 그해 우리 부서 기준이 있다. 없으면 Rows 는 플랫폼 기본값.</summary>
public sealed record StageBoard(bool Enabled, int ThisYear, int Year, bool Own, IReadOnlyList<StageRow> Rows)
{
    public int OnCount => Rows.Count(r => r.On);
    public bool AnyLocked => Rows.Any(r => r.Locked);
}

public enum StageError { None, Disabled, BadYear, NotOwn, AlreadyOwn, NotFound, BadPoints, Duplicate, BadTitle, BadEmoji, BadType, LastOn, Locked }

/// <summary>
/// 부서 시상 기준(R-219, ADR 0091): `DM2_FamilyMilestoneConfig` 의 그해 줄. 이 부서 연결로만 읽고 쓴다(전역 필터 + RLS).
/// 읽기 쪽(그해 켜진 줄이 없으면 기본값)은 FamilyTeamService.StagesAsync.
/// </summary>
public sealed class FamilyStageService(IAppDb db, TenantContext tenant, TenantClock clock, RuleService rules)
{
    public const int MaxPoints = 100_000, MaxTitle = 50, MaxEmoji = 8;

    public static string Message(StageError e) => e switch
    {
        StageError.BadYear => "올해와 내년만 정할 수 있어요.",
        StageError.NotOwn => "먼저 “우리 부서 기준으로 정하기”를 눌러 주세요.",
        StageError.AlreadyOwn => "이미 우리 부서 기준이 있어요.",
        StageError.NotFound => "그 단계를 찾을 수 없어요. 다시 열어 주세요.",
        StageError.BadPoints => $"점수는 1 ~ {MaxPoints:N0}점 사이로 써 주세요.",
        StageError.Duplicate => "같은 점수의 단계가 이미 있어요. 다른 점수를 쓰거나 그 단계를 고쳐 주세요.",
        StageError.BadTitle => $"축하 문구를 1 ~ {MaxTitle}자로 써 주세요.",
        StageError.BadEmoji => $"그림은 이모지 하나로 써 주세요({MaxEmoji}자까지).",
        StageError.BadType => "축하 종류를 골라 주세요.",
        StageError.LastOn => "켜진 단계가 하나는 있어야 해요.",
        StageError.Locked => "이미 가족이 받은 단계라 점수를 바꾸거나 지울 수 없어요.",
        _ => "가족팀 기능이 꺼져 있어요.",
    };

    private async Task<(RuleSet Rules, int ThisYear)> RulesAsync(CancellationToken ct)
    {
        var today = clock.Today(tenant.Current.TimeZoneId);
        return (await rules.ForDateAsync(today, ct), today.Year);
    }

    public async Task<StageBoard> GetAsync(int? year, CancellationToken ct = default)
    {
        var (r, thisYear) = await RulesAsync(ct);
        var y = year ?? thisYear;
        if (!r.Features.FamilyTeam || (y != thisYear && y != thisYear + 1)) return new StageBoard(r.Features.FamilyTeam, thisYear, y, false, []);
        await using var ctx = db.Create();
        var rows = await RowsAsync(ctx, y, ct);
        if (rows.Count > 0) return new StageBoard(true, thisYear, y, true, rows);
        var defaults = (await DefaultsAsync(y, ct)).Select(d => new StageRow(null, d.Points, TypeOf(d.Kind), d.Message, d.Emoji, d.Enabled, 0, false)).ToList();
        return new StageBoard(true, thisYear, y, false, defaults);
    }

    /// <summary>우리 부서 기준으로 정하기: 플랫폼 기본 단계를 그해 줄로 복사한다.</summary>
    public Task<StageError> OwnAsync(int year, CancellationToken ct = default) =>
        WithYearAsync(year, async (ctx, rows) =>
        {
            if (rows.Count > 0) return StageError.AlreadyOwn;
            var now = clock.UtcNow.UtcDateTime;
            var order = 0;
            foreach (var d in await DefaultsAsync(year, ct))
                ctx.FamilyMilestoneConfigs.Add(New(year, d.Points, TypeOf(d.Kind), d.Message, d.Emoji, d.Enabled, order++, now));
            return StageError.None;
        }, ct);

    /// <summary>기본값으로 되돌리기: 그해 줄을 모두 지운다. 잠긴 단계가 있으면 막는다.</summary>
    public Task<StageError> ResetAsync(int year, CancellationToken ct = default) =>
        WithYearAsync(year, async (ctx, rows) =>
        {
            if (rows.Count == 0) return StageError.NotOwn;
            foreach (var c in rows) if (await IsLockedAsync(ctx, year, c, ct)) return StageError.Locked;
            ctx.FamilyMilestoneConfigs.RemoveRange(rows);
            return StageError.None;
        }, ct);

    public Task<StageError> AddAsync(int year, int points, string? type, string? title, string? emoji, CancellationToken ct = default) =>
        WithYearAsync(year, (ctx, rows) =>
        {
            if (rows.Count == 0) return Task.FromResult(StageError.NotOwn);
            if (Check(points, type, title, emoji) is var bad and not StageError.None) return Task.FromResult(bad);
            if (rows.Any(c => c.PointThreshold == points)) return Task.FromResult(StageError.Duplicate);
            ctx.FamilyMilestoneConfigs.Add(New(year, points, type!, title!.Trim(), emoji!.Trim(), true, rows.Max(c => c.SortOrder) + 1, clock.UtcNow.UtcDateTime));
            return Task.FromResult(StageError.None);
        }, ct);

    public Task<StageError> UpdateAsync(int year, int id, int points, string? type, string? title, string? emoji, CancellationToken ct = default) =>
        WithYearAsync(year, async (ctx, rows) =>
        {
            if (rows.SingleOrDefault(c => c.Id == id) is not { } c) return StageError.NotFound;
            if (Check(points, type, title, emoji) is var bad and not StageError.None) return bad;
            if (points != c.PointThreshold)
            {
                if (await IsLockedAsync(ctx, year, c, ct)) return StageError.Locked;
                if (rows.Any(x => x.Id != id && x.PointThreshold == points)) return StageError.Duplicate;
            }
            c.PointThreshold = points; c.MilestoneType = type!; c.DisplayTitle = title!.Trim(); c.DisplayEmoji = emoji!.Trim();
            return StageError.None;
        }, ct);

    public Task<StageError> ToggleAsync(int year, int id, bool on, CancellationToken ct = default) =>
        WithYearAsync(year, (ctx, rows) =>
        {
            if (rows.SingleOrDefault(c => c.Id == id) is not { } c) return Task.FromResult(StageError.NotFound);
            if (!on && c.IsActive && rows.Count(x => x.IsActive) == 1) return Task.FromResult(StageError.LastOn);
            c.IsActive = on;
            return Task.FromResult(StageError.None);
        }, ct);

    public Task<StageError> DeleteAsync(int year, int id, CancellationToken ct = default) =>
        WithYearAsync(year, async (ctx, rows) =>
        {
            if (rows.SingleOrDefault(c => c.Id == id) is not { } c) return StageError.NotFound;
            if (await IsLockedAsync(ctx, year, c, ct)) return StageError.Locked;
            if (c.IsActive && rows.Count(x => x.IsActive) == 1) return StageError.LastOn;
            ctx.FamilyMilestoneConfigs.Remove(c);
            return StageError.None;
        }, ct);

    // ── 안쪽 ──

    private async Task<StageError> WithYearAsync(int year, Func<AppDbContext, List<FamilyMilestoneConfig>, Task<StageError>> work, CancellationToken ct)
    {
        var (r, thisYear) = await RulesAsync(ct);
        if (!r.Features.FamilyTeam) return StageError.Disabled;
        if (year != thisYear && year != thisYear + 1) return StageError.BadYear;
        await using var ctx = db.Create();
        var rows = await ctx.FamilyMilestoneConfigs.Where(c => c.Year == year).ToListAsync(ct);
        var e = await work(ctx, rows);
        if (e == StageError.None) await ctx.SaveChangesAsync(ct);
        return e;
    }

    private static StageError Check(int points, string? type, string? title, string? emoji)
    {
        if (points < 1 || points > MaxPoints) return StageError.BadPoints;
        if (type is not (MilestoneTypes.Award or MilestoneTypes.Applause)) return StageError.BadType;
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > MaxTitle) return StageError.BadTitle;
        if (string.IsNullOrWhiteSpace(emoji) || emoji.Trim().Length > MaxEmoji) return StageError.BadEmoji;
        return StageError.None;
    }

    private FamilyMilestoneConfig New(int year, int points, string type, string title, string emoji, bool on, int order, DateTime now) => new()
    {
        TenantId = tenant.Current.Id, Year = year, PointThreshold = points, MilestoneType = type,
        DisplayTitle = title, DisplayEmoji = emoji, IsActive = on, SortOrder = order, CreatedAt = now,
    };

    private async Task<IReadOnlyList<FamilyMilestoneRule>> DefaultsAsync(int year, CancellationToken ct) =>
        (await rules.ForDateAsync(new DateOnly(year, 1, 1), ct)).FamilyMilestones.OrderBy(m => m.Points).ToList();

    private static string TypeOf(FamilyMilestoneKind k) => k == FamilyMilestoneKind.Award ? MilestoneTypes.Award : MilestoneTypes.Applause;

    private static async Task<List<StageRow>> RowsAsync(AppDbContext ctx, int year, CancellationToken ct)
    {
        var rows = await ctx.FamilyMilestoneConfigs.AsNoTracking().Where(c => c.Year == year).OrderBy(c => c.PointThreshold).ToListAsync(ct);
        var given = await GivenAsync(ctx, year, ct);
        var hidden = (await ctx.FamilyMilestonesHidden.Select(h => h.MilestoneConfigId).Distinct().ToListAsync(ct)).ToHashSet();
        return rows.Select(c =>
        {
            var n = given.GetValueOrDefault(c.PointThreshold);
            return new StageRow(c.Id, c.PointThreshold, c.MilestoneType, c.DisplayTitle, c.DisplayEmoji ?? "", c.IsActive, n, n > 0 || hidden.Contains(c.Id));
        }).ToList();
    }

    /// <summary>그해 점수마다 가족 시상을 받은 팀 수.</summary>
    private static async Task<Dictionary<int, int>> GivenAsync(AppDbContext ctx, int year, CancellationToken ct)
    {
        var from = new DateTime(year, 1, 1);
        var to = from.AddYears(1);
        return await ctx.TeamAwards.Where(a => a.Kind == TeamTypes.Family && a.AwardedAt >= from && a.AwardedAt < to)
            .GroupBy(a => a.Threshold).Select(g => new { g.Key, N = g.Select(a => a.AwardKey).Distinct().Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);
    }

    private static async Task<bool> IsLockedAsync(AppDbContext ctx, int year, FamilyMilestoneConfig c, CancellationToken ct)
    {
        var from = new DateTime(year, 1, 1);
        var to = from.AddYears(1);
        return await ctx.FamilyMilestonesHidden.AnyAsync(h => h.MilestoneConfigId == c.Id, ct)
            || await ctx.TeamAwards.AnyAsync(a => a.Kind == TeamTypes.Family && a.Threshold == c.PointThreshold && a.AwardedAt >= from && a.AwardedAt < to, ct);
    }
}
