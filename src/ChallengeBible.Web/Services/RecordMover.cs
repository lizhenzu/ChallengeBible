using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>
/// 한 사람의 미션 기록을 다른 부서로 날짜 그대로 옮긴다. 임시 부서 → 부서 승인(R-174)과 진급(0066, R-139)이 같이 쓴다.
/// 받는 쪽에 같은 날 같은 항목 · 같은 주 암송이 있으면 받는 쪽 것을 남긴다. 퀴즈 시도는 그날 시도가 없을 때만 옮긴다.
/// 한 사람은 한 부서에만 속하므로(0090) 옮긴 뒤 보내는 쪽 기록은 지운다. 시스템 연결에서만 부른다.
/// </summary>
public static class RecordMover
{
    public static async Task<LobbyMove> MoveAsync(AppDbContext sys, string userName, int from, int to, CancellationToken ct)
    {
        var acts = await sys.MissionActivities.Where(a => a.TenantId == from && a.UserName == userName).OrderBy(a => a.ActivityId).ToListAsync(ct);
        var have = await sys.MissionActivities.AsNoTracking().Where(a => a.TenantId == to && a.UserName == userName)
            .Select(a => new { a.MissionDate, a.MissionItem, a.ReciteWeekStart }).ToListAsync(ct);
        var daily = have.Where(a => a.MissionItem != MissionItems.Recite).Select(a => (a.MissionDate, a.MissionItem)).ToHashSet();
        var recite = have.Where(a => a.MissionItem == MissionItems.Recite).Select(a => a.ReciteWeekStart).ToHashSet();

        var moved = 0;
        var points = 0;
        var skipped = 0;
        foreach (var a in acts)
        {
            var clash = a.MissionItem == MissionItems.Recite ? !recite.Add(a.ReciteWeekStart) : !daily.Add((a.MissionDate, a.MissionItem));
            if (clash) { skipped++; continue; }
            sys.MissionActivities.Add(new MissionActivity
            {
                TenantId = to, UserName = userName, MissionItem = a.MissionItem, MissionDate = a.MissionDate,
                Points = a.Points, ReciteWeekStart = a.ReciteWeekStart, PerformedAt = a.PerformedAt,
            });
            moved++;
            points += a.Points;
        }
        sys.MissionActivities.RemoveRange(acts);

        var quizDays = await sys.QuizAttempts.AsNoTracking().Where(q => q.TenantId == to && q.UserName == userName).Select(q => q.MissionDate).Distinct().ToListAsync(ct);
        var attempts = await sys.QuizAttempts.Where(q => q.TenantId == from && q.UserName == userName).ToListAsync(ct);
        var copied = attempts.Where(q => !quizDays.Contains(q.MissionDate)).ToList();
        foreach (var q in copied)
            sys.QuizAttempts.Add(new QuizAttempt
            {
                TenantId = to, UserName = userName, MissionDate = q.MissionDate, QuestionId = q.QuestionId, AttemptNo = q.AttemptNo,
                AnswerText = q.AnswerText, CorrectAnswerText = q.CorrectAnswerText, IsCorrect = q.IsCorrect, AttemptedAt = q.AttemptedAt,
            });
        sys.QuizAttempts.RemoveRange(attempts);
        await sys.SaveChangesAsync(ct);
        return new LobbyMove(moved, points, copied.Count, skipped);
    }
}
