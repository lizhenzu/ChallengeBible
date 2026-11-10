using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record DetailDayLine(DateTime AtUtc, Mission Mission, string What, int Points);
public sealed record DetailReading(int RoundNo, int ReadChapters, double Percent, int Completed, byte? LastBook, short? LastChapter, DateTime? LastUtc);
public sealed record MemberDetail(int MembershipId, string UserName, string Name, string Email, string Status, DateTime? DecidedUtc,
    IReadOnlyList<string> Roles, IReadOnlyList<string> Guardians, string? RoleCode,
    int SeasonPoints, int Streak, int ActiveDaysThisMonth, int DaysThisMonth, DetailDayLine? LastMission,
    IReadOnlyList<DayScore> Month, DetailReading? Reading, string? ResetBlocked);

/// <summary>
/// 관리 · 참여자 상세(확정 시안: 관리-참여자상세). 이 부서의 승인 · 정지 소속만 보인다(전역 필터 + RLS).
/// 계정 관리(0035 · 0029): 임시 비밀번호 · 성경 읽음 지우기는 계정 전역이라 시스템 연결로 한다.
/// 플랫폼 관리자 계정과 다른 부서의 관리자 계정은 막는다(플랫폼이 한다).
/// </summary>
public sealed class MemberDetailService(IAppDb db, ISystemDb systemDb, TenantContext tenant, TenantClock clock, TimeProvider time,
    MissionService missions, RuleService rules)
{
    public DateOnly Today => clock.Today(tenant.Current.TimeZoneId);

    private static Task<DeptRoleUserAssoc?> FindAsync(AppDbContext ctx, int deptRoleId, CancellationToken ct) =>
        ctx.DeptRoles.AsNoTracking().SingleOrDefaultAsync(d => d.Id == deptRoleId, ct);

    public async Task<MemberDetail?> GetAsync(int deptRoleId, DateOnly month, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var m = await FindAsync(ctx, deptRoleId, ct);
        if (m is null) return null;
        var a = await ctx.Users.AsNoTracking().SingleAsync(x => x.Id == m.UserId, ct);
        var name = a.UserName!;
        var guardianRows = await (from l in ctx.GuardianLinks
                                  where l.StudentUserName == name
                                  join ga in ctx.Users on l.GuardianUserName equals ga.UserName
                                  select new { ga.LastName, ga.FirstName }).ToListAsync(ct);
        var guardians = guardianRows.Select(g => AppUser.FullName(g.LastName, g.FirstName)).OrderBy(g => g).ToList();

        var today = Today;
        int season = 0, streak = 0, active = 0, days = 0;
        DetailDayLine? last = null;
        IReadOnlyList<DayScore> monthScores = [];
        {
            var p = name;
            season = (await missions.GetDayAsync(p, today, ct)).SeasonPoints;
            streak = await missions.StreakAsync(p, today, ct);
            monthScores = await missions.MonthAsync(p, month.Year, month.Month, ct);
            // "이달 한 날"은 보고 있는 달과 상관없이 이번 달(오늘까지) 기준
            var thisMonth = month.Year == today.Year && month.Month == today.Month ? monthScores : await missions.MonthAsync(p, today.Year, today.Month, ct);
            (active, days) = (thisMonth.Count(d => d.Total > 0), today.Day);
            var lastAct = await ctx.MissionActivities.AsNoTracking().Where(x => x.UserName == p).OrderByDescending(x => x.PerformedAt)
                .Select(x => new { x.PerformedAt, x.MissionItem, x.Points, x.MissionDate }).FirstOrDefaultAsync(ct);
            if (lastAct is not null)
            {
                var r = await rules.ForDateAsync(lastAct.MissionDate, ct);
                var item = MissionItemCodes.Parse(lastAct.MissionItem);
                last = new DetailDayLine(lastAct.PerformedAt, item.Mission(), r.Item(item).DisplayName, lastAct.Points);
            }
        }

        DetailReading? reading = null;
        if (tenant.Current.ReadingEnabled)
        {
            // 통독 기록은 계정 범위라 앱 필터가 "본인만" — 관리자 화면에서만 끄고 이 계정으로 직접 거른다(DB 는 fn_ReadingRead 가 관리자 · 교사에게 허용)
            var rounds = await ctx.ReadingRounds.IgnoreQueryFilters().AsNoTracking().Where(r => r.UserName == name)
                .Select(r => new { r.RoundNo, r.CompletedAt, r.ClosedAt }).ToListAsync(ct);
            if (rounds.Count > 0)
            {
                var roundNo = rounds.Where(r => r.CompletedAt == null && r.ClosedAt == null).Select(r => r.RoundNo).DefaultIfEmpty(rounds.Max(r => r.RoundNo)).Max();
                var reads = await ctx.ReadingChapterReads.IgnoreQueryFilters().AsNoTracking().Where(c => c.UserName == name)
                    .Select(c => new { c.RoundNo, c.BookId, c.Chapter, c.ReadAt }).ToListAsync(ct);
                var n = reads.Count(r => r.RoundNo == roundNo);
                var lr = reads.OrderByDescending(r => r.ReadAt).FirstOrDefault();
                reading = new DetailReading(roundNo, n, ReadingSummary.Percent(n, ReadingService.TotalChapters), rounds.Count(r => r.CompletedAt != null),
                    lr?.BookId, lr?.Chapter, lr?.ReadAt);
            }
        }

        var isAdmin = await (from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id
                             where ur.UserId == a.Id && r.Name == IdentityRoles.Admin select ur).AnyAsync(ct);
        return new MemberDetail(m.Id, name, a.DisplayName, a.Email ?? "", MembershipStatus.Approved, m.InsertDT, UserRole.ScreenRoles(m.RoleCode, isAdmin).ToList(), guardians, m.RoleCode,
            season, streak, active, days, last, monthScores, reading, await BlockedAsync(a, ct));
    }

    /// <summary>그날 한 일(수행 + 퀴즈 오답 0점), 늦은 시각 먼저.</summary>
    public async Task<List<DetailDayLine>> DayLogAsync(string userName, DateOnly date, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var r = await rules.ForDateAsync(date, ct);
        var acts = await ctx.MissionActivities.AsNoTracking().Where(x => x.UserName == userName && x.MissionDate == date)
            .Select(x => new { x.PerformedAt, x.MissionItem, x.Points }).ToListAsync(ct);
        var wrong = await ctx.QuizAttempts.AsNoTracking().Where(q => q.UserName == userName && q.MissionDate == date && !q.IsCorrect)
            .Select(q => new { q.AttemptedAt, q.AttemptNo }).ToListAsync(ct);
        return acts.Select(x =>
            {
                var item = MissionItemCodes.Parse(x.MissionItem);
                return new DetailDayLine(x.PerformedAt, item.Mission(), item == MissionItem.Recite ? "이번 주 암송 완료" : r.Item(item).DisplayName, x.Points);
            })
            .Concat(wrong.Select(w => new DetailDayLine(w.AttemptedAt, Mission.Quiz, $"오답 · {w.AttemptNo}번째 도전", 0)))
            .OrderByDescending(l => l.AtUtc).ToList();
    }

    /// <summary>활동이 있었던 최근 날짜들(최대 n일).</summary>
    public async Task<List<DateOnly>> RecentActiveDaysAsync(string userName, int n, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return await ctx.MissionActivities.Where(x => x.UserName == userName).Select(x => x.MissionDate).Distinct()
            .OrderByDescending(d => d).Take(n).ToListAsync(ct);
    }

    /// <summary>부서 관리자가 이 계정을 초기화할 수 없는 이유(ADR 0035 결정 4). 할 수 있으면 null. 한 사람 한 부서라 다른 부서 확인은 없다(0090).</summary>
    private async Task<string?> BlockedAsync(AppUser user, CancellationToken ct)
    {
        await using var sys = systemDb.Create(user.UserName!);
        return await AccountService.IsPlatformAdminAsync(sys, user.Id, ct) ? "플랫폼 관리자 계정이에요. 이 계정은 본인이 바꿔요." : null;
    }

    /// <summary>임시 비밀번호(0035). 이 부서 승인 · 정지 소속만, 막힌 계정이면 (null, 이유).</summary>
    public async Task<(string? Temp, string? Error)> ResetPasswordAsync(string actor, int deptRoleId, CancellationToken ct = default)
    {
        AppUser? target;
        await using (var ctx = db.Create())
        {
            var m = await FindAsync(ctx, deptRoleId, ct);
            if (m is null) return (null, "이 부서 소속이 아니에요.");
            target = await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == m.UserId, ct);
        }
        if (await BlockedAsync(target, ct) is { } why) return (null, why);
        var userName = target.UserName!;
        await using var sys = systemDb.Create(actor);
        var a = await sys.Users.SingleAsync(x => x.UserName == userName, ct);
        var temp = PlatformAccountService.ApplyTemporaryPassword(a);
        await PlatformAccountService.MarkMustChangeAsync(sys, userName, ct);
        sys.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = PlatformAccountService.ResetPasswordAction,
            TenantId = tenant.Current.Id, Detail = $"account={userName} {a.DisplayName}({a.Email})" });
        await sys.SaveChangesAsync(ct);
        return (temp, null);
    }

    /// <summary>성경 읽음 · 책갈피 지우기(0029). 이름을 똑같이 써야 한다. 막힌 계정이면 이유.</summary>
    public async Task<string?> ResetMarksAsync(string actor, int deptRoleId, string? confirmName, CancellationToken ct = default)
    {
        AppUser? target;
        await using (var ctx = db.Create())
        {
            var m = await FindAsync(ctx, deptRoleId, ct);
            if (m is null) return "이 부서 소속이 아니에요.";
            target = await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == m.UserId, ct);
        }
        if (await BlockedAsync(target, ct) is { } why) return why;
        var userName = target.UserName!;
        await using var sys = systemDb.Create(actor);
        var a = await sys.Users.SingleAsync(x => x.UserName == userName, ct);
        if ((confirmName ?? "").Trim() != a.DisplayName) return "이름이 맞지 않아요. 똑같이 써 주세요.";
        var (marks, books) = await PlatformAccountService.ClearBibleMarksAsync(sys, userName, ct);
        sys.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = PlatformAccountService.ResetMarksAction,
            TenantId = tenant.Current.Id, Detail = $"account={userName} {a.DisplayName}({a.Email}) 읽음 {marks} · 책갈피 {books}" });
        await sys.SaveChangesAsync(ct);
        return null;
    }
}
