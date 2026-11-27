using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>Students = 정규 반이면 학생 수, 새친구반이면 관리 중인 새친구 수.</summary>
public sealed record ClassRow(int Id, string Name, bool IsNewcomer, IReadOnlyList<string> Teachers, int Students);
/// <summary>Note = 꼬리표(미배정반) 또는 새친구의 반 이름(새친구반).</summary>
public sealed record ClassPerson(int PersonId, string Name, string RoleLabel, string Birthday, bool Linked, bool Newcomer = false, string? Note = null);
public sealed record MoveTarget(int TenantId, string Name);
public sealed record ClassDetail(SchoolClass Class, List<ClassPerson> Teachers, List<ClassPerson> Students,
    List<ClassPerson> TeacherCandidates, List<ClassPerson> StudentCandidates);

/// <summary>교사 화면(2026-11-21 확정 시안 역할-담임-시안2 ①) 사람 한 명. Kind = UserRole 종류, ClassId = 지금 들어 있는 반(없으면 null), Teacher = 그 반 선생님인가.</summary>
public sealed record BoardPerson(int PersonId, string Name, string RoleLabel, UserRoleKind Kind, bool IsAdmin, string? Phone, int? ClassId, bool Teacher);
/// <summary>반 하나와 선생님들(먼저 넣은 순 — 첫째가 담임, 다음은 부담임).</summary>
public sealed record BoardClass(int Id, string Name, bool IsNewcomer, IReadOnlyList<BoardPerson> Teachers);
public sealed record TeacherBoard(IReadOnlyList<BoardClass> Classes, IReadOnlyList<BoardPerson> NoClass, IReadOnlyList<BoardPerson> People);

public enum ClassError { None, NotFound, NoName, DupName, NotEligible, InOtherClass, Fixed }

/// <summary>
/// 반(ADR 0093 "반 관리 및 자동 생성 규칙", R-231 ~ R-233). 해 구분 없이 부서 반은 한 번 만들고 계속 쓴다.
/// 모든 부서에 새친구반(저장) · 미배정반(정규 반이 없는 학생, 계산)을 자동으로 둔다. 학생 · 선생님은 한 반에만 든다.
/// 일반 반 선생님은 교사 · 임원, 새친구반 담당은 교사 · 임원 · 부서 관리자. 부서 관리자만 부른다 — 화면이 먼저 검사한다.
/// 해 표(ClassTerm)는 부서마다 하나(가장 먼저 만든 것)만 쓴다.
/// </summary>
public sealed class ClassService(IAppDb db, PeopleService people, TenantContext tenant, TimeProvider time)
{
    public const string NewcomerName = "새친구반";
    public const string UnassignedName = "미배정반";
    public const string NewcomerCode = "T1020";

    /// <summary>부서의 반 묶음과 새친구반을 없으면 만든다(R-231). 임시 부서에는 반이 없다.</summary>
    private async Task<int> TermAsync(AppDbContext ctx, CancellationToken ct)
    {
        var term = await ctx.ClassTerms.OrderBy(t => t.Id).FirstOrDefaultAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (term is null)
        {
            var today = TenantClock.ToTenantDate(time.GetUtcNow(), tenant.Current.TimeZoneId);
            term = new ClassTerm { TenantId = tenant.Current.Id, Year = today.Year, StartDate = today, CreatedUtc = now };
            ctx.ClassTerms.Add(term);
            await ctx.SaveChangesAsync(ct);
        }
        if (!await ctx.Classes.AnyAsync(c => c.TermId == term.Id && c.IsNewcomer, ct))
        {
            ctx.Classes.Add(new SchoolClass { TenantId = tenant.Current.Id, TermId = term.Id, Name = NewcomerName, IsNewcomer = true, CreatedUtc = now });
            await ctx.SaveChangesAsync(ct);
        }
        return term.Id;
    }

    /// <summary>이 부서 명부(이름 · 역할).</summary>
    public Task<List<RosterRow>> RosterAsync(CancellationToken ct = default) => people.RosterAsync(null, null, ct);

    /// <summary>반 목록: 새친구반 먼저, 정규 반은 이름 순. 미배정반은 <see cref="UnassignedAsync"/> 로 따로.</summary>
    public async Task<List<ClassRow>> ClassesAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var term = await TermAsync(ctx, ct);
        var classes = await ctx.Classes.AsNoTracking().Where(c => c.TermId == term).ToListAsync(ct);
        var members = await ctx.ClassMembers.AsNoTracking().Where(m => m.TermId == term).ToListAsync(ct);
        var names = (await people.RosterAsync(null, null, ct)).ToDictionary(r => r.PersonId, r => r.Name);
        var newcomers = (await NewcomersAsync(ctx, ct)).Count;
        var ko = StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false);
        return classes.OrderByDescending(c => c.IsNewcomer).ThenBy(c => c.Name, ko)
            .Select(c => new ClassRow(c.Id, c.Name, c.IsNewcomer,
                members.Where(m => m.ClassId == c.Id && m.IsTeacher).Select(m => names.GetValueOrDefault(m.PersonId, "?")).ToList(),
                c.IsNewcomer ? newcomers : members.Count(m => m.ClassId == c.Id && !m.IsTeacher))).ToList();
    }

    /// <summary>
    /// 교사 화면(2026-11-21 사용자 "교사와 교사 외로 나누기", 확정 시안 역할-담임-시안2 ①): 반별 선생님(먼저 넣은 순) · 반이 없는 교사 ·
    /// 바닥 창에서 고를 사람들(학생 · 새친구 · 탈퇴한 줄은 뺀다).
    /// </summary>
    public async Task<TeacherBoard> TeacherBoardAsync(CancellationToken ct = default)
    {
        var rows = await ClassesAsync(ct);   // 반 순서(새친구반 먼저 · 이름 순)
        await using var ctx = db.Create();
        var term = await TermAsync(ctx, ct);
        var members = await ctx.ClassMembers.AsNoTracking().Where(m => m.TermId == term).OrderBy(m => m.Id).ToListAsync(ct);
        var roster = (await people.RosterAsync(null, null, ct)).Where(r => r.LeftOn is null).ToList();
        var byId = roster.ToDictionary(r => r.PersonId);
        BoardPerson B(RosterRow r)
        {
            var m = members.FirstOrDefault(x => x.PersonId == r.PersonId);
            return new BoardPerson(r.PersonId, r.Name, r.RoleLabel, UserRole.KindOf(r.RoleCode), r.IsAdmin, r.Phone1 ?? r.Phone2, m?.ClassId, m?.IsTeacher ?? false);
        }
        var classes = rows.Select(c => new BoardClass(c.Id, c.Name, c.IsNewcomer,
            members.Where(m => m.ClassId == c.Id && m.IsTeacher && byId.ContainsKey(m.PersonId)).Select(m => B(byId[m.PersonId])).ToList())).ToList();
        var placed = members.Select(m => m.PersonId).ToHashSet();
        var noClass = roster.Where(r => UserRole.KindOf(r.RoleCode) == UserRoleKind.Teacher && !placed.Contains(r.PersonId)).Select(B).ToList();
        var pool = roster.Where(r => !IsStudent(r)).Select(B).ToList();
        return new TeacherBoard(classes, noClass, pool);
    }

    /// <summary>담임 교체(2026-11-21 사용자): 이 반에서 한 선생님을 빼고 다른 사람을 같은 반 선생님으로. 넣기가 안 되면 원래대로 되돌린다.</summary>
    public async Task<ClassError> ReplaceTeacherAsync(int classId, int oldPersonId, int newPersonId, CancellationToken ct = default)
    {
        if (oldPersonId == newPersonId) return ClassError.None;
        var e = await RemoveAsync(classId, oldPersonId, ct);
        if (e != ClassError.None) return e;
        e = await AddAsync(classId, [newPersonId], teacher: true, ct);
        if (e != ClassError.None) await AddAsync(classId, [oldPersonId], teacher: true, ct);
        return e;
    }

    /// <summary>선생님을 다른 반으로(빼고 넣기). 넣기가 안 되면 원래 반에 다시 넣는다.</summary>
    public async Task<ClassError> MoveTeacherAsync(int fromClassId, int toClassId, int personId, CancellationToken ct = default)
    {
        if (fromClassId == toClassId) return ClassError.None;
        var e = await RemoveAsync(fromClassId, personId, ct);
        if (e != ClassError.None) return e;
        e = await AddAsync(toClassId, [personId], teacher: true, ct);
        if (e != ClassError.None) await AddAsync(fromClassId, [personId], teacher: true, ct);
        return e;
    }

    /// <summary>반마다 선생님(이름 · 연락처 1) — 교적부 반 목록(R-361). 반 번호 → 선생님들.</summary>
    public async Task<Dictionary<int, List<(string Name, string? Phone)>>> TeachersByClassAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var term = await TermAsync(ctx, ct);
        var teachers = await ctx.ClassMembers.AsNoTracking().Where(m => m.TermId == term && m.IsTeacher).OrderBy(m => m.Id).Select(m => new { m.ClassId, m.PersonId }).ToListAsync(ct);
        var roster = (await people.RosterAsync(null, null, ct)).ToDictionary(r => r.PersonId);
        return teachers.GroupBy(t => t.ClassId).ToDictionary(g => g.Key, g => g.Select(t => roster.TryGetValue(t.PersonId, out var r) ? (r.Name, r.Phone1 ?? r.Phone2) : ("?", (string?)null)).ToList());
    }

    /// <summary>정규 반에 든 학생(사람 번호 → 반 번호) — 교적부 이름 찾기(R-361). 새친구반은 빼고 실제 반만.</summary>
    public async Task<Dictionary<int, int>> StudentClassAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var term = await TermAsync(ctx, ct);
        var real = await ctx.Classes.AsNoTracking().Where(c => c.TermId == term && !c.IsNewcomer).Select(c => c.Id).ToListAsync(ct);
        return (await ctx.ClassMembers.AsNoTracking().Where(m => m.TermId == term && !m.IsTeacher && real.Contains(m.ClassId)).Select(m => new { m.PersonId, m.ClassId }).ToListAsync(ct))
            .GroupBy(m => m.PersonId).ToDictionary(g => g.Key, g => g.First().ClassId);
    }

    /// <summary>이번 학기 반 이름: 사람 번호 → (정규 반 이름, 선생님인지). 새친구반은 빼고 정규 반만(참여자 화면 「0학년1반」 · 「0학년1반 담당」).</summary>
    public async Task<Dictionary<int, (string Class, bool Teacher)>> PersonClassNamesAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var term = await TermAsync(ctx, ct);
        var real = await ctx.Classes.AsNoTracking().Where(c => c.TermId == term && !c.IsNewcomer).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return (await ctx.ClassMembers.AsNoTracking().Where(m => m.TermId == term && real.Keys.Contains(m.ClassId)).OrderBy(m => m.Id).Select(m => new { m.PersonId, m.ClassId, m.IsTeacher }).ToListAsync(ct))
            .GroupBy(m => m.PersonId).ToDictionary(g => g.Key, g => (real[g.First().ClassId], g.First().IsTeacher));
    }

    private DateOnly Today => TenantClock.ToTenantDate(time.GetUtcNow(), tenant.Current.TimeZoneId);

    /// <summary>관리 중인 새친구(사람 번호 → 출석 수). 새친구는 새친구반과 실제 반(또는 미배정반) 양쪽에 보인다(반별 학생 이동 6).</summary>
    private Task<Dictionary<int, int>> NewcomersAsync(AppDbContext ctx, CancellationToken ct) => AttendanceService.ActiveNewcomersAsync(ctx, Today, ct);

    /// <summary>미배정반(R-232): 학생(또는 새친구 역할)인데 정규 반에 없는 사람 모두. 따로 저장하지 않아 진급 · 새 등록 · 반에서 빼기가 저절로 반영된다.</summary>
    public async Task<List<ClassPerson>> UnassignedAsync(CancellationToken ct = default)
    {
        var roster = await people.RosterAsync(null, null, ct);
        await using var ctx = db.Create();
        var term = await TermAsync(ctx, ct);
        var placed = (await ctx.ClassMembers.AsNoTracking().Where(m => m.TermId == term).Select(m => m.PersonId).ToListAsync(ct)).ToHashSet();
        var nc = await NewcomersAsync(ctx, ct);
        return roster.Where(r => IsStudent(r) && !placed.Contains(r.PersonId)).Select(r => P(r) with { Newcomer = nc.ContainsKey(r.PersonId), Note = r.Prev }).ToList();
    }

    public async Task<ClassDetail?> DetailAsync(int classId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var c = await ctx.Classes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == classId, ct);
        if (c is null) return null;
        var members = await ctx.ClassMembers.AsNoTracking().Where(m => m.TermId == c.TermId).ToListAsync(ct);
        var roster = await people.RosterAsync(null, null, ct);
        var placed = members.Select(m => m.PersonId).ToHashSet();
        var mine = members.Where(m => m.ClassId == c.Id).ToList();
        var classNames = await ctx.Classes.AsNoTracking().Where(x => x.TermId == c.TermId).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var nc = await NewcomersAsync(ctx, ct);
        List<ClassPerson> Of(bool teacher) => roster.Where(r => mine.Any(m => m.PersonId == r.PersonId && m.IsTeacher == teacher))
            .Select(r => P(r) with { Newcomer = nc.ContainsKey(r.PersonId), Note = teacher ? r.Phone1 : null }).ToList();   // 선생님은 연락처 1
        // 새친구반: 학생 칸에 관리 중인 새친구(실제 반 이름을 함께)
        var students = c.IsNewcomer
            ? roster.Where(r => nc.ContainsKey(r.PersonId)).Select(r => P(r) with
              {
                  Newcomer = true,
                  Note = members.FirstOrDefault(m => m.PersonId == r.PersonId && !m.IsTeacher) is { } m2 ? classNames.GetValueOrDefault(m2.ClassId, UnassignedName) : UnassignedName,
              }).ToList()
            : Of(false);
        return new ClassDetail(c, Of(true), students,
            roster.Where(r => !placed.Contains(r.PersonId) && CanTeach(r, c.IsNewcomer)).Select(P).ToList(),
            c.IsNewcomer ? [] : roster.Where(r => !placed.Contains(r.PersonId) && IsStudent(r)).Select(P).ToList());
    }

    /// <summary>정규 반 만들기. "새친구반" · "미배정반" 이름은 기본 반이라 쓸 수 없다.</summary>
    public async Task<(ClassError Error, int Id)> CreateAsync(string? name, CancellationToken ct = default)
    {
        var n = Clean(name);
        if (n.Length == 0) return (ClassError.NoName, 0);
        await using var ctx = db.Create();
        var term = await TermAsync(ctx, ct);
        if (n == UnassignedName || await ctx.Classes.AnyAsync(c => c.TermId == term && c.Name == n, ct)) return (ClassError.DupName, 0);
        var cls = new SchoolClass { TenantId = tenant.Current.Id, TermId = term, Name = n, CreatedUtc = time.GetUtcNow().UtcDateTime };
        ctx.Classes.Add(cls);
        await ctx.SaveChangesAsync(ct);
        return (ClassError.None, cls.Id);
    }

    public async Task<ClassError> RenameAsync(int classId, string? name, CancellationToken ct = default)
    {
        var n = Clean(name);
        if (n.Length == 0) return ClassError.NoName;
        await using var ctx = db.Create();
        var c = await ctx.Classes.SingleOrDefaultAsync(x => x.Id == classId, ct);
        if (c is null) return ClassError.NotFound;
        if (n == UnassignedName || await ctx.Classes.AnyAsync(x => x.TermId == c.TermId && x.Id != c.Id && x.Name == n, ct)) return ClassError.DupName;
        c.Name = n;
        await ctx.SaveChangesAsync(ct);
        return ClassError.None;
    }

    /// <summary>정규 반 지우기: 든 학생은 미배정반으로 돌아간다. 새친구반은 지울 수 없다.</summary>
    public async Task<ClassError> DeleteAsync(int classId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var c = await ctx.Classes.SingleOrDefaultAsync(x => x.Id == classId, ct);
        if (c is null) return ClassError.NotFound;
        if (c.IsNewcomer) return ClassError.Fixed;
        ctx.ClassMembers.RemoveRange(await ctx.ClassMembers.Where(m => m.ClassId == c.Id).ToListAsync(ct));
        await ctx.SaveChangesAsync(ct);
        ctx.Classes.Remove(c);
        await ctx.SaveChangesAsync(ct);
        return ClassError.None;
    }

    /// <summary>반에 넣기(R-232): 한 반만, 학생은 학생 · 새친구 역할만 정규 반에, 선생님은 일반 반 교사 · 임원 · 새친구반 교사 · 임원 · 관리자.</summary>
    public async Task<ClassError> AddAsync(int classId, IReadOnlyCollection<int> personIds, bool teacher, CancellationToken ct = default)
    {
        if (personIds.Count == 0) return ClassError.NotFound;
        await using var ctx = db.Create();
        var c = await ctx.Classes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == classId, ct);
        if (c is null) return ClassError.NotFound;
        if (!teacher && c.IsNewcomer) return ClassError.NotEligible;   // 새친구는 정규 반(또는 미배정반)에 — 새친구반에는 담당만
        var roster = (await people.RosterAsync(null, null, ct)).ToDictionary(r => r.PersonId);
        var placed = (await ctx.ClassMembers.Where(m => m.TermId == c.TermId).Select(m => m.PersonId).ToListAsync(ct)).ToHashSet();
        foreach (var pid in personIds)
        {
            if (!roster.TryGetValue(pid, out var r)) return ClassError.NotFound;
            if (placed.Contains(pid)) return ClassError.InOtherClass;
            if (teacher ? !CanTeach(r, c.IsNewcomer) : !IsStudent(r)) return ClassError.NotEligible;
        }
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var pid in personIds.Distinct())
            ctx.ClassMembers.Add(new ClassMember { TenantId = tenant.Current.Id, TermId = c.TermId, ClassId = c.Id, PersonId = pid, IsTeacher = teacher, CreatedUtc = now });
        await ctx.SaveChangesAsync(ct);
        return ClassError.None;
    }

    /// <summary>미배정반에서 한꺼번에 배정(사람 → 반). 반을 고르지 않은 사람은 건너뛴다.</summary>
    public async Task<(ClassError Error, int Count)> AssignAsync(IReadOnlyDictionary<int, int> personToClass, CancellationToken ct = default)
    {
        var n = 0;
        foreach (var g in personToClass.Where(kv => kv.Value > 0).GroupBy(kv => kv.Value))
        {
            var e = await AddAsync(g.Key, g.Select(kv => kv.Key).ToList(), teacher: false, ct);
            if (e != ClassError.None) return (e, n);
            n += g.Count();
        }
        return (ClassError.None, n);
    }

    public async Task<ClassError> RemoveAsync(int classId, int personId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var m = await ctx.ClassMembers.SingleOrDefaultAsync(x => x.ClassId == classId && x.PersonId == personId, ct);
        if (m is null) return ClassError.NotFound;
        ctx.ClassMembers.Remove(m);
        await ctx.SaveChangesAsync(ct);
        return ClassError.None;
    }

    // 학생: 역할이 학생이거나 "새친구"(T1020). 선생님: 일반 반은 교사 · 임원, 새친구반은 교사 · 임원 · 부서 관리자(0093 10 · 11, R-232)
    public static bool IsStudent(RosterRow r) => UserRole.KindOf(r.RoleCode) == UserRoleKind.Student || r.RoleCode?.Trim() == NewcomerCode;
    public static bool CanTeach(RosterRow r, bool newcomerClass)
    {
        if (IsStudent(r)) return false;
        var k = UserRole.KindOf(r.RoleCode);
        return k is UserRoleKind.Teacher or UserRoleKind.Officer || (newcomerClass && r.IsAdmin);   // 임원도 일반 반 담임 가능(R-232 고침, 2026-11-15 사용자 "가능은 하다, 필수는 아니다")
    }

    private static string Clean(string? name) { var n = (name ?? "").Trim(); return n.Length > 50 ? n[..50] : n; }
    private static ClassPerson P(RosterRow r) => new(r.PersonId, r.Name, r.RoleLabel, r.Birthday, r.Linked);

    public static string Message(ClassError e) => e switch
    {
        ClassError.NotFound => "찾을 수 없어요. 화면을 새로 고쳐 주세요.",
        ClassError.NoName => "반 이름을 넣어 주세요.",
        ClassError.DupName => "같은 이름의 반이 있어요(새친구반 · 미배정반은 기본 반 이름이에요).",
        ClassError.NotEligible => "이 반에 넣을 수 없는 사람이에요. 일반 반 선생님은 교사 · 임원만, 새친구반 담당은 교사 · 임원 · 관리자만 돼요.",
        ClassError.InOtherClass => "이미 다른 반에 있어요. 그 반에서 먼저 빼 주세요.",
        ClassError.Fixed => "새친구반은 기본 반이라 지울 수 없어요.",
        _ => "",
    };
}
