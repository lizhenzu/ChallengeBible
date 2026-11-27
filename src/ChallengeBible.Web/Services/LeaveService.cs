using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public enum LeaveError { None, BadPassword, BadWord, IsAdmin, NoDept, Already, NotFound }

/// <summary>할 일 화면의 탈퇴 신청 한 줄.</summary>
public sealed record LeaveRow(int Id, int TenantId, string Dept, string Name, string Role, DateTime RequestedUtc);

/// <summary>
/// 탈퇴(ADR 0119, R-342 · R-343). 본인이 비밀번호 + "탈퇴" 글자로 신청하고, 승인 전까지는 취소할 수 있고 평소처럼 쓴다.
/// 그 부서 관리자나 플랫폼 관리자 중 한 사람이 승인하면 계정과 그 사람의 기록(미션 · 퀴즈 · 통독 · 필사 · 기도 · 받은 선물 · 쪽지 · 사진 · 출석 · 가족팀)을 모두 지운다.
/// 명부 줄은 남기되 연락처 · 생일을 비우고 탈퇴한 날을 적는다. 그 사람이 올린 사진첩 사진 · 주보 · 사기 기록은 남기고 올린 사람만 비운다("탈퇴한 사람").
/// 관리자(플랫폼 · 부서) 계정은 신청도 승인도 안 된다(CLAUDE.md R-315).
/// </summary>
public sealed class LeaveService(ISystemDb systemDb, PhotoService photos, MessageService msgs, TimeProvider time)
{
    public const string ConfirmWord = "탈퇴";
    public const string Action = "Leave";
    private static readonly PasswordHasher<AppUser> Hasher = new();

    private static IQueryable<string> AdminIds(AppDbContext ctx) =>
        from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id
        where r.Name == IdentityRoles.Admin || r.Name == IdentityRoles.SuperAdmin
        select ur.UserId;

    public async Task<bool> IsAdminAsync(string userName, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(userName);
        var admins = AdminIds(ctx);
        return await ctx.Users.AnyAsync(u => u.UserName == userName && admins.Contains(u.Id), ct);
    }

    /// <summary>내 기다리는 탈퇴 신청(없으면 null).</summary>
    public async Task<DateTime?> PendingAsync(string userName, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(userName);
        return await ctx.LeaveRequests.IgnoreQueryFilters().Where(r => r.UserName == userName).Select(r => (DateTime?)r.RequestedUtc).FirstOrDefaultAsync(ct);
    }

    public async Task<LeaveError> RequestAsync(string userName, string? password, string? word, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(userName);
        var me = await ctx.Users.SingleOrDefaultAsync(u => u.UserName == userName, ct);
        if (me is null) return LeaveError.NotFound;
        if (await AdminIds(ctx).ContainsAsync(me.Id, ct)) return LeaveError.IsAdmin;
        if (Hasher.VerifyHashedPassword(me, me.PasswordHash ?? "", password ?? "") == PasswordVerificationResult.Failed) return LeaveError.BadPassword;
        if ((word ?? "").Trim() != ConfirmWord) return LeaveError.BadWord;
        if (await ctx.LeaveRequests.IgnoreQueryFilters().AnyAsync(r => r.UserName == userName, ct)) return LeaveError.Already;
        // 신청은 내 부서로(정식 부서가 없으면 임시 부서) — 그 부서 관리자 · 플랫폼 관리자의 할 일에 뜬다
        var dept = await (from d in ctx.DeptRoles join t in ctx.Tenants on d.TenantId equals t.Id where d.UserId == me.Id orderby t.IsLobby, d.Id select new { d.TenantId, d.RoleCode, t.Name }).FirstOrDefaultAsync(ct)
            ?? await ctx.Tenants.Where(t => t.IsLobby).Select(t => new { TenantId = t.Id, RoleCode = (string?)null, t.Name }).FirstOrDefaultAsync(ct);
        if (dept is null) return LeaveError.NoDept;
        var name = AppUser.FullName(me.LastName, me.FirstName);
        ctx.LeaveRequests.Add(new LeaveRequest { TenantId = dept.TenantId, UserId = me.Id, UserName = userName, FullName = name, RoleCode = dept.RoleCode, RequestedUtc = time.GetUtcNow().UtcDateTime });
        await ctx.SaveChangesAsync(ct);
        await msgs.NotifyDeptAsync(userName, dept.TenantId, "탈퇴 신청", $"{name}님이 탈퇴를 신청했어요 · {RosterLinkService.ShortDept(dept.Name)}\n오늘 할 일에서 승인하면 계정과 모든 기록이 지워져요.", ct);
        return LeaveError.None;
    }

    /// <summary>
    /// 가장 중 탈퇴(R-348, 2026-11-20 사용자 "비밀번호 · 확인 글자 · 승인 없이"): 가장한 플랫폼 관리자가 그 계정을 바로 탈퇴 처리한다.
    /// 가장한 사람이 플랫폼 관리자가 아니거나 대상이 관리자 계정이면 하지 않는다(R-315). 감사 로그에 가장한 관리자 이름으로 남는다.
    /// </summary>
    public async Task<LeaveError> ForceAsync(string impersonator, string userName, CancellationToken ct = default)
    {
        string actor;
        int requestId;
        await using (var ctx = systemDb.Create("impersonate"))
        {
            var imp = await ctx.Users.SingleOrDefaultAsync(u => u.UserName == impersonator, ct);
            var supers = from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id where r.Name == IdentityRoles.SuperAdmin select ur.UserId;
            if (imp?.UserName is null || !await supers.ContainsAsync(imp.Id, ct)) return LeaveError.NotFound;
            actor = imp.UserName;
            var me = await ctx.Users.SingleOrDefaultAsync(u => u.UserName == userName, ct);
            if (me is null) return LeaveError.NotFound;
            if (await AdminIds(ctx).ContainsAsync(me.Id, ct)) return LeaveError.IsAdmin;
            var req = await ctx.LeaveRequests.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.UserName == userName, ct);
            if (req is null)
            {
                var dept = await (from d in ctx.DeptRoles join t in ctx.Tenants on d.TenantId equals t.Id where d.UserId == me.Id orderby t.IsLobby, d.Id select new { d.TenantId, d.RoleCode }).FirstOrDefaultAsync(ct)
                    ?? await ctx.Tenants.Where(t => t.IsLobby).Select(t => new { TenantId = t.Id, RoleCode = (string?)null }).FirstOrDefaultAsync(ct);
                if (dept is null) return LeaveError.NoDept;
                req = new LeaveRequest { TenantId = dept.TenantId, UserId = me.Id, UserName = userName, FullName = AppUser.FullName(me.LastName, me.FirstName), RoleCode = dept.RoleCode, RequestedUtc = time.GetUtcNow().UtcDateTime };
                ctx.LeaveRequests.Add(req);
                await ctx.SaveChangesAsync(ct);
            }
            requestId = req.Id;
        }
        return await ApproveAsync(actor, requestId, null, ct);
    }

    public async Task<bool> CancelAsync(string userName, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(userName);
        var r = await ctx.LeaveRequests.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.UserName == userName, ct);
        if (r is null) return false;
        ctx.LeaveRequests.Remove(r);
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>기다리는 탈퇴 신청: 부서 관리자는 그 부서 것만(tenantId), 플랫폼 관리자는 모두(null).</summary>
    public async Task<IReadOnlyList<LeaveRow>> ListAsync(string actor, int? tenantId, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        var q = from r in ctx.LeaveRequests.IgnoreQueryFilters() join t in ctx.Tenants on r.TenantId equals t.Id select new { r, t.Name };
        if (tenantId is int tid) q = q.Where(x => x.r.TenantId == tid);
        var rows = await q.AsNoTracking().OrderBy(x => x.r.RequestedUtc).ToListAsync(ct);
        return rows.Select(x => new LeaveRow(x.r.Id, x.r.TenantId, RosterLinkService.ShortDept(x.Name), x.r.FullName,
            UserRole.LabelOf(UserRole.KindOf(x.r.RoleCode)), x.r.RequestedUtc)).ToList();
    }

    /// <summary>기다리는 탈퇴 수(☰ 메뉴 숫자): 부서 관리자는 그 부서, 플랫폼 관리자는 모두(null).</summary>
    public async Task<int> CountAsync(string actor, int? tenantId, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        return await ctx.LeaveRequests.IgnoreQueryFilters().CountAsync(r => tenantId == null || r.TenantId == tenantId, ct);
    }

    private static async Task<LeaveRequest?> FindAsync(AppDbContext ctx, int id, int? tenantId, CancellationToken ct) =>
        await ctx.LeaveRequests.IgnoreQueryFilters().SingleOrDefaultAsync(r => r.Id == id && (tenantId == null || r.TenantId == tenantId), ct);

    /// <summary>반려: 신청을 지우고 본인에게 쪽지.</summary>
    public async Task<bool> RejectAsync(string actor, int id, int? tenantId, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        var r = await FindAsync(ctx, id, tenantId, ct);
        if (r is null) return false;
        ctx.LeaveRequests.Remove(r);
        await ctx.SaveChangesAsync(ct);
        await msgs.SendSystemAsync(r.UserName, "탈퇴 반려", "탈퇴 신청이 반려됐어요. 계정과 기록은 그대로예요. 궁금하면 부서 관리자에게 물어봐 주세요.", ct);
        return true;
    }

    private static async Task DeleteAsync<T>(AppDbContext ctx, IQueryable<T> q, CancellationToken ct) where T : class
    {
        if (ctx.Database.IsRelational()) await q.ExecuteDeleteAsync(ct);
        else { ctx.Set<T>().RemoveRange(await q.ToListAsync(ct)); await ctx.SaveChangesAsync(ct); }
    }

    /// <summary>승인: 계정 · 기록을 모두 지우고 명부 줄에 탈퇴한 날만 남긴다. 관리자 계정이면 하지 않는다.</summary>
    public async Task<LeaveError> ApproveAsync(string actor, int id, int? tenantId, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        var r = await FindAsync(ctx, id, tenantId, ct);
        if (r is null) return LeaveError.NotFound;
        if (await AdminIds(ctx).ContainsAsync(r.UserId, ct)) return LeaveError.IsAdmin;   // R-315: 관리자는 절대 지우지 않는다
        var (un, uid) = (r.UserName, r.UserId);
        var tz = await ctx.Tenants.Where(t => t.Id == r.TenantId).Select(t => t.TimeZoneId).SingleAsync(ct);
        var today = TenantClock.ToTenantDate(time.GetUtcNow(), tz);
        var photo = await ctx.UserSettings.Where(s => s.UserName == un).Select(s => s.PhotoPath).FirstOrDefaultAsync(ct);

        await using var tx = ctx.Database.IsRelational() ? await ctx.Database.BeginTransactionAsync(ct) : null;
        var people = await ctx.People.IgnoreQueryFilters().Where(p => p.UserId == uid).ToListAsync(ct);
        var pids = people.Select(p => p.Id).ToList();
        var roles = await ctx.DeptRoles.Where(d => d.UserId == uid).ToListAsync(ct);

        // 기록
        await DeleteAsync(ctx, ctx.QuizAttempts.IgnoreQueryFilters().Where(x => x.UserName == un), ct);
        await DeleteAsync(ctx, ctx.MissionActivities.IgnoreQueryFilters().Where(x => x.UserName == un), ct);
        await DeleteAsync(ctx, ctx.ReadingVerses.IgnoreQueryFilters().Where(x => x.UserName == un), ct);
        await DeleteAsync(ctx, ctx.ReadingChapterReads.IgnoreQueryFilters().Where(x => x.UserName == un), ct);
        await DeleteAsync(ctx, ctx.ReadingChapterProgresses.IgnoreQueryFilters().Where(x => x.UserName == un), ct);
        await DeleteAsync(ctx, ctx.ReadingBookmarks.IgnoreQueryFilters().Where(x => x.UserName == un), ct);
        await DeleteAsync(ctx, ctx.ReadingRounds.IgnoreQueryFilters().Where(x => x.UserName == un), ct);
        await DeleteAsync(ctx, ctx.CopyVerses.IgnoreQueryFilters().Where(x => x.UserName == un), ct);
        await DeleteAsync(ctx, ctx.Prayer365Logs.IgnoreQueryFilters().Where(x => x.UserName == un), ct);
        await DeleteAsync(ctx, ctx.PromotionRequests.IgnoreQueryFilters().Where(x => x.UserName == un), ct);
        // 받은 선물(이 사람 것 · 명부 줄 것)과 그 이력. 다른 사람 선물을 산 기록은 남기고 산 사람만 비운다
        var gifts = ctx.Gifts.IgnoreQueryFilters().Where(g => g.UserName == un || (g.PersonId != null && pids.Contains(g.PersonId.Value)));
        var giftIds = await gifts.Select(g => new { g.TenantId, g.Id }).ToListAsync(ct);
        foreach (var g in giftIds) await DeleteAsync(ctx, ctx.GiftLogs.IgnoreQueryFilters().Where(l => l.TenantId == g.TenantId && l.GiftId == g.Id), ct);
        await DeleteAsync(ctx, gifts, ct);
        await DeleteAsync(ctx, ctx.GiftPointUses.IgnoreQueryFilters().Where(x => x.UserName == un), ct);
        foreach (var l in await ctx.GiftLogs.IgnoreQueryFilters().Where(l => l.ActorUserName == un).ToListAsync(ct)) l.ActorUserName = null;
        // 가족팀 · 보호자 연결
        await DeleteAsync(ctx, ctx.FamilyMembers.IgnoreQueryFilters().Where(x => x.UserName == un), ct);
        await DeleteAsync(ctx, ctx.FamilyRequests.IgnoreQueryFilters().Where(x => x.TargetUserName == un || x.FromUserName == un), ct);
        await DeleteAsync(ctx, ctx.FamilyNotices.IgnoreQueryFilters().Where(x => x.UserName == un), ct);
        foreach (var t in await ctx.FamilyTeams.IgnoreQueryFilters().Where(t => t.CreatorUserName == un).ToListAsync(ct)) t.CreatorUserName = null;
        await DeleteAsync(ctx, ctx.GuardianLinks.IgnoreQueryFilters().Where(x => x.GuardianUserName == un || x.StudentUserName == un), ct);
        // 출석 · 반
        await DeleteAsync(ctx, ctx.AttendanceChecks.IgnoreQueryFilters().Where(a => pids.Contains(a.PersonId)), ct);
        await DeleteAsync(ctx, ctx.ClassMembers.IgnoreQueryFilters().Where(m => pids.Contains(m.PersonId)), ct);
        // 쪽지(주고받은 것 · 읽음)
        var msgIds = ctx.Messages.IgnoreQueryFilters().Where(m => m.SenderUserName == un || m.RecipientUserName == un).Select(m => m.Id);
        await DeleteAsync(ctx, ctx.MessageReceipts.IgnoreQueryFilters().Where(x => x.UserName == un || msgIds.Contains(x.MessageId)), ct);
        await DeleteAsync(ctx, ctx.MessageTargets.IgnoreQueryFilters().Where(x => msgIds.Contains(x.MessageId)), ct);
        await DeleteAsync(ctx, ctx.Messages.IgnoreQueryFilters().Where(m => m.SenderUserName == un || m.RecipientUserName == un), ct);
        // 남기는 것: 올린 사진 · 주보 — 올린 사람만 비운다("탈퇴한 사람")
        foreach (var p in await ctx.Photos.IgnoreQueryFilters().Where(p => p.UploadedByUserName == un).ToListAsync(ct)) p.UploadedByUserName = null;
        foreach (var a in await ctx.Albums.IgnoreQueryFilters().Where(a => a.CreatedByUserName == un || a.UpdatedByUserName == un).ToListAsync(ct))
        { if (a.CreatedByUserName == un) a.CreatedByUserName = null; if (a.UpdatedByUserName == un) a.UpdatedByUserName = null; }
        foreach (var b in await ctx.Bulletins.IgnoreQueryFilters().Where(b => b.CreatedByUserName == un || b.UpdatedByUserName == un).ToListAsync(ct))
        { if (b.CreatedByUserName == un) b.CreatedByUserName = null; if (b.UpdatedByUserName == un) b.UpdatedByUserName = null; }
        // 명부: 줄은 남기고 계정 연결 · 연락처 · 생일을 비우고 탈퇴한 날
        foreach (var p in people)
        {
            p.UserId = null;
            p.Phone1 = p.Phone2 = null;
            p.BirthMonth = p.BirthDay = null;
            p.LeftOn = today;
            p.RoleCode ??= roles.FirstOrDefault(d => d.TenantId == p.TenantId)?.RoleCode;
        }
        await ctx.SaveChangesAsync(ct);
        // 계정
        await DeleteAsync(ctx, ctx.MemberTags.IgnoreQueryFilters().Where(x => x.UserId == uid), ct);
        await DeleteAsync(ctx, ctx.DeptRoles.Where(x => x.UserId == uid), ct);
        await DeleteAsync(ctx, ctx.SignupApplications.IgnoreQueryFilters().Where(x => x.UserId == uid), ct);
        await DeleteAsync(ctx, ctx.UserSettings.Where(x => x.UserName == un), ct);
        await DeleteAsync(ctx, ctx.LeaveRequests.IgnoreQueryFilters().Where(x => x.UserId == uid), ct);
        if (ctx.Database.IsRelational())
            foreach (var t in new[] { "UserTokens", "UserLogins", "UserClaims" })
                await ctx.Database.ExecuteSqlRawAsync($"DELETE FROM [Identity].[{t}] WHERE UserId = {{0}}", [uid], ct);
        await DeleteAsync(ctx, ctx.UserRoles.Where(x => x.UserId == uid), ct);
        await DeleteAsync(ctx, ctx.Users.Where(x => x.Id == uid), ct);
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = Action, TenantId = r.TenantId, Detail = $"{r.FullName} 탈퇴 승인 · {today:M월 d일}" });
        await ctx.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        if (photos.FullPath(photo) is { } path && File.Exists(path)) File.Delete(path);
        return LeaveError.None;
    }

    public static string Message(LeaveError e) => e switch
    {
        LeaveError.BadPassword => "비밀번호가 맞지 않아요.",
        LeaveError.BadWord => $"확인 글자에 \"{ConfirmWord}\" 라고 써 주세요.",
        LeaveError.IsAdmin => "관리자 계정은 탈퇴할 수 없어요.",
        LeaveError.Already => "이미 탈퇴를 신청했어요.",
        LeaveError.NoDept => "신청할 부서가 없어요. 플랫폼 관리자에게 말해 주세요.",
        _ => "탈퇴 신청을 찾을 수 없어요.",
    };
}

/// <summary>할 일 · 탈퇴 신청 화면이 같이 쓰는 승인 · 반려 한 번(R-343). tenantId = 부서 관리자의 부서, 플랫폼 관리자는 null.</summary>
public static class LeaveActions
{
    public static async Task<string> DoAsync(LeaveService leave, string actor, int id, string? what, int? tenantId, CancellationToken ct)
    {
        if (what == "ok")
        {
            var e = await leave.ApproveAsync(actor, id, tenantId, ct);
            return e == LeaveError.None ? "탈퇴를 승인했어요. 계정과 기록을 지웠어요." : LeaveService.Message(e);
        }
        return await leave.RejectAsync(actor, id, tenantId, ct) ? "탈퇴 신청을 반려했어요." : LeaveService.Message(LeaveError.NotFound);
    }
}
