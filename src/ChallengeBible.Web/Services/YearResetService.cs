using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>지울 것의 건수: 미션 기록(퀴즈 풀이 포함) · 출석 · 선물 · 시상 기록(지난 기록 포함) · 감사 로그 · 계정(관리자 제외) · 명부 · 팀 · 통독 회차.</summary>
public sealed record YearResetCounts(int Missions, int Attendance, int Gifts, int Awards, int Audits, int Accounts = 0, int People = 0, int Teams = 0, int Reading = 0)
{
    public string Detail => $"missions={Missions};attend={Attendance};gifts={Gifts};awards={Awards};audits={Audits};accounts={Accounts};people={People};teams={Teams};reading={Reading}";
}
public sealed record YearResetStatus(int Year, YearResetCounts Counts, DateTime? LastUtc, string? LastBy, IReadOnlyList<(DateTime AtUtc, string Actor, string? Detail)> History);

/// <summary>
/// 새해 초기화(ADR 0094 정한 것 17 → 2026-11-16 사용자 "완전히 ground zero", R-296): 플랫폼 관리자가 버튼으로 모든 부서를 한 번에 지운다(되돌릴 수 없음).
/// 지우는 것: 미션 · 퀴즈 기록, 출석(새친구 포함), 모든 선물(완독 포함)과 그 이력 · 쓴 점수, 시상 기록 · 지난 시상 기록, 가족팀 · 친구팀(구성 · 신청 · 알림),
/// 학부모 ↔ 학생 연결, 성경통독 기록(회차 · 장 · 절 · 책갈피), 진급 신청, 검색 기록, 감사 로그, 초기화 시각 기록(GiftYearClose),
/// **관리자(Admin · SuperAdmin 역할)를 뺀 모든 계정**과 그 소속 · 가입 신청 · 설정, 명부(관리자 줄 제외) · 반 배정(관리자 줄 제외).
/// 남기는 것: 관리자 계정과 그 명부 줄 · 반 배정, 부서 · 반 이름, 상품 · 설정 · 콘텐츠 · 규칙.
/// 초기화 시각을 남기지 않으므로 초기화 뒤에는 처음 상태와 똑같이 계산한다(이전처럼 "초기화 뒤 기록만" 거르지 않음).
/// 내년(2027년 말)부터 이력을 남길지는 그때 다시 정한다(사용자 2026-11-16).
/// </summary>
public sealed class YearResetService(ISystemDb systemDb, TimeProvider time)
{
    public const string Action = "GiftYearReset";
    public const string ConfirmWord = "RESET";
    public const string Zone = "Asia/Seoul";

    private int Year => TenantClock.ToTenantDate(time.GetUtcNow(), Zone).Year;

    /// <summary>남길 계정: Identity 역할 Admin · SuperAdmin(CLAUDE.md "관리자는 절대 지우지 않는다").</summary>
    private static IQueryable<string> KeepIds(AppDbContext ctx) =>
        from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id
        where r.Name == IdentityRoles.Admin || r.Name == IdentityRoles.SuperAdmin
        select ur.UserId;

    private static IQueryable<AppUser> GoneUsers(AppDbContext ctx) { var keep = KeepIds(ctx); return ctx.Users.Where(u => !keep.Contains(u.Id)); }
    private static IQueryable<Person> GonePeople(AppDbContext ctx) { var keep = KeepIds(ctx); return ctx.People.Where(p => p.UserId == null || !keep.Contains(p.UserId)); }

    private static async Task<YearResetCounts> CountAsync(AppDbContext ctx, CancellationToken ct) => new(
        await ctx.MissionActivities.CountAsync(ct) + await ctx.QuizAttempts.CountAsync(ct),
        await ctx.AttendanceChecks.CountAsync(ct),
        await ctx.Gifts.CountAsync(ct),
        await ctx.TeamAwards.CountAsync(ct) + await ctx.TeamAwardArchives.CountAsync(ct),
        await ctx.AuditLogs.CountAsync(ct),
        await GoneUsers(ctx).CountAsync(ct),
        await GonePeople(ctx).CountAsync(ct),
        await ctx.FamilyTeams.CountAsync(ct),
        await ctx.ReadingRounds.CountAsync(ct));

    public async Task<YearResetStatus> StatusAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        var history = (await ctx.AuditLogs.AsNoTracking().Where(a => a.Action == Action).OrderByDescending(a => a.Id).Take(10)
            .Select(a => new { a.OccurredUtc, a.ActorUserName, a.Detail }).ToListAsync(ct)).Select(a => (a.OccurredUtc, a.ActorUserName, a.Detail)).ToList();
        var last = history.Count > 0 ? history[0] : default;
        return new YearResetStatus(Year, await CountAsync(ctx, ct), history.Count > 0 ? last.OccurredUtc : null, history.Count > 0 ? last.ActorUserName : null, history);
    }

    /// <summary>지우기: DB 에서는 한 번에(ExecuteDelete), 시험용 메모리 DB 는 읽어서 지운다.</summary>
    private static async Task DeleteAsync<T>(AppDbContext ctx, IQueryable<T> q, CancellationToken ct) where T : class
    {
        if (ctx.Database.IsRelational()) await q.ExecuteDeleteAsync(ct);
        else { ctx.Set<T>().RemoveRange(await q.ToListAsync(ct)); await ctx.SaveChangesAsync(ct); }
    }

    public async Task<YearResetCounts> ResetAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        await using var tx = ctx.Database.IsRelational() ? await ctx.Database.BeginTransactionAsync(ct) : null;
        var now = time.GetUtcNow().UtcDateTime;
        var counts = await CountAsync(ctx, ct);
        var keep = KeepIds(ctx);
        var goneNames = GoneUsers(ctx).Select(u => u.UserName);

        // 기록: 선물 · 시상 · 미션 · 출석 · 팀 · 보호 관계 · 통독 · 진급 신청 · 검색 · 감사 로그 · 초기화 시각
        await DeleteAsync(ctx, ctx.GiftLogs, ct);
        await DeleteAsync(ctx, ctx.GiftPointUses, ct);
        await DeleteAsync(ctx, ctx.Gifts, ct);
        await DeleteAsync(ctx, ctx.TeamAwards, ct);
        await DeleteAsync(ctx, ctx.TeamAwardArchives, ct);
        await DeleteAsync(ctx, ctx.QuizAttempts, ct);
        await DeleteAsync(ctx, ctx.MissionActivities, ct);
        await DeleteAsync(ctx, ctx.AttendanceChecks, ct);
        await DeleteAsync(ctx, ctx.FamilyRequests, ct);
        await DeleteAsync(ctx, ctx.FamilyNotices, ct);
        await DeleteAsync(ctx, ctx.FamilyMilestonesHidden, ct);
        await DeleteAsync(ctx, ctx.FamilyMembers, ct);
        await DeleteAsync(ctx, ctx.FamilyTeams, ct);
        await DeleteAsync(ctx, ctx.GuardianLinks, ct);
        await DeleteAsync(ctx, ctx.ReadingVerses, ct);
        await DeleteAsync(ctx, ctx.ReadingChapterReads, ct);
        await DeleteAsync(ctx, ctx.ReadingChapterProgresses, ct);
        await DeleteAsync(ctx, ctx.ReadingBookmarks, ct);
        await DeleteAsync(ctx, ctx.ReadingRounds, ct);
        await DeleteAsync(ctx, ctx.PromotionRequests, ct);
        await DeleteAsync(ctx, ctx.SearchLogs, ct);
        await DeleteAsync(ctx, ctx.AuditLogs, ct);
        await DeleteAsync(ctx, ctx.GiftYearCloses, ct);

        // 명부 · 반 배정(관리자 줄은 남김)
        await DeleteAsync(ctx, ctx.ClassMembers.Where(m => ctx.People.Any(p => p.TenantId == m.TenantId && p.Id == m.PersonId && (p.UserId == null || !keep.Contains(p.UserId)))), ct);
        await DeleteAsync(ctx, GonePeople(ctx), ct);

        // 계정(관리자는 남김): 소속 · 가입 신청 · 설정 → Identity
        await DeleteAsync(ctx, ctx.DeptRoles.Where(d => d.UserId == null || !keep.Contains(d.UserId)), ct);
        await DeleteAsync(ctx, ctx.SignupApplications.Where(s => !keep.Contains(s.UserId)), ct);
        await DeleteAsync(ctx, ctx.UserSettings.Where(s => goneNames.Contains(s.UserName)), ct);
        await DeleteAsync(ctx, ctx.CopyVerses.Where(c => goneNames.Contains(c.UserName)), ct);   // 성경필사(ADR 0099): 지우는 계정의 것만 — 남는 관리자 필사는 새해에도 그대로(사용자 질문 8 → 1)
        if (ctx.Database.IsRelational())
        {
            const string keepSql = "SELECT ur.UserId FROM [Identity].[UserRoles] ur JOIN [Identity].[Role] r ON r.Id = ur.RoleId WHERE r.NormalizedName IN (N'ADMIN', N'SUPERADMIN')";
            foreach (var t in new[] { "UserTokens", "UserLogins", "UserClaims" })
                await ctx.Database.ExecuteSqlRawAsync($"DELETE FROM [Identity].[{t}] WHERE UserId NOT IN ({keepSql})", ct);
        }
        await DeleteAsync(ctx, ctx.UserRoles.Where(r => !keep.Contains(r.UserId)), ct);
        await DeleteAsync(ctx, ctx.Users.Where(u => !keep.Contains(u.Id)), ct);

        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = now, ActorUserName = actor, Action = Action, Detail = counts.Detail });   // 다 지운 뒤 이번 초기화 한 줄만
        await ctx.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return counts;
    }

    /// <summary>그해 마지막 초기화 시각(UTC). 2026-11-16 부터 초기화가 이 기록까지 지우므로 보통 null — 개인 점수 · 팀 선물이 이 시각 뒤만 본다.</summary>
    public static Task<DateTime?> LastResetAsync(AppDbContext ctx, int year, CancellationToken ct) =>
        ctx.GiftYearCloses.AsNoTracking().Where(c => c.Year == year).Select(c => (DateTime?)c.ClosedUtc).SingleOrDefaultAsync(ct);

    /// <summary>가장 최근 초기화 날(부서 날짜, 해와 상관없이). 새친구 · 전도 · 생일 선물은 이날 뒤에 시작된 것만 다시 생긴다. 없으면 null.</summary>
    public static async Task<DateOnly?> LastResetDateAsync(AppDbContext ctx, string timeZoneId, CancellationToken ct)
    {
        var at = await ctx.GiftYearCloses.AsNoTracking().OrderByDescending(c => c.ClosedUtc).Select(c => (DateTime?)c.ClosedUtc).FirstOrDefaultAsync(ct);
        return at is { } u ? TenantClock.ToTenantDate(new DateTimeOffset(DateTime.SpecifyKind(u, DateTimeKind.Utc)), timeZoneId) : null;
    }
}
