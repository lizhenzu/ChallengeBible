using ChallengeBible.Domain.Points;
using ChallengeBible.Domain.Rules;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record ChildRow(int ParticipantId, string Name, string? Group, bool Active);

public sealed record DayLogLine(DateTime AtUtc, Mission Mission, string What, int Points);

/// <summary>
/// 보호자 화면(R-132, 1차로 당김): 이 부서에서 나와 연결된 자녀만 본다. 보기만 한다.
/// 연결 여부를 서버에서 먼저 확인한 뒤에만 자녀 기록을 읽는다.
/// </summary>
public sealed class GuardianService(IAppDb db, TenantContext tenant)
{
    public async Task<List<ChildRow>> ChildrenAsync(CancellationToken ct = default)
    {
        if (tenant.MembershipId is not int me || !tenant.Has(Roles.Guardian)) return [];
        await using var ctx = db.Create();
        return await (from l in ctx.GuardianLinks
                      where l.GuardianMembershipId == me
                      join p in ctx.Participants on l.ParticipantId equals p.Id
                      join m in ctx.Memberships on p.MembershipId equals m.Id
                      join a in ctx.Accounts on m.AccountId equals a.Id
                      orderby a.DisplayName
                      select new ChildRow(p.Id, a.DisplayName, m.GroupLabel, p.IsActive && m.Status == MembershipStatus.Approved))
            .ToListAsync(ct);
    }

    /// <summary>이 보호자와 연결된 참여자인가(R-132). 아니면 어떤 기록도 주지 않는다.</summary>
    public async Task<bool> IsMyChildAsync(int participantId, CancellationToken ct = default)
    {
        if (tenant.MembershipId is not int me || !tenant.Has(Roles.Guardian)) return false;
        await using var ctx = db.Create();
        return await ctx.GuardianLinks.AnyAsync(l => l.GuardianMembershipId == me && l.ParticipantId == participantId, ct);
    }

    /// <summary>그날 한 일: 수행 기록 + 퀴즈 오답(0점). 시각 순.</summary>
    public async Task<List<DayLogLine>> DayLogAsync(int participantId, DateOnly date, CancellationToken ct = default)
    {
        if (!await IsMyChildAsync(participantId, ct)) return [];
        await using var ctx = db.Create();
        var rules = RuleSet.Default;
        var acts = await ctx.Activities.Where(a => a.ParticipantId == participantId && a.MissionDate == date)
            .Select(a => new { a.PerformedUtc, a.MissionItem, a.Points }).ToListAsync(ct);
        var wrong = await ctx.QuizAttempts.Where(q => q.ParticipantId == participantId && q.MissionDate == date && !q.IsCorrect)
            .Select(q => new { q.AttemptedUtc, q.AttemptNo }).ToListAsync(ct);
        return acts.Select(a =>
            {
                var item = MissionItemCodes.Parse(a.MissionItem);
                var label = item == MissionItem.QuizCorrect ? "정답" : rules.Item(item).DisplayName;
                return new DayLogLine(a.PerformedUtc, item.Mission(), label, a.Points);
            })
            .Concat(wrong.Select(w => new DayLogLine(w.AttemptedUtc, Mission.Quiz, $"오답 · {w.AttemptNo}번째 도전", 0)))
            .OrderByDescending(l => l.AtUtc).ToList();
    }

    public async Task<int> StreakAsync(int participantId, CancellationToken ct = default)
    {
        if (!await IsMyChildAsync(participantId, ct)) return 0;
        await using var ctx = db.Create();
        var days = await ctx.PointLedgers.Where(l => l.ParticipantId == participantId).Select(l => l.MissionDate).Distinct().ToListAsync(ct);
        return PointMath.Streak(days);
    }
}
