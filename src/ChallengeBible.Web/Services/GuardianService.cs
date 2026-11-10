using ChallengeBible.Domain.Points;
using ChallengeBible.Domain.Rules;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record ChildRow(string UserName, string Name, string? Group, bool Active);

public sealed record DayLogLine(DateTime AtUtc, Mission Mission, string What, int Points);

/// <summary>
/// 보호자 화면(R-132, 1차로 당김): 이 부서에서 나와 연결된 자녀만 본다. 보기만 한다.
/// 연결 여부를 서버에서 먼저 확인한 뒤에만 자녀 기록을 읽는다.
/// </summary>
public sealed class GuardianService(IAppDb db, ChallengeBible.Infrastructure.Tenancy.RequestScope scope, TenantContext tenant, RuleService ruleService)
{
    public async Task<List<ChildRow>> ChildrenAsync(CancellationToken ct = default)
    {
        if (scope.UserName is not { } me || !tenant.Has(Roles.Guardian)) return [];
        await using var ctx = db.Create();
        return (await (from l in ctx.GuardianLinks
                       where l.GuardianUserName == me
                       join a in ctx.Users on l.StudentUserName equals a.UserName
                       select new { l.StudentUserName, a.LastName, a.FirstName }).ToListAsync(ct))
            .Select(x => new ChildRow(x.StudentUserName, AppUser.FullName(x.LastName, x.FirstName), null, true)).OrderBy(x => x.Name).ToList();
    }

    /// <summary>이 보호자와 연결된 참여자인가(R-132). 아니면 어떤 기록도 주지 않는다.</summary>
    public async Task<bool> IsMyChildAsync(string userName, CancellationToken ct = default)
    {
        if (scope.UserName is not { } me || !tenant.Has(Roles.Guardian)) return false;
        await using var ctx = db.Create();
        return await ctx.GuardianLinks.AnyAsync(l => l.GuardianUserName == me && l.StudentUserName == userName, ct);
    }

    /// <summary>그날 한 일: 수행 기록 + 퀴즈 오답(0점). 시각 순.</summary>
    public async Task<List<DayLogLine>> DayLogAsync(string userName, DateOnly date, CancellationToken ct = default)
    {
        if (!await IsMyChildAsync(userName, ct)) return [];
        await using var ctx = db.Create();
        var rules = await ruleService.ForDateAsync(date, ct);   // 그날 적용된 항목 이름(ADR 0060)
        var acts = await ctx.MissionActivities.Where(a => a.UserName == userName && a.MissionDate == date)
            .Select(a => new { a.PerformedAt, a.MissionItem, a.Points }).ToListAsync(ct);
        var wrong = await ctx.QuizAttempts.Where(q => q.UserName == userName && q.MissionDate == date && !q.IsCorrect)
            .Select(q => new { q.AttemptedAt, q.AttemptNo }).ToListAsync(ct);
        return acts.Select(a =>
            {
                var item = MissionItemCodes.Parse(a.MissionItem);
                var label = item == MissionItem.QuizCorrect ? "정답" : rules.Item(item).DisplayName;
                return new DayLogLine(a.PerformedAt, item.Mission(), label, a.Points);
            })
            .Concat(wrong.Select(w => new DayLogLine(w.AttemptedAt, Mission.Quiz, $"오답 · {w.AttemptNo}번째 도전", 0)))
            .OrderByDescending(l => l.AtUtc).ToList();
    }

    public async Task<int> StreakAsync(string userName, CancellationToken ct = default)
    {
        if (!await IsMyChildAsync(userName, ct)) return 0;
        await using var ctx = db.Create();
        var days = await ctx.MissionActivities.Where(l => l.UserName == userName).Select(l => l.MissionDate).Distinct().ToListAsync(ct);
        return PointMath.Streak(days);
    }
}
