using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>Id = 신청이면 SignupApplication.Id, 승인이면 DeptRoleUserAssoc.Id.</summary>
public sealed record MemberRow(int Id, string UserName, string DisplayName, string Email, string Status,
    string? RoleCode, string RoleLabel, bool IsAdmin, DateTime RequestedUtc)
{
    /// <summary>신청에 쓴 생일(선택, 0 = 안 씀) — 승인 때 명부 후보 순서(확정 시안 명부계정연결 ③).</summary>
    public int BMonth { get; init; }
    public int BDay { get; init; }
}

public sealed record MemberFilter(string Status, string? Query, string? Role);

public enum MemberError
{
    None, NotFound, NoRole, BadState, SelfAdminRemove, TeamLocked, RuleAdmin, RuleOnly, AdminNotStudent,
}

/// <summary>
/// 소속 관리(ADR 0090): 신청 승인 · 거절, 역할 바꾸기, 관리자 지정. 승인하면 DeptRoleUserAssoc 에 한 줄이 생긴다(유년부와 같다).
/// 역할이 바뀌어 맞지 않는 팀에서는 빠진다 — 단 시상 기록이 있는 팀이면 막는다.
/// 부르는 화면이 관리자인지 먼저 검사한다.
/// </summary>
public sealed class MemberAdminService(IAppDb db, ISystemDb systemDb, TenantContext tenant, TimeProvider time)
{
    public async Task<List<MemberRow>> ListAsync(MemberFilter f, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var ko = StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false);
        var codes = await RoleNamesAsync(ctx, ct);
        List<MemberRow> rows;
        if (f.Status == MembershipStatus.Approved)
        {
            var adminIds = await AdminIdsAsync(ctx, ct);
            rows = (await (from d in ctx.DeptRoles
                           join u in ctx.Users on d.UserId equals u.Id
                           select new { d.Id, u.UserName, u.LastName, u.FirstName, u.Email, d.RoleCode, d.UserId, d.InsertDT }).ToListAsync(ct))
                .Select(x => new MemberRow(x.Id, x.UserName!, AppUser.FullName(x.LastName, x.FirstName), x.Email ?? "", MembershipStatus.Approved,
                    x.RoleCode, Label(x.RoleCode, codes), adminIds.Contains(x.UserId!), x.InsertDT)).ToList();
        }
        else
        {
            var status = f.Status == MembershipStatus.Rejected ? SignupStatus.Rejected : SignupStatus.Pending;
            rows = (await (from a in ctx.SignupApplications
                           join u in ctx.Users on a.UserId equals u.Id
                           where a.Status == status
                           select new { a.Id, a.UserName, a.FullName, u.Email, a.RoleKind, a.InsertDT, a.BMonth, a.BDay }).ToListAsync(ct))
                .Select(x => new MemberRow(x.Id, x.UserName, x.FullName, x.Email ?? "", f.Status, UserRole.CodeForKind(x.RoleKind.Trim()),
                    Label(UserRole.CodeForKind(x.RoleKind.Trim()), codes), false, x.InsertDT) { BMonth = x.BMonth, BDay = x.BDay }).ToList();
        }
        if (!string.IsNullOrWhiteSpace(f.Query))
        {
            var s = f.Query.Trim();
            rows = rows.Where(r => r.DisplayName.Contains(s) || r.UserName.Contains(s, StringComparison.OrdinalIgnoreCase) || r.Email.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (!string.IsNullOrWhiteSpace(f.Role)) rows = rows.Where(r => (r.RoleCode ?? "") == (f.Role == "-" ? "" : f.Role)).ToList();
        return rows.OrderBy(r => r.DisplayName, ko).ToList();
    }

    private static async Task<Dictionary<string, string>> RoleNamesAsync(AppDbContext ctx, CancellationToken ct) =>
        await ctx.CodeData.AsNoTracking().Where(c => c.MajorCD == CodeData.RoleMajor && c.MinorCD != null)
            .ToDictionaryAsync(c => c.MinorCD!, c => c.MinorNM ?? c.MinorCD!, ct);

    private static string Label(string? code, Dictionary<string, string> names) =>
        string.IsNullOrWhiteSpace(code) ? UserRole.LabelOf(UserRoleKind.Parent) : names.GetValueOrDefault(code.Trim(), UserRole.LabelOf(UserRole.KindOf(code)));

    private static async Task<HashSet<string>> AdminIdsAsync(AppDbContext ctx, CancellationToken ct) =>
        (await (from ur in ctx.UserRoles
                join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id
                where r.Name == IdentityRoles.Admin
                select ur.UserId).ToListAsync(ct)).ToHashSet();

    /// <summary>이 부서의 역할 코드 목록(화면의 고르기).</summary>
    public async Task<List<(string Code, string Name)>> RoleCodesAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return (await ctx.CodeData.AsNoTracking().Where(c => c.MajorCD == CodeData.RoleMajor && c.MinorCD != null).OrderBy(c => c.DisplayOrder).ThenBy(c => c.Id)
            .Select(c => new { c.MinorCD, c.MinorNM }).ToListAsync(ct)).Select(c => (c.MinorCD!, c.MinorNM ?? c.MinorCD!)).ToList();
    }

    public async Task<Dictionary<string, int>> CountsAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var counts = await ctx.SignupApplications.GroupBy(a => a.Status).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return new Dictionary<string, int>
        {
            [MembershipStatus.Requested] = counts.GetValueOrDefault(SignupStatus.Pending),
            [MembershipStatus.Rejected] = counts.GetValueOrDefault(SignupStatus.Rejected),
            [MembershipStatus.Approved] = await ctx.DeptRoles.CountAsync(ct),
        };
    }

    /// <summary>신청 승인: 역할 코드(비면 학부모)로 소속을 만든다. 임시 부서에 있던 사람이면 기록도 옮긴다(R-173 · R-174).</summary>
    public Task<MemberError> ApproveAsync(string actor, int applicationId, string? roleCode, CancellationToken ct = default) =>
        ApproveAsync(actor, applicationId, roleCode, null, ct);

    /// <summary>승인 + 명부 연결(R-228): personId 가 있으면 그 명부 줄에 계정을 잇고, 없으면 새 줄을 만든다.</summary>
    public async Task<MemberError> ApproveAsync(string actor, int applicationId, string? roleCode, int? personId, CancellationToken ct = default)
    {
        await using (var ctx = db.Create())
        {
            var mine = await ctx.SignupApplications.AnyAsync(a => a.Id == applicationId && a.Status == SignupStatus.Pending, ct);
            if (!mine) return await ctx.SignupApplications.AnyAsync(a => a.Id == applicationId, ct) ? MemberError.BadState : MemberError.NotFound;
        }
        await using var sys = systemDb.Create(actor);
        await using var tx = sys.Database.IsRelational() ? await sys.Database.BeginTransactionAsync(ct) : null;
        var now = time.GetUtcNow().UtcDateTime;
        var me = tenant.Current.Id;
        var a = await sys.SignupApplications.SingleAsync(x => x.TenantId == me && x.Id == applicationId, ct);
        var user = await sys.Users.SingleAsync(u => u.Id == a.UserId, ct);
        var (error, moved) = await DeptMembership.PlaceAsync(sys, user, me, Clean(roleCode), now, ct, personId, actor);
        if (error == MoveError.TeamLocked) return MemberError.TeamLocked;
        if (error == MoveError.AdminNotStudent) return MemberError.AdminNotStudent;   // R-365
        a.Status = SignupStatus.Approved;
        a.DecidedDT = now;
        a.DecidedBy = actor;
        if (moved.Activities + moved.QuizAttempts + moved.Skipped > 0)
            sys.AuditLogs.Add(new AuditLog
            {
                OccurredUtc = now, ActorUserName = actor, Action = LobbyService.MovedAction, TenantId = me,
                Detail = $"account={user.UserName} activities={moved.Activities} points={moved.Points} quiz={moved.QuizAttempts} skipped={moved.Skipped}",
            });
        await sys.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return MemberError.None;
    }

    public async Task<MemberError> RejectAsync(string actor, int applicationId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var a = await ctx.SignupApplications.SingleOrDefaultAsync(x => x.Id == applicationId, ct);
        if (a is null) return MemberError.NotFound;
        if (a.Status != SignupStatus.Pending) return MemberError.BadState;
        a.Status = SignupStatus.Rejected;
        a.DecidedDT = time.GetUtcNow().UtcDateTime;
        a.DecidedBy = actor;
        await ctx.SaveChangesAsync(ct);
        return MemberError.None;
    }

    /// <summary>역할 바꾸기. 새 역할과 맞지 않는 팀에서는 빠지고, 시상 기록이 있는 팀이면 막는다(유년부 FitTeamToRole).</summary>
    public async Task<MemberError> ChangeRoleAsync(int deptRoleId, string? roleCode, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var d = await ctx.DeptRoles.SingleOrDefaultAsync(x => x.Id == deptRoleId, ct);
        if (d is null) return MemberError.NotFound;
        var userName = await ctx.Users.Where(u => u.Id == d.UserId).Select(u => u.UserName).SingleAsync(ct);
        var code = Clean(roleCode);
        if (DeptMembership.StudentLike(code) && await DeptMembership.IsAdminAccountAsync(ctx, d.UserId!, ct)) return MemberError.AdminNotStudent;   // R-365
        if (!await FamilyTeamService.FitTeamToRoleAsync(ctx, tenant.Current.Id, userName!, UserRole.KindOf(code), ct)) return MemberError.TeamLocked;
        d.RoleCode = code;
        d.UpdateDT = time.GetUtcNow().UtcDateTime;
        await ctx.SaveChangesAsync(ct);
        await using (var sys = systemDb.Create(userName!)) await DeptAdminRuleService.SyncAsync(sys, [d.UserId!], ct);   // 부서 관리자 규칙
        return MemberError.None;
    }

    /// <summary>
    /// 사람마다 관리자 지정 · 해제는 없다(2026-11-16 사용자 "권한은 역할 · 업무에"): 부서 관리자는 부서 관리자 규칙의 역할 · 업무로만 정해진다.
    /// 예전 화면 · 요청이 와도 아무것도 바꾸지 않고 알림만 돌려준다.
    /// </summary>
    public Task<MemberError> SetAdminAsync(string actor, int deptRoleId, bool admin, CancellationToken ct = default) => Task.FromResult(MemberError.RuleOnly);

    private static string? Clean(string? code) => string.IsNullOrWhiteSpace(code) ? null : code.Trim();

    // ── 보호 관계(R-132) ──

    /// <summary>이 부서의 학부모(역할 코드 없음): 아이디 · 이름.</summary>
    public async Task<List<(string UserName, string Name)>> GuardiansAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return (await (from d in ctx.DeptRoles
                       where d.RoleCode == null || d.RoleCode == ""
                       join u in ctx.Users on d.UserId equals u.Id
                       select new { u.UserName, u.LastName, u.FirstName }).ToListAsync(ct))
            .Select(x => (x.UserName!, AppUser.FullName(x.LastName, x.FirstName))).OrderBy(x => x.Item2).ToList();
    }

    /// <summary>학생 아이디 → 연결된 보호자 아이디 목록.</summary>
    public async Task<Dictionary<string, List<string>>> LinksAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var rows = await ctx.GuardianLinks.AsNoTracking().Select(l => new { l.StudentUserName, l.GuardianUserName }).ToListAsync(ct);
        return rows.GroupBy(r => r.StudentUserName).ToDictionary(g => g.Key, g => g.Select(x => x.GuardianUserName).ToList());
    }

    public async Task<MemberError> LinkGuardianAsync(string actor, string guardian, string student, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        if (!await IsMemberAsync(ctx, guardian, ct) || !await IsMemberAsync(ctx, student, ct)) return MemberError.NotFound;
        if (UserRole.KindOf(await RoleCodeOfAsync(ctx, guardian, ct)) != UserRoleKind.Parent) return MemberError.NotFound;
        if (await ctx.GuardianLinks.AnyAsync(l => l.GuardianUserName == guardian && l.StudentUserName == student, ct)) return MemberError.None;
        ctx.GuardianLinks.Add(new GuardianLink { TenantId = tenant.Current.Id, GuardianUserName = guardian, StudentUserName = student,
            CreatedUtc = time.GetUtcNow().UtcDateTime, CreatedByUserName = actor });
        await ctx.SaveChangesAsync(ct);
        return MemberError.None;
    }

    private static Task<string?> RoleCodeOfAsync(AppDbContext ctx, string userName, CancellationToken ct) =>
        (from d in ctx.DeptRoles join u in ctx.Users on d.UserId equals u.Id where u.UserName == userName select d.RoleCode).FirstOrDefaultAsync(ct);

    private static Task<bool> IsMemberAsync(AppDbContext ctx, string userName, CancellationToken ct) =>
        (from d in ctx.DeptRoles join u in ctx.Users on d.UserId equals u.Id where u.UserName == userName select d).AnyAsync(ct);

    public async Task<MemberError> UnlinkGuardianAsync(string guardian, string student, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var link = await ctx.GuardianLinks.SingleOrDefaultAsync(l => l.GuardianUserName == guardian && l.StudentUserName == student, ct);
        if (link is null) return MemberError.NotFound;
        ctx.GuardianLinks.Remove(link);
        await ctx.SaveChangesAsync(ct);
        return MemberError.None;
    }

    public static string Message(MemberError e) => e switch
    {
        MemberError.NotFound => "이 부서에서 찾을 수 없어요.",
        MemberError.NoRole => "역할을 골라 주세요.",
        MemberError.BadState => "이미 처리됐어요. 화면을 새로 고쳐 주세요.",
        MemberError.SelfAdminRemove => "내 관리자 역할은 여기서 뺄 수 없어요.",
        MemberError.RuleOnly => "부서 관리자는 사람마다 정하지 않아요. 지도 · 부장 · 부감 · 서기 · 어드민 역할이나 선물 담당 같은 관리자 업무를 맡으면 저절로 돼요.",
        MemberError.RuleAdmin => "부서 관리자 규칙으로 관리자인 역할이에요. 역할을 바꾸거나 플랫폼의 부서 관리자 규칙을 바꿔 주세요.",
        MemberError.TeamLocked => "시상 기록이 있는 팀에 있어 바꿀 수 없어요.",
        MemberError.AdminNotStudent => DeptMembership.AdminNotStudentMessage,
        _ => "",
    };
}
