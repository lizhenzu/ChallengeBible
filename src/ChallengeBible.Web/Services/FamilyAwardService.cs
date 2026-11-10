using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Seasons;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>선물 줄 차례 한 장: 팀 · 넘은 "축하 + 보상" 단계. Crossed = 팀 점수가 처음 그 단계를 넘은 날.</summary>
public sealed record AwardDue(int TeamId, string Team, int Points, int Stage, string Emoji, string Message, DateOnly? Crossed, IReadOnlyList<(string Name, string Relation)> Members);
/// <summary>준 기록 한 줄. Day = 부서 시간대의 기록 날짜.</summary>
public sealed record AwardGiven(int AwardId, string Team, int Stage, DateOnly Day);
public sealed record AwardBoard(bool Enabled, int Year, IReadOnlyList<AwardDue> Due, IReadOnlyList<AwardGiven> Given);

public enum AwardError { None, Disabled, NotFound, NotAward, NotReached, Already, NoKey }

/// <summary>
/// 가족팀 시상 기록(R-220, ADR 0092): `DM2_TeamAward`(Kind = FAMILY). 이 부서 연결로만 읽고 쓴다(전역 필터 + RLS).
/// 기록이 있는 팀은 시상 잠금(0090, FamilyTeamService.HasAwardRecordAsync)이 된다.
/// </summary>
public sealed class FamilyAwardService(IAppDb db, TenantContext tenant, TenantClock clock, RuleService rules, FamilyTeamService teams)
{
    public const string GiveAction = "TeamAward", UndoAction = "TeamAwardUndo";

    public static string Message(AwardError e) => e switch
    {
        AwardError.NotFound => "그 팀이나 기록을 찾을 수 없어요. 다시 열어 주세요.",
        AwardError.NotAward => "선물을 주는 “축하 + 보상” 단계가 아니에요.",
        AwardError.NotReached => "아직 그 단계를 넘지 않은 팀이에요.",
        AwardError.Already => "이미 기록한 선물이에요.",
        AwardError.NoKey => "팀 이름이 없어 기록할 수 없어요. 가족팀 화면에서 팀 이름을 정해 주세요.",
        _ => "가족팀 기능이 꺼져 있어요.",
    };

    /// <summary>시상 기록의 팀 키: 처음 이름(AwardKey), 없으면 지금 이름 — 잠금 판단(HasAwardRecordAsync)과 같다.</summary>
    internal static string KeyOf(FamilyTeam t) => t.AwardKey ?? t.Name;

    private async Task<(RuleSet Rules, DateOnly Today)> RulesAsync(CancellationToken ct)
    {
        var today = clock.Today(tenant.Current.TimeZoneId);
        return (await rules.ForDateAsync(today, ct), today);
    }

    public async Task<AwardBoard> GetAsync(CancellationToken ct = default)
    {
        var (r, today) = await RulesAsync(ct);
        if (!r.Features.FamilyTeam) return new AwardBoard(false, today.Year, [], []);
        await using var ctx = db.Create();
        var due = await DueAsync(ctx, r, today, ct);
        var from = new DateTime(today.Year, 1, 1);
        var awards = await ctx.TeamAwards.AsNoTracking().Where(a => a.Kind == TeamTypes.Family && a.AwardedAt >= from && a.AwardedAt < from.AddYears(1))
            .OrderByDescending(a => a.AwardedAt).ThenByDescending(a => a.AwardId).ToListAsync(ct);
        var names = await TeamNamesAsync(ctx, ct);
        var given = awards.Select(a => new AwardGiven(a.AwardId, names.GetValueOrDefault(a.AwardKey) ?? a.AwardKey, a.Threshold,
            TenantClock.ToTenantDate(new DateTimeOffset(DateTime.SpecifyKind(a.AwardedAt, DateTimeKind.Utc)), tenant.Current.TimeZoneId))).ToList();
        return new AwardBoard(true, today.Year, due, given);
    }

    /// <summary>선물 줬어요: 그 팀이 올해 켜진 AWARD 단계를 넘었고 기록이 없을 때만.</summary>
    public async Task<AwardError> GiveAsync(int teamId, int stage, string actor, CancellationToken ct = default)
    {
        var (r, today) = await RulesAsync(ct);
        if (!r.Features.FamilyTeam) return AwardError.Disabled;
        await using var ctx = db.Create();
        var due = await DueAsync(ctx, r, today, ct, includeGiven: true);
        var team = await ctx.FamilyTeams.SingleOrDefaultAsync(t => t.Id == teamId && t.IsActive && t.TeamType == TeamTypes.Family, ct);
        if (team is null) return AwardError.NotFound;
        var key = KeyOf(team);
        if (string.IsNullOrWhiteSpace(key)) return AwardError.NoKey;
        var stages = await teams.StagesAsync(ctx, r, today.Year, ct);
        if (!stages.Any(s => s.Points == stage && s.Kind == FamilyMilestoneKind.Award)) return AwardError.NotAward;
        if (await ctx.TeamAwards.AnyAsync(a => a.Kind == TeamTypes.Family && a.AwardKey == key && a.Threshold == stage, ct)) return AwardError.Already;
        if (!due.Any(d => d.TeamId == teamId && d.Stage == stage)) return AwardError.NotReached;

        var now = clock.UtcNow.UtcDateTime;
        ctx.TeamAwards.Add(new TeamAward { TenantId = tenant.Current.Id, Kind = TeamTypes.Family, AwardKey = key, Threshold = stage, AwardedAt = now });
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = now, ActorUserName = actor, Action = GiveAction, TenantId = tenant.Current.Id, Detail = $"{key} · {stage}점" });
        await ctx.SaveChangesAsync(ct);
        return AwardError.None;
    }

    /// <summary>기록 취소: 그 줄을 지운다(잠금도 풀린다).</summary>
    public async Task<AwardError> UndoAsync(int awardId, string actor, CancellationToken ct = default)
    {
        var (r, _) = await RulesAsync(ct);
        if (!r.Features.FamilyTeam) return AwardError.Disabled;
        await using var ctx = db.Create();
        var a = await ctx.TeamAwards.SingleOrDefaultAsync(x => x.AwardId == awardId && x.Kind == TeamTypes.Family, ct);
        if (a is null) return AwardError.NotFound;
        ctx.TeamAwards.Remove(a);
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = clock.UtcNow.UtcDateTime, ActorUserName = actor, Action = UndoAction, TenantId = tenant.Current.Id, Detail = $"{a.AwardKey} · {a.Threshold}점" });
        await ctx.SaveChangesAsync(ct);
        return AwardError.None;
    }

    // ── 안쪽 ──

    /// <summary>선물 줄 차례(넘은 날 순). includeGiven = 이미 기록한 것도 넣는다(GiveAsync 의 "넘었는가" 판단용).</summary>
    private async Task<List<AwardDue>> DueAsync(AppDbContext ctx, RuleSet r, DateOnly today, CancellationToken ct, bool includeGiven = false)
    {
        var awardStages = (await teams.StagesAsync(ctx, r, today.Year, ct)).Where(s => s.Kind == FamilyMilestoneKind.Award).OrderBy(s => s.Points).ToList();
        if (awardStages.Count == 0) return [];
        var (loaded, season, _, _) = await teams.LoadTeamsAsync(ctx, today, ct);
        var family = loaded.Where(t => t.Team.TeamType == TeamTypes.Family).ToList();
        var done = (await ctx.TeamAwards.Where(a => a.Kind == TeamTypes.Family).Select(a => new { a.AwardKey, a.Threshold }).ToListAsync(ct))
            .Select(a => (a.AwardKey, a.Threshold)).ToHashSet();

        var start = await ctx.TenantSeasons.Where(s => s.Year == today.Year).Select(s => (DateOnly?)s.StartDate).SingleOrDefaultAsync(ct);
        var (from, to) = (start is { } d ? Season.Create(today.Year, d) : Season.DefaultFor(today.Year)).ScoringRange(today);
        var ids = family.SelectMany(t => t.Members.Select(m => m.Pid)).Distinct().ToList();
        var daily = from > to || ids.Count == 0 ? [] : await ctx.MissionActivities.Where(l => ids.Contains(l.UserName) && l.MissionDate >= from && l.MissionDate <= to)
            .GroupBy(l => new { l.UserName, l.MissionDate }).Select(g => new { g.Key.UserName, g.Key.MissionDate, P = g.Sum(x => x.Points) }).ToListAsync(ct);

        var list = new List<AwardDue>();
        foreach (var t in family)
        {
            var total = t.Members.Sum(m => season.GetValueOrDefault(m.Pid));
            var key = KeyOf(t.Team);
            var pids = t.Members.Select(m => m.Pid).ToHashSet();
            var byDay = daily.Where(x => pids.Contains(x.UserName)).GroupBy(x => x.MissionDate).OrderBy(g => g.Key).Select(g => (Day: g.Key, P: g.Sum(x => x.P))).ToList();
            var name = FamilyTeamService.DisplayName(t.Team.Name, t.Members.Select(m => (m.Name, m.M.Relation)).ToList());
            foreach (var s in awardStages.Where(s => s.Points <= total))
            {
                if (!includeGiven && done.Contains((key, s.Points))) continue;
                list.Add(new AwardDue(t.Team.Id, name, total, s.Points, s.Emoji, s.Message, CrossedOn(byDay, s.Points),
                    t.Members.Select(m => (m.Name, m.M.Relation)).ToList()));
            }
        }
        return list.OrderBy(x => x.Crossed ?? DateOnly.MaxValue).ThenBy(x => x.Stage).ThenBy(x => x.TeamId).ToList();
    }

    private static DateOnly? CrossedOn(List<(DateOnly Day, int P)> byDay, int stage)
    {
        var sum = 0;
        foreach (var (day, p) in byDay) { sum += p; if (sum >= stage) return day; }
        return null;
    }

    /// <summary>시상 키 → 지금 팀 이름.</summary>
    private static async Task<Dictionary<string, string>> TeamNamesAsync(AppDbContext ctx, CancellationToken ct)
    {
        var map = new Dictionary<string, string>();
        foreach (var t in await ctx.FamilyTeams.AsNoTracking().Where(t => t.TeamType == TeamTypes.Family).ToListAsync(ct))
        {
            var key = KeyOf(t);
            if (!string.IsNullOrWhiteSpace(key)) map.TryAdd(key, string.IsNullOrWhiteSpace(t.Name) ? key : t.Name);
        }
        return map;
    }
}
