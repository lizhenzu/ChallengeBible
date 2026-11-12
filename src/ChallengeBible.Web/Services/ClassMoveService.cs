using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public enum MoveClassError { None, NotFound, Fixed, BadTarget, Empty, NotEligible, SameClass }

/// <summary>반 이동 마법사의 보낼 곳: 이 부서 미배정반 · 이 부서 다른 반 · 다른 부서(그 부서 미배정반).</summary>
public enum MoveDest { Unassigned, Class, Dept }

/// <summary>
/// 반 이동(ADR 0093 반별 학생 이동 + 2026-11-12 마법사, R-238 · R-239). 부서 관리자만 — 화면이 먼저 검사한다.
/// 한 반(또는 미배정반)에서 고른 아이를 미배정반 · 다른 반 · 다른 부서(승인 없이 그 부서 미배정반)로 옮긴다.
/// 반에서 나갈 때 아이마다 꼬리표(이전 부서 · 반 · 담임 · 담임 전화)를 남기고, 타부서로 가도 따라간다. 미배정반에서 옮기면 있던 꼬리표를 그대로 둔다.
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

    /// <summary>옮길 수 있는 아이: from 이 정규 반이면 그 반 학생, null 이면 미배정반 아이.</summary>
    public async Task<List<ClassPerson>> KidsAsync(int? fromClassId, CancellationToken ct = default)
    {
        if (fromClassId is not int id) return await classes.UnassignedAsync(ct);
        var d = await classes.DetailAsync(id, ct);
        return d is null || d.Class.IsNewcomer ? [] : d.Students;
    }

    /// <summary>
    /// 옮기기. 고른 아이는 모두 from 에 있어야 한다. dest = Class 면 같은 부서의 다른 정규 반(학생 자격 검사), Dept 면 다른 부서.
    /// 돌려주는 값: 옮긴 수와, 선물 받은 팀에 있어 타부서로 못 간 아이 이름(이 부서 미배정반에 남는다).
    /// </summary>
    public async Task<(MoveClassError Error, int Moved, List<string> Skipped)> MoveAsync(string actor, int? fromClassId, IReadOnlyCollection<int> personIds,
        MoveDest dest, int? toClassId, int? toTenantId, CancellationToken ct = default)
    {
        var ids = personIds.Distinct().ToList();
        if (ids.Count == 0) return (MoveClassError.Empty, 0, []);
        if (fromClassId is int fc)
        {
            var from = await classes.DetailAsync(fc, ct);
            if (from is null) return (MoveClassError.NotFound, 0, []);
            if (from.Class.IsNewcomer) return (MoveClassError.Fixed, 0, []);
        }
        var kids = (await KidsAsync(fromClassId, ct)).Select(k => k.PersonId).ToHashSet();
        if (ids.Any(i => !kids.Contains(i))) return (MoveClassError.NotFound, 0, []);

        // 보낼 곳 먼저 확인(아무것도 바꾸기 전에)
        if (dest == MoveDest.Class)
        {
            if (toClassId is not int tc) return (MoveClassError.BadTarget, 0, []);
            if (tc == fromClassId) return (MoveClassError.SameClass, 0, []);
            var to = await classes.DetailAsync(tc, ct);
            if (to is null || to.Class.IsNewcomer) return (MoveClassError.BadTarget, 0, []);
        }
        if (dest == MoveDest.Dept && (toTenantId is not int tt || !(await TargetsAsync(actor, ct)).Any(t => t.TenantId == tt)))
            return (MoveClassError.BadTarget, 0, []);

        if (fromClassId is int c) await TagAndReleaseAsync(c, ids, ct);
        switch (dest)
        {
            case MoveDest.Unassigned:
                return (MoveClassError.None, ids.Count, []);
            case MoveDest.Class:
            {
                var e = await classes.AddAsync(toClassId!.Value, ids, teacher: false, ct);
                return e == ClassError.None ? (MoveClassError.None, ids.Count, []) : (MoveClassError.NotEligible, 0, []);
            }
            default:
                return await ToDeptAsync(actor, ids, toTenantId!.Value, ct);
        }
    }

    /// <summary>꼬리표를 붙이고 그 반에서 뺀다(같은 부서 미배정반으로).</summary>
    private async Task TagAndReleaseAsync(int classId, List<int> ids, CancellationToken ct)
    {
        await using var ctx = db.Create();
        var c = await ctx.Classes.AsNoTracking().SingleAsync(x => x.Id == classId, ct);
        var members = await ctx.ClassMembers.Where(m => m.ClassId == c.Id).ToListAsync(ct);
        var roster = (await classes.RosterAsync(ct)).ToDictionary(r => r.PersonId);
        var teachers = members.Where(m => m.IsTeacher && roster.ContainsKey(m.PersonId)).Select(m => roster[m.PersonId]).ToList();
        var tName = teachers.Count == 0 ? null : Cut(string.Join(", ", teachers.Select(t => t.Name)), 100);
        var tPhone = teachers.Select(t => t.Phone1).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        foreach (var p in await ctx.People.Where(p => ids.Contains(p.Id)).ToListAsync(ct))
            (p.PrevDeptName, p.PrevClassName, p.PrevTeacherName, p.PrevTeacherPhone) = (Cut(tenant.Current.Name, 100), c.Name, tName, tPhone);
        ctx.ClassMembers.RemoveRange(members.Where(m => !m.IsTeacher && ids.Contains(m.PersonId)));
        await ctx.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 다른 부서 미배정반으로 바로(R-239): 가입한 아이는 소속째(미션 기록 · 팀 정리 포함), 가입 안 한 아이는 명부 줄째로.
    /// 출석 · 꼬리표 · 생일 · 연락처 · 새친구 표시가 따라간다. 시상 기록이 있는 팀에 든 아이는 남긴다.
    /// </summary>
    private async Task<(MoveClassError, int, List<string>)> ToDeptAsync(string actor, List<int> ids, int targetTenantId, CancellationToken ct)
    {
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
            Detail = $"target={targetTenantId} moved={moved} skipped={skipped.Count}" });
        await sys.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return (MoveClassError.None, moved, skipped);
    }

    private static string Cut(string s, int n) => s.Length > n ? s[..n] : s;

    public static string Message(MoveClassError e) => e switch
    {
        MoveClassError.NotFound => "찾을 수 없어요. 처음부터 다시 해 주세요.",
        MoveClassError.Fixed => "새친구반은 옮기지 않아요. 새친구는 실제 반에서 옮겨요.",
        MoveClassError.BadTarget => "보낼 곳을 골라 주세요.",
        MoveClassError.Empty => "옮길 아이를 골라 주세요.",
        MoveClassError.NotEligible => "그 반에 넣을 수 없는 아이가 있어요(학생 · 새친구만). 미배정반으로 옮겨졌어요.",
        MoveClassError.SameClass => "지금 있는 반과 다른 반을 골라 주세요.",
        _ => "",
    };
}
