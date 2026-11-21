using System.Globalization;

namespace ChallengeBible.Web.Services;

/// <summary>교적부 반 한 줄: 반 · 선생님들(이름 · 연락처) · 학생 수.</summary>
public sealed record BookClass(int Id, string Name, bool IsNewcomer, IReadOnlyList<(string Name, string? Phone)> Teachers, int Students);

/// <summary>교적부 처음 화면: 반 목록 · 요약 · 이름 찾기 결과.</summary>
public sealed record BookHome(IReadOnlyList<BookClass> Classes, int Students, int NoTeacher, IReadOnlyList<BookHit> Hits);

public sealed record BookHit(int PersonId, string Name, int ClassId, string ClassName);

/// <summary>반 교적 한 아이: 명부 줄 + 빠진 정보(전화 · 생일).</summary>
public sealed record BookKid(RosterRow Row, string? Note)
{
    public bool NoBirthday => Row.BirthMonth is null || Row.BirthDay is null;
    public bool NoPhone => string.IsNullOrWhiteSpace(Row.Phone1) && string.IsNullOrWhiteSpace(Row.Phone2);
    /// <summary>빠진 정보 글자(생일 · 전화). 없으면 빈 목록.</summary>
    public IReadOnlyList<string> Missing => [.. (NoBirthday ? ["생일"] : Array.Empty<string>()), .. (NoPhone ? ["전화"] : Array.Empty<string>())];
}

public sealed record BookClassPage(BookClass Class, IReadOnlyList<BookKid> Kids, BookClass? Prev, BookClass? Next, int BirthdayThisMonth, int MissingCount);

/// <summary>
/// 교적부(R-361, 2026-11-21 사용자 · 확정 시안 교적부): 유년부 /ChurchRecord 를 옮겼다 — 교사 · 관리자가 전체 반의 아이들 정보(생일 · 전화 · 전화 2)를 반별로 본다.
/// 성별 · 학교 · 주소는 우리 명부에 칸이 없어 뺐다. 고치기는 생일 · 전화만(<see cref="PeopleService.UpdateContactAsync"/>).
/// </summary>
public sealed class RosterBookService(ClassService classes, PeopleService people)
{
    private static readonly StringComparer Ko = StringComparer.Create(CultureInfo.GetCultureInfo("ko-KR"), false);
    public const int HitMax = 50;

    private async Task<List<BookClass>> ClassListAsync(CancellationToken ct)
    {
        var rows = await classes.ClassesAsync(ct);
        var teachers = await classes.TeachersByClassAsync(ct);
        return rows.Select(c => new BookClass(c.Id, c.Name, c.IsNewcomer, teachers.GetValueOrDefault(c.Id) ?? [], c.Students)).ToList();
    }

    public async Task<BookHome> HomeAsync(string? search, CancellationToken ct = default)
    {
        var list = await ClassListAsync(ct);
        var roster = (await people.RosterAsync(null, null, ct)).Where(r => r.LeftOn is null && ClassService.IsStudent(r)).ToList();
        var hits = new List<BookHit>();
        var key = (search ?? "").Trim();
        if (key.Length > 0)
        {
            var where = await classes.StudentClassAsync(ct);
            var names = list.ToDictionary(c => c.Id, c => c.Name);
            hits = roster.Where(r => r.Name.Contains(key, StringComparison.OrdinalIgnoreCase) && where.ContainsKey(r.PersonId))
                .OrderBy(r => r.Name.StartsWith(key, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(r => r.Name, Ko).Take(HitMax)
                .Select(r => new BookHit(r.PersonId, r.Name, where[r.PersonId], names.GetValueOrDefault(where[r.PersonId], "?"))).ToList();
        }
        return new BookHome(list, roster.Count, list.Count(c => c.Teachers.Count == 0), hits);
    }

    public async Task<BookClassPage?> ClassAsync(int classId, DateOnly today, CancellationToken ct = default)
    {
        var list = await ClassListAsync(ct);
        var idx = list.FindIndex(c => c.Id == classId);
        if (idx < 0 || await classes.DetailAsync(classId, ct) is not { } detail) return null;
        var roster = (await people.RosterAsync(null, null, ct)).Where(r => r.LeftOn is null).ToDictionary(r => r.PersonId);
        var kids = detail.Students.Where(s => roster.ContainsKey(s.PersonId)).OrderBy(s => s.Name, Ko).Select(s => new BookKid(roster[s.PersonId], s.Note)).ToList();
        return new BookClassPage(list[idx], kids, idx > 0 ? list[idx - 1] : null, idx < list.Count - 1 ? list[idx + 1] : null,
            kids.Count(k => k.Row.BirthMonth == today.Month), kids.Count(k => k.Missing.Count > 0));
    }
}
