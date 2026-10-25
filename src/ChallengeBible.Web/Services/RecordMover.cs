using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>
/// 한 참여자의 미션 기록을 다른 영역의 참여자에게 날짜 그대로 옮긴다(복사). 임시 부서 → 부서 승인(R-174)과 진급(0066, R-139)이 같이 쓴다.
/// 받는 쪽에 같은 날 같은 항목 · 같은 주 암송이 있으면 받는 쪽 것을 남기고 건너뛴다. 퀴즈 시도는 그날 시도가 없을 때만, 축하 단계는 (해 · 단계)가 없을 때만.
/// 시스템 연결(물리 설계 3-3)에서만 부른다. 보내는 쪽은 지우지 않는다 — 지우기는 부르는 쪽이 정한다.
/// </summary>
public static class RecordMover
{
    public static async Task<LobbyMove> CopyAsync(AppDbContext sys, int from, int fromP, int to, int toP, CancellationToken ct)
    {
        var acts = await sys.Activities.AsNoTracking().Where(a => a.TenantId == from && a.ParticipantId == fromP).OrderBy(a => a.Id).ToListAsync(ct);
        var have = await sys.Activities.AsNoTracking().Where(a => a.TenantId == to && a.ParticipantId == toP)
            .Select(a => new { a.MissionDate, a.MissionItem, a.ReciteStartDate }).ToListAsync(ct);
        var daily = have.Where(a => a.MissionItem != "Recite").Select(a => (a.MissionDate, a.MissionItem)).ToHashSet();
        var recite = have.Where(a => a.MissionItem == "Recite").Select(a => a.ReciteStartDate).ToHashSet();

        var map = new Dictionary<long, Activity>();
        var skipped = 0;
        foreach (var a in acts)
        {
            var clash = a.MissionItem == "Recite" ? !recite.Add(a.ReciteStartDate) : !daily.Add((a.MissionDate, a.MissionItem));
            if (clash) { skipped++; continue; }
            var n = new Activity
            {
                TenantId = to, ParticipantId = toP, MissionItem = a.MissionItem, MissionDate = a.MissionDate,
                DailyWordId = a.DailyWordId, DailyPrayerId = a.DailyPrayerId, DailyQuizId = a.DailyQuizId, WeeklyVerseId = a.WeeklyVerseId,
                ReciteStartDate = a.ReciteStartDate, Points = a.Points, PerformedUtc = a.PerformedUtc,
            };
            sys.Activities.Add(n);
            map[a.Id] = n;
        }
        await sys.SaveChangesAsync(ct);   // 새 기록 번호가 생긴다

        var ids = map.Keys.ToList();
        var ledger = await sys.PointLedgers.AsNoTracking().Where(l => l.TenantId == from && l.ParticipantId == fromP && ids.Contains(l.ActivityId)).ToListAsync(ct);
        foreach (var l in ledger)
            sys.PointLedgers.Add(new PointLedger
            {
                TenantId = to, ParticipantId = toP, MissionDate = l.MissionDate, MissionItem = l.MissionItem, Points = l.Points,
                ActivityId = map[l.ActivityId].Id, RecordedUtc = l.RecordedUtc,
            });

        // 퀴즈 시도: 그 부서에서 그날 시도가 없을 때만 그날 것을 통째로
        var quizDays = await sys.QuizAttempts.AsNoTracking().Where(q => q.TenantId == to && q.ParticipantId == toP).Select(q => q.MissionDate).Distinct().ToListAsync(ct);
        var attempts = (await sys.QuizAttempts.AsNoTracking().Where(q => q.TenantId == from && q.ParticipantId == fromP).ToListAsync(ct))
            .Where(q => !quizDays.Contains(q.MissionDate)).ToList();
        foreach (var q in attempts)
            sys.QuizAttempts.Add(new QuizAttempt
            {
                TenantId = to, ParticipantId = toP, MissionDate = q.MissionDate, AttemptNo = q.AttemptNo, DailyQuizId = q.DailyQuizId,
                SelectedChoiceId = q.SelectedChoiceId, AnswerText = q.AnswerText, IsCorrect = q.IsCorrect, AttemptedUtc = q.AttemptedUtc,
            });

        // 축하 단계: 그 부서에 없는 (해 · 단계)만 — 같은 축하를 두 번 보이지 않게
        var reached = (await sys.MilestonesReached.AsNoTracking().Where(m => m.TenantId == to && m.ParticipantId == toP).Select(m => new { m.Year, m.Threshold }).ToListAsync(ct))
            .Select(m => (m.Year, m.Threshold)).ToHashSet();
        var marks = (await sys.MilestonesReached.AsNoTracking().Where(m => m.TenantId == from && m.ParticipantId == fromP).ToListAsync(ct))
            .Where(m => !reached.Contains((m.Year, m.Threshold))).ToList();
        foreach (var m in marks)
            sys.MilestonesReached.Add(new MilestoneReached { TenantId = to, ParticipantId = toP, Year = m.Year, Threshold = m.Threshold, ReachedUtc = m.ReachedUtc });

        await sys.SaveChangesAsync(ct);
        return new LobbyMove(map.Count, ledger.Count, attempts.Count, marks.Count, skipped);
    }
}
