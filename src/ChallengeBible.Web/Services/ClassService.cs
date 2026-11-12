using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record TermRow(int Id, int Year, DateOnly StartDate, bool IsCurrent, bool IsFuture);
public sealed record ClassRow(int Id, string Name, bool IsNewcomer, IReadOnlyList<string> Teachers, int Students);
public sealed record ClassPerson(int PersonId, string Name, string RoleLabel, string Birthday, bool Linked);
public sealed record ClassDetail(SchoolClass Class, TermRow Term, List<ClassPerson> Teachers, List<ClassPerson> Students,
    List<ClassPerson> TeacherCandidates, List<ClassPerson> StudentCandidates);

public enum ClassError { None, NotFound, NoName, DupName, OneNewcomer, NotEligible, InOtherClass, YearExists, BadDate }

/// <summary>
/// 조직 · 반(ADR 0093 일정 · 조직 · 반, R-231 ~ R-233). 반은 해마다 새로 짜고, 관리자가 정한 날부터 그해 반을 쓴다.
/// 학생 · 선생님은 한 해에 한 반에만 든다. 일반 반 선생님은 교사만, 새친구반 담당은 교사 · 임원 · 부서 관리자.
/// 부서 관리자만 부른다 — 화면이 먼저 검사한다.
/// </summary>
public sealed class ClassService(IAppDb db, PeopleService people, TenantContext tenant, TimeProvider time)
{
    private DateOnly Today => TenantClock.ToTenantDate(time.GetUtcNow(), tenant.Current.TimeZoneId);

    /// <summary>해 목록. 지금 해 = 시작일이 오늘 이전인 것 중 가장 늦은 것(R-231).</summary>
    public async Task<List<TermRow>> TermsAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return ToRows(await ctx.ClassTerms.AsNoTracking().OrderBy(t => t.Year).ToListAsync(ct), Today);
    }

    public static List<TermRow> ToRows(List<ClassTerm> terms, DateOnly today)
    {
        var current = terms.Where(t => t.StartDate <= today).OrderByDescending(t => t.StartDate).FirstOrDefault()
                      ?? terms.OrderBy(t => t.StartDate).FirstOrDefault();
        return terms.Select(t => new TermRow(t.Id, t.Year, t.StartDate, t == current, t.StartDate > today && t != current)).ToList();
    }

    public async Task<List<ClassRow>> ClassesAsync(int termId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var classes = await ctx.Classes.AsNoTracking().Where(c => c.TermId == termId).ToListAsync(ct);
        var members = await ctx.ClassMembers.AsNoTracking().Where(m => m.TermId == termId).ToListAsync(ct);
        var names = (await people.RosterAsync(null, null, ct)).ToDictionary(r => r.PersonId, r => r.Name);
        var ko = StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false);
        return classes.OrderByDescending(c => c.IsNewcomer).ThenBy(c => c.Name, ko)
            .Select(c => new ClassRow(c.Id, c.Name, c.IsNewcomer,
                members.Where(m => m.ClassId == c.Id && m.IsTeacher).Select(m => names.GetValueOrDefault(m.PersonId, "?")).ToList(),
                members.Count(m => m.ClassId == c.Id && !m.IsTeacher))).ToList();
    }

    /// <summary>반 없는 학생(R-232): 학생(또는 새친구 역할)인데 이 해 어느 반에도 없는 사람.</summary>
    public async Task<List<ClassPerson>> UnassignedAsync(int? termId, CancellationToken ct = default)
    {
        var roster = await people.RosterAsync(null, null, ct);
        HashSet<int> placed = [];
        if (termId is int id)
        {
            await using var ctx = db.Create();
            placed = (await ctx.ClassMembers.AsNoTracking().Where(m => m.TermId == id).Select(m => m.PersonId).ToListAsync(ct)).ToHashSet();
        }
        return roster.Where(r => IsStudent(r) && !placed.Contains(r.PersonId)).Select(P).ToList();
    }

    public async Task<ClassDetail?> DetailAsync(int classId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var c = await ctx.Classes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == classId, ct);
        if (c is null) return null;
        var term = ToRows(await ctx.ClassTerms.AsNoTracking().ToListAsync(ct), Today).Single(t => t.Id == c.TermId);
        var members = await ctx.ClassMembers.AsNoTracking().Where(m => m.TermId == c.TermId).ToListAsync(ct);
        var roster = await people.RosterAsync(null, null, ct);
        var placed = members.Select(m => m.PersonId).ToHashSet();
        var mine = members.Where(m => m.ClassId == c.Id).ToList();
        List<ClassPerson> Of(bool teacher) => roster.Where(r => mine.Any(m => m.PersonId == r.PersonId && m.IsTeacher == teacher)).Select(P).ToList();
        return new ClassDetail(c, term, Of(true), Of(false),
            roster.Where(r => !placed.Contains(r.PersonId) && CanTeach(r, c.IsNewcomer)).Select(P).ToList(),
            c.IsNewcomer ? [] : roster.Where(r => !placed.Contains(r.PersonId) && IsStudent(r)).Select(P).ToList());
    }

    /// <summary>반 만들기. 해가 하나도 없으면 올해 묶음(오늘부터 사용)을 함께 만든다.</summary>
    public async Task<(ClassError Error, int Id)> CreateAsync(int? termId, string? name, bool newcomer, CancellationToken ct = default)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return (ClassError.NoName, 0);
        if (n.Length > 50) n = n[..50];
        await using var ctx = db.Create();
        var now = time.GetUtcNow().UtcDateTime;
        ClassTerm? term = termId is int id ? await ctx.ClassTerms.SingleOrDefaultAsync(t => t.Id == id, ct) : null;
        if (term is null)
        {
            if (termId is not null || await ctx.ClassTerms.AnyAsync(ct)) return (ClassError.NotFound, 0);
            term = new ClassTerm { TenantId = tenant.Current.Id, Year = Today.Year, StartDate = Today, CreatedUtc = now };
            ctx.ClassTerms.Add(term);
            await ctx.SaveChangesAsync(ct);
        }
        var same = await ctx.Classes.Where(c => c.TermId == term.Id).ToListAsync(ct);
        if (same.Any(c => c.Name == n)) return (ClassError.DupName, 0);
        if (newcomer && same.Any(c => c.IsNewcomer)) return (ClassError.OneNewcomer, 0);
        var cls = new SchoolClass { TenantId = tenant.Current.Id, TermId = term.Id, Name = n, IsNewcomer = newcomer, CreatedUtc = now };
        ctx.Classes.Add(cls);
        await ctx.SaveChangesAsync(ct);
        return (ClassError.None, cls.Id);
    }

    public async Task<ClassError> RenameAsync(int classId, string? name, CancellationToken ct = default)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return ClassError.NoName;
        if (n.Length > 50) n = n[..50];
        await using var ctx = db.Create();
        var c = await ctx.Classes.SingleOrDefaultAsync(x => x.Id == classId, ct);
        if (c is null) return ClassError.NotFound;
        if (await ctx.Classes.AnyAsync(x => x.TermId == c.TermId && x.Id != c.Id && x.Name == n, ct)) return ClassError.DupName;
        c.Name = n;
        await ctx.SaveChangesAsync(ct);
        return ClassError.None;
    }

    /// <summary>반 지우기: 든 사람은 "반 없음"으로 돌아간다.</summary>
    public async Task<ClassError> DeleteAsync(int classId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var c = await ctx.Classes.SingleOrDefaultAsync(x => x.Id == classId, ct);
        if (c is null) return ClassError.NotFound;
        ctx.ClassMembers.RemoveRange(await ctx.ClassMembers.Where(m => m.ClassId == c.Id).ToListAsync(ct));
        await ctx.SaveChangesAsync(ct);
        ctx.Classes.Remove(c);
        await ctx.SaveChangesAsync(ct);
        return ClassError.None;
    }

    /// <summary>반에 넣기(R-232): 한 해 한 반, 학생은 학생 · 새친구 역할만, 선생님은 일반 반 교사만 · 새친구반 교사 · 임원 · 관리자.</summary>
    public async Task<ClassError> AddAsync(int classId, IReadOnlyCollection<int> personIds, bool teacher, CancellationToken ct = default)
    {
        if (personIds.Count == 0) return ClassError.NotFound;
        await using var ctx = db.Create();
        var c = await ctx.Classes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == classId, ct);
        if (c is null) return ClassError.NotFound;
        if (!teacher && c.IsNewcomer) return ClassError.NotEligible;   // 새친구는 실제 반에 넣는다(새친구반에는 담당만)
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

    public async Task<ClassError> RemoveAsync(int classId, int personId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var m = await ctx.ClassMembers.SingleOrDefaultAsync(x => x.ClassId == classId && x.PersonId == personId, ct);
        if (m is null) return ClassError.NotFound;
        ctx.ClassMembers.Remove(m);
        await ctx.SaveChangesAsync(ct);
        return ClassError.None;
    }

    /// <summary>새해 반 만들기(R-231): 지난해 반 이름 · 사람을 그대로 복사하고 "이날부터 사용"을 정한다. 명부에서 빠진 사람은 오지 않는다.</summary>
    public async Task<(ClassError Error, int TermId)> CopyAsync(int fromTermId, DateOnly start, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var from = await ctx.ClassTerms.AsNoTracking().SingleOrDefaultAsync(t => t.Id == fromTermId, ct);
        if (from is null) return (ClassError.NotFound, 0);
        if (await ctx.ClassTerms.AnyAsync(t => t.Year == from.Year + 1, ct)) return (ClassError.YearExists, 0);
        if (start <= from.StartDate) return (ClassError.BadDate, 0);
        var now = time.GetUtcNow().UtcDateTime;
        var me = tenant.Current.Id;
        var term = new ClassTerm { TenantId = me, Year = from.Year + 1, StartDate = start, CreatedUtc = now };
        ctx.ClassTerms.Add(term);
        await ctx.SaveChangesAsync(ct);
        var classes = await ctx.Classes.AsNoTracking().Where(c => c.TermId == from.Id).ToListAsync(ct);
        var members = await ctx.ClassMembers.AsNoTracking().Where(m => m.TermId == from.Id).ToListAsync(ct);
        foreach (var c in classes)
        {
            var copy = new SchoolClass { TenantId = me, TermId = term.Id, Name = c.Name, IsNewcomer = c.IsNewcomer, CreatedUtc = now };
            ctx.Classes.Add(copy);
            await ctx.SaveChangesAsync(ct);
            foreach (var m in members.Where(m => m.ClassId == c.Id))
                ctx.ClassMembers.Add(new ClassMember { TenantId = me, TermId = term.Id, ClassId = copy.Id, PersonId = m.PersonId, IsTeacher = m.IsTeacher, CreatedUtc = now });
        }
        await ctx.SaveChangesAsync(ct);
        return (ClassError.None, term.Id);
    }

    /// <summary>"이날부터 사용" 바꾸기. 지난해보다 늦고 다음 해보다 이른 날이어야 한다.</summary>
    public async Task<ClassError> SetStartAsync(int termId, DateOnly start, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var t = await ctx.ClassTerms.SingleOrDefaultAsync(x => x.Id == termId, ct);
        if (t is null) return ClassError.NotFound;
        if (await ctx.ClassTerms.AnyAsync(x => (x.Year < t.Year && x.StartDate >= start) || (x.Year > t.Year && x.StartDate <= start), ct)) return ClassError.BadDate;
        t.StartDate = start;
        await ctx.SaveChangesAsync(ct);
        return ClassError.None;
    }

    // 학생: 역할이 학생이거나 "새친구"(T1020). 선생님: 일반 반은 교사, 새친구반은 교사 · 임원 · 부서 관리자(0093 10 · 11)
    public const string NewcomerCode = "T1020";
    public static bool IsStudent(RosterRow r) => UserRole.KindOf(r.RoleCode) == UserRoleKind.Student || r.RoleCode?.Trim() == NewcomerCode;
    public static bool CanTeach(RosterRow r, bool newcomerClass)
    {
        if (IsStudent(r)) return false;
        var k = UserRole.KindOf(r.RoleCode);
        return k == UserRoleKind.Teacher || (newcomerClass && (k == UserRoleKind.Officer || r.IsAdmin));
    }

    private static ClassPerson P(RosterRow r) => new(r.PersonId, r.Name, r.RoleLabel, r.Birthday, r.Linked);

    public static string Message(ClassError e) => e switch
    {
        ClassError.NotFound => "찾을 수 없어요. 화면을 새로 고쳐 주세요.",
        ClassError.NoName => "반 이름을 넣어 주세요.",
        ClassError.DupName => "같은 해에 같은 이름의 반이 있어요.",
        ClassError.OneNewcomer => "새친구반은 한 해에 하나만 만들 수 있어요.",
        ClassError.NotEligible => "이 반에 넣을 수 없는 사람이에요. 일반 반 선생님은 교사만, 새친구반 담당은 교사 · 임원 · 관리자만 돼요.",
        ClassError.InOtherClass => "이미 다른 반에 있어요. 그 반에서 먼저 빼 주세요.",
        ClassError.YearExists => "다음 해 반이 이미 있어요.",
        ClassError.BadDate => "시작 날짜가 앞뒤 해와 겹쳐요. 다른 날을 골라 주세요.",
        _ => "",
    };
}
