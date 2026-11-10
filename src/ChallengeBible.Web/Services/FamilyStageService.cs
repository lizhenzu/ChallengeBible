using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>선물 단계 한 줄(R-219). Given = 그해 이 단계 선물을 받은 팀 수(가족 · 친구).</summary>
public sealed record StageRow(int Points, bool On, int Amount, int Given);

/// <summary>Own = 그해 우리 부서가 저장한 기준이 있다. 없으면 처음 값(모두 켬 · 기본 금액).</summary>
public sealed record StageBoard(bool Enabled, int ThisYear, int Year, bool Own, int DefaultAmount, IReadOnlyList<StageRow> Rows)
{
    public int OnCount => Rows.Count(r => r.On);
    public int OnTotal => Rows.Where(r => r.On).Sum(r => r.Amount);
}

public enum StageError { None, Disabled, BadYear, BadAmount, BadForm }

/// <summary>
/// 부서 시상 기준(R-219, ADR 0091 → 0093 으로 고침): 1,000점 단위 10단계마다 선물을 줄지와 금액 한도. `DM2_FamilyMilestoneConfig` 의 그해 줄.
/// 이 부서 연결로만 읽고 쓴다(전역 필터 + RLS). 단위 · 단계 수 · 처음 금액은 규칙(RuleSet)에서 읽는다.
/// </summary>
public sealed class FamilyStageService(IAppDb db, TenantContext tenant, TenantClock clock, RuleService rules)
{
    public const int MaxAmount = 1_000_000;

    public static string Message(StageError e) => e switch
    {
        StageError.BadYear => "올해와 내년만 정할 수 있어요.",
        StageError.BadAmount => "고칠 곳이 있어요. 빨간 칸을 확인해 주세요.",
        StageError.BadForm => "화면을 새로 고친 뒤 다시 저장해 주세요.",
        _ => "가족팀 기능이 꺼져 있어요.",
    };

    /// <summary>금액 칸 읽기: 숫자와 쉼표만, 0 ~ 100만 원. 안 되면 null.</summary>
    public static int? ParseAmount(string? s)
    {
        var t = (s ?? "").Replace(",", "").Replace("원", "").Trim();
        return int.TryParse(t, System.Globalization.NumberStyles.None, null, out var n) && n is >= 0 and <= MaxAmount ? n : null;
    }

    private async Task<(RuleSet Rules, int ThisYear)> RulesAsync(CancellationToken ct)
    {
        var today = clock.Today(tenant.Current.TimeZoneId);
        return (await rules.ForDateAsync(today, ct), today.Year);
    }

    public async Task<StageBoard> GetAsync(int? year, CancellationToken ct = default)
    {
        var (r, thisYear) = await RulesAsync(ct);
        var y = year is { } v && (v == thisYear || v == thisYear + 1) ? v : thisYear;
        if (!r.Features.FamilyTeam) return new StageBoard(false, thisYear, y, false, r.FamilyAwardDefaultAmount, []);
        await using var ctx = db.Create();
        var rr = await rules.ForDateAsync(new DateOnly(y, 1, 1), ct);
        var (own, steps) = await FamilyTeamService.StageSettingsAsync(ctx, rr, y, ct);
        var given = await GivenAsync(ctx, y, ct);
        return new StageBoard(true, thisYear, y, own, rr.FamilyAwardDefaultAmount,
            steps.Select(s => new StageRow(s.Points, s.On, s.Amount, given.GetValueOrDefault(s.Points))).ToList());
    }

    /// <summary>
    /// 10단계를 한 번에 저장한다. on[i] · amount[i] 는 i 번째 단계(1,000점부터). 끈 단계의 금액은 비어 있어도 되고, 그때는 처음 금액을 남긴다.
    /// 잘못된 칸이 있으면 아무것도 저장하지 않고 그 칸 번호들을 돌려준다.
    /// </summary>
    public async Task<(StageError Error, IReadOnlyList<int> Bad)> SaveAsync(int year, IReadOnlyList<bool> on, IReadOnlyList<string?> amount, CancellationToken ct = default)
    {
        var (r, thisYear) = await RulesAsync(ct);
        if (!r.Features.FamilyTeam) return (StageError.Disabled, []);
        if (year != thisYear && year != thisYear + 1) return (StageError.BadYear, []);
        var rr = await rules.ForDateAsync(new DateOnly(year, 1, 1), ct);
        if (on.Count != rr.FamilyAwardSteps || amount.Count != rr.FamilyAwardSteps) return (StageError.BadForm, []);

        var values = new int[rr.FamilyAwardSteps];
        var bad = new List<int>();
        for (var i = 0; i < values.Length; i++)
        {
            var n = ParseAmount(amount[i]);
            if (n is { } ok) values[i] = ok;
            else if (on[i]) bad.Add(i);
            else values[i] = rr.FamilyAwardDefaultAmount;
        }
        if (bad.Count > 0) return (StageError.BadAmount, bad);

        await using var ctx = db.Create();
        var rows = await ctx.FamilyMilestoneConfigs.Where(c => c.Year == year).ToListAsync(ct);
        var now = clock.UtcNow.UtcDateTime;
        for (var i = 0; i < values.Length; i++)
        {
            var p = (i + 1) * rr.FamilyAwardStep;
            var c = rows.FirstOrDefault(x => x.PointThreshold == p);
            if (c is null)
                ctx.FamilyMilestoneConfigs.Add(c = new FamilyMilestoneConfig { TenantId = tenant.Current.Id, Year = year, PointThreshold = p, MilestoneType = MilestoneTypes.Award,
                    DisplayTitle = FamilyTeamService.StageMessage(p), CreatedAt = now });
            c.MilestoneType = MilestoneTypes.Award;
            c.DisplayTitle = FamilyTeamService.StageMessage(p);
            c.DisplayEmoji = FamilyTeamService.StageEmoji;
            c.SortOrder = i + 1;
            c.IsActive = on[i];
            c.GiftAmount = values[i];
        }
        await ctx.SaveChangesAsync(ct);
        return (StageError.None, []);
    }

    /// <summary>그해 점수마다 선물을 받은 팀 수(가족 · 친구).</summary>
    private static async Task<Dictionary<int, int>> GivenAsync(AppDbContext ctx, int year, CancellationToken ct)
    {
        var from = new DateTime(year, 1, 1);
        var to = from.AddYears(1);
        return await ctx.TeamAwards.Where(a => a.AwardedAt >= from && a.AwardedAt < to)
            .GroupBy(a => a.Threshold).Select(g => new { g.Key, N = g.Select(a => a.Kind + ":" + a.AwardKey).Distinct().Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);
    }
}
