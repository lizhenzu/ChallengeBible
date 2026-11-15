using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>남는 관리자 한 명: 이름 · 아이디 · 무슨 관리자인지(플랫폼 관리자 / ○○부 관리자).</summary>
public sealed record PeopleResetKeep(string UserName, string Name, IReadOnlyList<string> Roles);
public sealed record PeopleResetStatus(int Accounts, int People, int Classes, IReadOnlyList<PeopleResetKeep> Keep);
public sealed record PeopleResetResult(int Accounts, int People, int Teams, int Classes);

/// <summary>
/// 관리자만 남기기(확정 시안 플랫폼-관리자만남기기, 2026-11-15 사용자 "아이들 모두 삭제하고 반 편성, Superadmin 과 각 부서 관리자는 제외"):
/// 플랫폼 관리자(Identity SuperAdmin)와 부서 관리자(Identity Admin)만 남기고 **모든 부서의** 다른 계정 · 명부와 그 사람의 기록을 지운다(되돌릴 수 없음).
/// 같이 지움: 소속 · 참여 신청 · 반 배정 · 출석 · 미션 · 퀴즈 · 통독 · 선물(이력 · 쓴 점수) · 가족팀 구성원(다 빠진 팀은 팀 · 신청 · 숨김 · 시상까지) · 보호자 연결 ·
/// 진급 신청 · 설정 · 그 사람이 남긴 감사 로그, 그리고 **모든 부서의 반**(가짜 반, 2026-11-15 사용자 질문 1 → 1 — 새친구반은 출석 화면의 기본 반이라 비워서 남김)과 반 배정 전부.
/// 그대로: 부서 · 학기 · 새친구반 · 상품 · 설정 · 콘텐츠, 남는 관리자의 계정 · 소속 · 명부. 시스템 연결 · 한 트랜잭션.
/// </summary>
public sealed class PeopleResetService(ISystemDb systemDb, TimeProvider time)
{
    public const string Action = "PeopleReset";
    public const string ConfirmWord = "DELETE";

    /// <summary>남길 계정 Id: 플랫폼 관리자 · 부서 관리자 역할이 있는 계정.</summary>
    private static IQueryable<string> KeepIds(AppDbContext ctx) =>
        from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id
        where r.Name == IdentityRoles.SuperAdmin || r.Name == IdentityRoles.Admin select ur.UserId;

    public async Task<PeopleResetStatus> StatusAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        var keep = KeepIds(ctx);
        var accounts = await ctx.Users.CountAsync(u => !keep.Contains(u.Id), ct);
        var people = await ctx.People.CountAsync(p => p.UserId == null || !keep.Contains(p.UserId), ct);
        var kept = await ctx.Users.AsNoTracking().Where(u => keep.Contains(u.Id)).Select(u => new { u.Id, u.UserName, u.LastName, u.FirstName }).ToListAsync(ct);
        var ids = kept.Select(k => k.Id).ToList();
        var roles = await (from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id where ids.Contains(ur.UserId) select new { ur.UserId, r.Name }).ToListAsync(ct);
        var depts = await (from d in ctx.DeptRoles join t in ctx.Tenants on d.TenantId equals t.Id where ids.Contains(d.UserId!) select new { d.UserId, t.Name }).ToListAsync(ct);
        var classes = await ctx.Classes.CountAsync(c => !c.IsNewcomer, ct);
        return new PeopleResetStatus(accounts, people, classes, kept.Select(k => new PeopleResetKeep(k.UserName ?? "", AppUser.FullName(k.LastName, k.FirstName) is { Length: > 0 } n ? n : k.UserName ?? "",
                [.. roles.Any(r => r.UserId == k.Id && r.Name == IdentityRoles.SuperAdmin) ? ["플랫폼 관리자"] : Array.Empty<string>(),
                 .. roles.Any(r => r.UserId == k.Id && r.Name == IdentityRoles.Admin) ? depts.Where(d => d.UserId == k.Id).Select(d => $"{d.Name} 관리자") : []]))
            .OrderBy(k => k.Name).ToList());
    }

    private static async Task DeleteAsync<T>(AppDbContext ctx, IQueryable<T> q, CancellationToken ct) where T : class
    {
        if (ctx.Database.IsRelational()) await q.ExecuteDeleteAsync(ct);
        else { ctx.Set<T>().RemoveRange(await q.ToListAsync(ct)); await ctx.SaveChangesAsync(ct); }
    }

    public async Task<PeopleResetResult> ResetAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        await using var tx = ctx.Database.IsRelational() ? await ctx.Database.BeginTransactionAsync(ct) : null;
        var keep = await KeepIds(ctx).Distinct().ToListAsync(ct);
        var gone = await ctx.Users.Where(u => !keep.Contains(u.Id)).Select(u => new { u.Id, u.UserName }).ToListAsync(ct);
        var ids = gone.Select(u => u.Id).ToList();
        var names = gone.Select(u => u.UserName!).ToList();
        var persons = await ctx.People.Where(p => p.UserId == null || !keep.Contains(p.UserId)).Select(p => p.Id).ToListAsync(ct);

        // 가족팀: 지울 사람을 빼고, 구성원이 다 빠진 팀은 팀째 지운다
        await DeleteAsync(ctx, ctx.FamilyRequests.Where(r => names.Contains(r.TargetUserName) || names.Contains(r.FromUserName)), ct);
        await DeleteAsync(ctx, ctx.FamilyNotices.Where(n => names.Contains(n.UserName)), ct);
        await DeleteAsync(ctx, ctx.FamilyMembers.Where(m => names.Contains(m.UserName)), ct);
        var emptyTeams = await ctx.FamilyTeams.Where(t => !ctx.FamilyMembers.Any(m => m.TenantId == t.TenantId && m.FamilyTeamId == t.Id)).Select(t => new { t.TenantId, t.Id, t.AwardKey }).ToListAsync(ct);
        var teamIds = emptyTeams.Select(t => t.Id).ToList();
        var awardKeys = emptyTeams.Select(t => t.AwardKey).OfType<string>().ToList();

        var gifts = ctx.Gifts.Where(g => (g.UserName != null && names.Contains(g.UserName)) || (g.PersonId != null && persons.Contains(g.PersonId.Value))
            || (g.FamilyTeamId != null && teamIds.Contains(g.FamilyTeamId.Value))).Select(g => g.Id);
        await DeleteAsync(ctx, ctx.GiftLogs.Where(l => gifts.Contains(l.GiftId)), ct);
        await DeleteAsync(ctx, ctx.GiftPointUses.Where(u => gifts.Contains(u.GiftId) || names.Contains(u.UserName)), ct);
        await DeleteAsync(ctx, ctx.Gifts.Where(g => gifts.Contains(g.Id)), ct);

        await DeleteAsync(ctx, ctx.FamilyRequests.Where(r => teamIds.Contains(r.FamilyTeamId)), ct);
        await DeleteAsync(ctx, ctx.FamilyMilestonesHidden.Where(h => teamIds.Contains(h.FamilyTeamId)), ct);
        await DeleteAsync(ctx, ctx.TeamAwards.Where(a => awardKeys.Contains(a.AwardKey)), ct);
        await DeleteAsync(ctx, ctx.FamilyTeams.Where(t => teamIds.Contains(t.Id)), ct);

        await DeleteAsync(ctx, ctx.GuardianLinks.Where(g => names.Contains(g.GuardianUserName) || names.Contains(g.StudentUserName)), ct);
        await DeleteAsync(ctx, ctx.PromotionRequests.Where(p => names.Contains(p.UserName)), ct);
        await DeleteAsync(ctx, ctx.QuizAttempts.Where(q => names.Contains(q.UserName)), ct);
        await DeleteAsync(ctx, ctx.MissionActivities.Where(m => names.Contains(m.UserName)), ct);
        await DeleteAsync(ctx, ctx.ReadingVerses.Where(r => names.Contains(r.UserName)), ct);
        await DeleteAsync(ctx, ctx.ReadingChapterReads.Where(r => names.Contains(r.UserName)), ct);
        await DeleteAsync(ctx, ctx.ReadingChapterProgresses.Where(r => names.Contains(r.UserName)), ct);
        await DeleteAsync(ctx, ctx.ReadingBookmarks.Where(r => names.Contains(r.UserName)), ct);
        await DeleteAsync(ctx, ctx.ReadingRounds.Where(r => names.Contains(r.UserName)), ct);

        await DeleteAsync(ctx, ctx.AttendanceChecks.Where(a => persons.Contains(a.PersonId)), ct);
        await DeleteAsync(ctx, ctx.ClassMembers, ct);   // 반을 새로 짜므로 반 배정은 모두(남는 관리자의 담임 자리 포함)
        var classCount = await ctx.Classes.CountAsync(c => !c.IsNewcomer, ct);
        await DeleteAsync(ctx, ctx.Classes.Where(c => !c.IsNewcomer), ct);
        await DeleteAsync(ctx, ctx.SignupApplications.Where(s => ids.Contains(s.UserId) || persons.Contains(s.PersonId)), ct);
        await DeleteAsync(ctx, ctx.People.Where(p => persons.Contains(p.Id)), ct);

        await DeleteAsync(ctx, ctx.DeptRoles.Where(d => d.UserId != null && ids.Contains(d.UserId)), ct);
        await DeleteAsync(ctx, ctx.UserSettings.Where(s => names.Contains(s.UserName)), ct);
        await DeleteAsync(ctx, ctx.AuditLogs.Where(a => a.ActorUserName != null && names.Contains(a.ActorUserName)), ct);
        if (ctx.Database.IsRelational())   // 앱이 쓰지 않는 Identity 표(모델에 없음)
            foreach (var table in new[] { "[Identity].[UserTokens]", "[Identity].[UserLogins]", "[Identity].[UserClaims]" })
                await ctx.Database.ExecuteSqlRawAsync($"DELETE FROM {table} WHERE UserId NOT IN (SELECT ur.UserId FROM [Identity].[UserRoles] ur JOIN [Identity].[Role] r ON r.Id = ur.RoleId WHERE r.Name IN ('SuperAdmin', 'Admin'))", ct);
        await DeleteAsync(ctx, ctx.UserRoles.Where(r => ids.Contains(r.UserId)), ct);
        await DeleteAsync(ctx, ctx.Users.Where(u => ids.Contains(u.Id)), ct);

        var result = new PeopleResetResult(ids.Count, persons.Count, teamIds.Count, classCount);
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = Action, Detail = $"accounts={result.Accounts};people={result.People};teams={result.Teams};classes={result.Classes}" });
        await ctx.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return result;
    }
}
