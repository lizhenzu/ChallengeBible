using System.Globalization;
using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ChallengeBible.Web.Services;

/// <summary>
/// 플랫폼 · 기본 규칙 저장(확정 시안: 플랫폼-기본규칙, ADR 0060 · 0032). 새 값은 늘 새 버전으로, 적용 시작은 내일 또는 다음 주 시작일.
/// 확정(사용자): 주 시작 요일은 1차에 바꾸지 않는다, 미션 항목 6개는 고정(켜기 · 이름 · 점수만), 가족 단계는 있는 줄만 고친다.
/// 쓰기는 시스템 연결 + 시작마다 플랫폼 관리자 확인. 저장 사실과 바뀐 점은 AuditLog(RuleSetSaved)에.
/// </summary>
public sealed class PlatformRuleService(ISystemDb systemDb, RuleService rules, TenantClock clock, TimeProvider time, IMemoryCache cache)
{
    public const string LogAction = "RuleSetSaved";
    private static readonly CultureInfo Ko = new("ko-KR");

    private DateOnly Today => clock.Today(PlatformHomeService.PlatformTimeZone);

    /// <summary>적용 시작으로 고를 수 있는 날: 내일, 다음 주 시작일.</summary>
    public (DateOnly Tomorrow, DateOnly NextWeek) StartChoices(RuleSet current)
    {
        var tomorrow = Today.AddDays(1);
        return (tomorrow, Week.Of(Today, current.WeekStart).Start.AddDays(7));
    }

    /// <summary>바꾼 점(사람이 읽는 말). 없으면 빈 목록.</summary>
    public static List<string> Diff(RuleSet a, RuleSet b)
    {
        var d = new List<string>();
        if (a.GraceDays != b.GraceDays) d.Add($"인정 기간 {a.GraceDays} → {b.GraceDays}일");
        if (a.QuizMaxAttempts != b.QuizMaxAttempts) d.Add($"퀴즈 {a.QuizMaxAttempts} → {b.QuizMaxAttempts}번");
        foreach (var x in b.Items)
        {
            var o = a.Item(x.Item);
            if (o.Enabled != x.Enabled) d.Add($"{o.DisplayName} {(x.Enabled ? "켬" : "끔")}");
            if (o.DisplayName != x.DisplayName) d.Add($"\"{o.DisplayName}\" → \"{x.DisplayName}\"");
            if (o.Points != x.Points) d.Add($"{x.DisplayName} {o.Points} → {x.Points}점");
        }
        if (!a.Milestones.SequenceEqual(b.Milestones))
            d.Add($"축하 단계 {string.Join(" · ", a.Milestones.Select(m => m.ToString("N0", Ko)))} → {string.Join(" · ", b.Milestones.Select(m => m.ToString("N0", Ko)))}");
        if (!a.FamilyMilestones.SequenceEqual(b.FamilyMilestones)) d.Add("가족 단계");
        if (a.Features.Ranking != b.Features.Ranking) d.Add($"순위 {(b.Features.Ranking ? "켬" : "끔")}");
        if (a.Features.FamilyTeam != b.Features.FamilyTeam) d.Add($"가족팀 {(b.Features.FamilyTeam ? "켬" : "끔")}");
        if (a.Features.TranslationCompare != b.Features.TranslationCompare) d.Add($"번역 대조 {(b.Features.TranslationCompare ? "켬" : "끔")}");
        return d;
    }

    /// <summary>들어온 값이 지금 규칙의 모양을 지키는지(고정 항목 · 주 시작 · 가족 단계 줄 수) + RuleSet.Validate.</summary>
    public static List<string> Check(RuleSet current, RuleSet next)
    {
        var e = next.Validate().ToList();
        if (next.WeekStart != current.WeekStart) e.Add("주 시작 요일은 1차에 바꾸지 않아요.");
        if (next.Items.Count != current.Items.Count || next.Items.Select(i => i.Item).Except(current.Items.Select(i => i.Item)).Any())
            e.Add("미션 항목은 더하거나 뺄 수 없어요.");
        if (next.Items.Any(i => string.IsNullOrWhiteSpace(i.DisplayName) || i.DisplayName.Length > 20)) e.Add("항목 이름은 1 ~ 20자예요.");
        if (next.Items.Any(i => i.Points > 10)) e.Add("항목 점수는 0 ~ 10점이에요.");
        if (next.QuizMaxAttempts > 3) e.Add("퀴즈 도전 횟수는 1 ~ 3번이에요.");
        if (next.Milestones.Any(m => m <= 0) || next.Milestones.Distinct().Count() != next.Milestones.Count) e.Add("축하 단계는 서로 다른 양수예요.");
        if (next.FamilyMilestones.Count != current.FamilyMilestones.Count) e.Add("가족 단계는 있는 줄만 고칠 수 있어요.");
        if (next.FamilyMilestones.Any(f => f.Points <= 0 || string.IsNullOrWhiteSpace(f.Message) || f.Message.Length > 50 || f.Emoji.Length > 8))
            e.Add("가족 단계 점수는 양수, 문구는 1 ~ 50자예요.");
        return e;
    }

    /// <summary>새 버전으로 저장. 바뀐 것이 없거나 틀리면 저장하지 않고 이유를 돌려준다.</summary>
    public async Task<(RuleVersion? Saved, List<string> Errors)> SaveAsync(int actor, RuleSet next, DateOnly effectiveFrom, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        if (!await ctx.Accounts.AnyAsync(a => a.Id == actor && a.IsPlatformAdmin && a.Status == "Active", ct))
            throw new UnauthorizedAccessException("플랫폼 관리자만 할 수 있어요.");

        var versions = await rules.VersionsAsync(ct);
        var latest = versions[^1];
        var errors = Check(latest.Rules, next);
        var (tomorrow, nextWeek) = StartChoices(latest.Rules);
        if (effectiveFrom != tomorrow && effectiveFrom != nextWeek) errors.Add("적용 시작은 내일이나 다음 주 시작일이에요.");
        var diff = Diff(latest.Rules, next);
        if (errors.Count == 0 && diff.Count == 0) errors.Add("바뀐 값이 없어요.");
        if (errors.Count > 0) return (null, errors);

        var no = latest.VersionNo + 1;
        var saved = next with { Version = no, Milestones = next.Milestones.Order().ToList() };
        var now = time.GetUtcNow().UtcDateTime;
        ctx.PlatformRuleSetVersions.Add(new PlatformRuleSetVersion { VersionNo = no, Values = RuleService.ToJson(saved), EffectiveFrom = effectiveFrom, ChangedUtc = now, ChangedByAccountId = actor });
        var detail = $"v{no} · {effectiveFrom.ToString("M월 d일", Ko)}부터 · {string.Join(", ", diff)}";
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = now, ActorAccountId = actor, Action = LogAction, Detail = detail.Length > 400 ? detail[..399] + "…" : detail });
        await ctx.SaveChangesAsync(ct);
        RuleService.Forget(cache);
        return (new RuleVersion(no, effectiveFrom, saved, now, actor), []);
    }

    /// <summary>이 규칙을 쓰는 부서 수(운영 중 · 성경말씀 켬). 1차는 모든 부서가 기본 규칙을 쓴다.</summary>
    public async Task<int> MissionTenantsAsync(int actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        return await ctx.Tenants.CountAsync(t => t.Status == TenantStatus.Active && t.MissionEnabled, ct);
    }

    public DateOnly TodayDate => Today;

    /// <summary>버전 기록용 계정 이름.</summary>
    public async Task<Dictionary<int, string>> NamesAsync(int actor, IEnumerable<int> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        await using var ctx = systemDb.Create(actor);
        return await ctx.Accounts.AsNoTracking().Where(a => list.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.DisplayName, ct);
    }
}
