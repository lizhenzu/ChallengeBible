using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>Id = 신청이면 SignupApplication.Id, 승인이면 DeptRoleUserAssoc.Id.</summary>
public sealed record MemberRow(int Id, string UserName, string DisplayName, string Email, string Status,
    string? RoleCode, string RoleLabel, bool IsAdmin, DateTime RequestedUtc);

public sealed record MemberFilter(string Status, string? Query, string? Role);

public enum MemberError
{
    None, NotFound, NoRole, BadState, SelfAdminRemove, TeamLocked,
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
                           select new { a.Id, a.UserName, a.FullName, u.Email, a.RoleKind, a.InsertDT }).ToListAsync(ct))
                .Select(x => new MemberRow(x.Id, x.UserName, x.FullName, x.Email ?? "", f.Status, UserRole.CodeForKind(x.RoleKind.Trim()),
                    Label(UserRole.CodeForKind(x.RoleKind.Trim()), codes), false, x.InsertDT)).ToList();
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
    public async Task<MemberError> ApproveAsync(string actor, int applicationId, string? roleCode, CancellationToken ct = default)
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
        var (error, moved) = await DeptMembership.PlaceAsync(sys, user, me, Clean(roleCode), now, ct);
        if (error == MoveError.TeamLocked) return MemberError.TeamLocked;
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
        if (!await FamilyTeamService.FitTeamToRoleAsync(ctx, tenant.Current.Id, userName!, UserRole.KindOf(code), ct)) return MemberError.TeamLocked;
        d.RoleCode = code;
        d.UpdateDT = time.GetUtcNow().UtcDateTime;
        await ctx.SaveChangesAsync(ct);
        return MemberError.None;
    }

    /// <summary>이 부서의 관리자 지정 · 해제(Identity Admin 역할). 나 자신의 관리자 권한은 뺄 수 없다.</summary>
    public async Task<MemberError> SetAdminAsync(string actor, int deptRoleId, bool admin, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var d = await ctx.DeptRoles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == deptRoleId, ct);
        if (d is null) return MemberError.NotFound;
        var user = await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == d.UserId, ct);
        if (user.UserName == actor && !admin) return MemberError.SelfAdminRemove;
        await using var sys = systemDb.Create(actor);
        var role = await sys.IdentityRoleRows.SingleAsync(r => r.Name == IdentityRoles.Admin, ct);
        var row = await sys.UserRoles.SingleOrDefaultAsync(r => r.UserId == user.Id && r.RoleId == role.Id, ct);
        if (admin && row is null) sys.UserRoles.Add(new AppUserRole { UserId = user.Id, RoleId = role.Id });
        else if (!admin && row is not null) sys.UserRoles.Remove(row);
        await sys.SaveChangesAsync(ct);
        return MemberError.None;
    }

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
        if (await ctx.GuardianLinks.AnyAsync(l => l.GuardianUserName == guardian && l.StudentUserName == student, ct)) return MemberError.None;
        ctx.GuardianLinks.Add(new GuardianLink { TenantId = tenant.Current.Id, GuardianUserName = guardian, StudentUserName = student,
            CreatedUtc = time.GetUtcNow().UtcDateTime, CreatedByUserName = actor });
        await ctx.SaveChangesAsync(ct);
        return MemberError.None;
    }

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
        MemberError.TeamLocked => "시상 기록이 있는 팀에 있어 바꿀 수 없어요.",
        _ => "",
    };
}
