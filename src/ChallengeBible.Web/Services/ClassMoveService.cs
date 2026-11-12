using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public enum MoveClassError { None, NotFound, Fixed, BadTarget, Empty }

/// <summary>
/// 반별 학생 이동(ADR 0093 반별 학생 이동, R-238 · R-239). 부서 관리자만 — 화면이 먼저 검사한다.
/// 반 하나의 학생을 모두 미배정반으로, 또는 다른 부서(받는 부서 승인 없이 그 부서 미배정반)로 옮긴다.
/// 옮길 때 아이마다 꼬리표(이전 부서 · 반 · 담임 · 담임 전화)를 남기고, 타부서로 가도 따라간다.
/// </summary>
public sealed class ClassMoveService(IAppDb db, ISystemDb systemDb, ClassService classes, TenantContext tenant, TimeProvider time)
{
    /// <summary>받을 수 있는 부서: 이 부서 · 임시 부서 · 쉬는 부서를 뺀 나머지.</summary>
    public async Task<List<MoveTarget>> TargetsAsync(string actor, CancellationToken ct = default)
    {
        await using var sys = systemDb.Create(actor);
        var me = tenant.Current.Id;
        return await sys.Tenants.AsNoTracking().Where(t => t.Id != me && !t.IsLobby && t.Status == TenantStatus.Active)
            .OrderBy(t => t.Name).Select(t => new MoveTarget(t.Id, t.Name)).ToListAsync(ct);
    }

    /// <summary>꼬리표를 붙이고 반에서 뺀다(같은 부서 미배정반으로). 옮긴 학생 번호를 돌려준다.</summary>
    private async Task<(MoveClassError Error, List<int> People)> TagAndReleaseAsync(int classId, CancellationToken ct)
    {
        await using var ctx = db.Create();
        var c = await ctx.Classes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == classId, ct);
        if (c is null) return (MoveClassError.NotFound, []);
        if (c.IsNewcomer) return (MoveClassError.Fixed, []);
        var members = await ctx.ClassMembers.Where(m => m.ClassId == c.Id).ToListAsync(ct);
        var kids = members.Where(m => !m.IsTeacher).ToList();
        if (kids.Count == 0) return (MoveClassError.Empty, []);
        var teacherIds = members.Where(m => m.IsTeacher).Select(m => m.PersonId).ToList();
        var roster = (await classes.RosterAsync(ct)).ToDictionary(r => r.PersonId);
        var teachers = teacherIds.Where(roster.ContainsKey).Select(id => roster[id]).ToList();
        var tName = teachers.Count == 0 ? null : Cut(string.Join(", ", teachers.Select(t => t.Name)), 100);
        var tPhone = teachers.Select(t => t.Phone1).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        var ids = kids.Select(k => k.PersonId).ToList();
        foreach (var p in await ctx.People.Where(p => ids.Contains(p.Id)).ToListAsync(ct))
            (p.PrevDeptName, p.PrevClassName, p.PrevTeacherName, p.PrevTeacherPhone) = (Cut(tenant.Current.Name, 100), c.Name, tName, tPhone);
        ctx.ClassMembers.RemoveRange(kids);
        await ctx.SaveChangesAsync(ct);
        return (MoveClassError.None, ids);
    }

    /// <summary>[미배정반으로 이동](R-238): 그 반 학생 모두 이 부서 미배정반으로. 반과 담임은 그대로 남는다.</summary>
    public async Task<(MoveClassError Error, int Count)> ToUnassignedAsync(int classId, CancellationToken ct = default)
    {
        var (e, ids) = await TagAndReleaseAsync(classId, ct);
        return (e, ids.Count);
    }

    /// <summary>
    /// [타부서로 이동](R-239): 그 반 학생 모두 받을 부서의 미배정반으로 바로(승인 없음). 가입한 아이는 소속째(미션 기록 · 팀 정리 포함),
    /// 가입 안 한 아이는 명부 줄째로 옮긴다. 출석 · 꼬리표 · 생일 · 연락처가 따라간다. 시상 기록이 있는 팀에 든 아이는 남긴다(이름을 돌려준다).
    /// </summary>
    public async Task<(MoveClassError Error, int Moved, List<string> Skipped)> ToDeptAsync(string actor, int classId, int targetTenantId, CancellationToken ct = default)
    {
        if (!(await TargetsAsync(actor, ct)).Any(t => t.TenantId == targetTenantId)) return (MoveClassError.BadTarget, 0, []);
        var (e, ids) = await TagAndReleaseAsync(classId, ct);
        if (e != MoveClassError.None) return (e, 0, []);

        var me = tenant.Current.Id;
        var now = time.GetUtcNow().UtcDateTime;
        var moved = 0;
        List<string> skipped = [];
        await using var sys = systemDb.Create(actor);
        await using var tx = sys.Database.IsRelational() ? await sys.Database.BeginTransactionAsync(ct) : null;
        var people = await sys.People.Where(p => p.TenantId == me && ids.Contains(p.Id)).ToListAsync(ct);
        foreach (var p in people)
        {
            if (p.UserId is not null)
            {
                var user = await sys.Users.SingleAsync(u => u.Id == p.UserId, ct);
                var role = await sys.DeptRoles.Where(d => d.TenantId == me && d.UserId == user.Id).Select(d => d.RoleCode).FirstOrDefaultAsync(ct);
                var (err, _) = await DeptMembership.PlaceAsync(sys, user, targetTenantId, role, now, ct, null, actor);
                if (err == MoveError.TeamLocked) { skipped.Add(p.Name); continue; }
            }
            else
            {
                var copy = new Person { TenantId = targetTenantId, Name = p.Name, RoleCode = p.RoleCode, CreatedUtc = now, CreatedByUserName = actor };
                PeopleService.CarryOver(p, copy);
                sys.People.Add(copy);
                await sys.SaveChangesAsync(ct);
                await PeopleService.MoveChecksAsync(sys, p, copy, ct);
                sys.ClassMembers.RemoveRange(await sys.ClassMembers.Where(m => m.TenantId == me && m.PersonId == p.Id).ToListAsync(ct));
                sys.People.Remove(p);
                await sys.SaveChangesAsync(ct);
            }
            moved++;
        }
        sys.AuditLogs.Add(new AuditLog { OccurredUtc = now, ActorUserName = actor, Action = "ClassMoved", TenantId = me,
            Detail = $"class={classId} target={targetTenantId} moved={moved} skipped={skipped.Count}" });
        await sys.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return (MoveClassError.None, moved, skipped);
    }

    private static string Cut(string s, int n) => s.Length > n ? s[..n] : s;

    public static string Message(MoveClassError e) => e switch
    {
        MoveClassError.NotFound => "찾을 수 없어요. 화면을 새로 고쳐 주세요.",
        MoveClassError.Fixed => "새친구반은 옮기지 않아요. 새친구는 실제 반에서 옮겨요.",
        MoveClassError.BadTarget => "받을 부서를 골라 주세요.",
        MoveClassError.Empty => "이 반에 아이가 없어요.",
        _ => "",
    };
}
