using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>명부 한 줄(화면용). 연결된 줄은 UserName · DeptRoleId 가 차 있다.</summary>
public sealed record RosterRow(int PersonId, string Name, string? RoleCode, string RoleLabel, byte? BirthMonth, byte? BirthDay,
    string? Phone1, string? Phone2, int? DeptRoleId, string? UserName, string? Email, bool IsAdmin, DateTime CreatedUtc)
{
    public bool Linked => UserName is not null;
    /// <summary>꼬리표 한 줄(이전 부서 · 반 · 담임 · 전화). 반 이동 전이면 null.</summary>
    public string? Prev { get; init; }
    public string Birthday => PeopleService.BirthdayText(BirthMonth, BirthDay);
}

/// <summary>명부에 넣고 고치는 값. 연결된 줄은 생일 · 연락처만 쓴다.</summary>
public sealed record PersonInput(string? Name, string? RoleCode, int? BirthMonth, int? BirthDay, string? Phone1, string? Phone2);

/// <summary>승인할 때 고르는 명부 후보(같은 이름, 연결 안 된 줄).</summary>
public sealed record PersonCandidate(int PersonId, string Name, string Birthday, string? Phone1, DateTime CreatedUtc);

public enum PeopleError { None, NotFound, NoName, BadBirthday, BadPhone, Linked, NotLinked }

/// <summary>
/// 부서 명부(ADR 0093 사람 · 소속, R-226 ~ R-230). 사람과 계정을 나눈다: 명부 한 줄에 계정을 잇기만 하고 기록은 옮기지 않는다.
/// 올리기 · 고치기는 부서 관리자 · 교사, 지우기 · 연결 풀기는 부서 관리자만 — 부르는 화면이 먼저 검사한다.
/// </summary>
public sealed class PeopleService(IAppDb db, TenantContext tenant, TimeProvider time)
{
    public async Task<List<RosterRow>> RosterAsync(string? query, string? role, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        await EnsureAsync(ctx, ct);
        var codes = await ctx.CodeData.AsNoTracking().Where(c => c.MajorCD == CodeData.RoleMajor && c.MinorCD != null)
            .ToDictionaryAsync(c => c.MinorCD!, c => c.MinorNM ?? c.MinorCD!, ct);
        var adminIds = await DeptMembership.AdminIdsAsync(ctx, ct);
        var people = await ctx.People.AsNoTracking().ToListAsync(ct);
        var linked = (await (from d in ctx.DeptRoles join u in ctx.Users on d.UserId equals u.Id
                             select new { d.Id, d.UserId, d.RoleCode, u.UserName, u.LastName, u.FirstName, u.Email }).ToListAsync(ct))
            .ToDictionary(x => x.UserId!);
        var rows = people.Select(p =>
        {
            var m = p.UserId is null ? null : linked.GetValueOrDefault(p.UserId);
            var code = m is null ? p.RoleCode : m.RoleCode;
            var name = m is null ? p.Name : AppUser.FullName(m.LastName, m.FirstName);
            return new RosterRow(p.Id, name, code, Label(code, codes), p.BirthMonth, p.BirthDay, p.Phone1, p.Phone2,
                m?.Id, m?.UserName, m?.Email, m is not null && adminIds.Contains(m.UserId!), p.CreatedUtc) { Prev = PrevText(p) };
        }).ToList();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var s = query.Trim();
            var digits = Digits(s);
            rows = rows.Where(r => r.Name.Contains(s) || (r.UserName ?? "").Contains(s, StringComparison.OrdinalIgnoreCase)
                || (r.Email ?? "").Contains(s, StringComparison.OrdinalIgnoreCase)
                || (digits.Length >= 3 && (Digits(r.Phone1).Contains(digits) || Digits(r.Phone2).Contains(digits)))).ToList();
        }
        if (!string.IsNullOrWhiteSpace(role)) rows = rows.Where(r => (r.RoleCode ?? "") == (role == "-" ? "" : role)).ToList();
        var ko = StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false);
        return rows.OrderBy(r => r.Name, ko).ToList();
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        await EnsureAsync(ctx, ct);
        return await ctx.People.CountAsync(ct);
    }

    /// <summary>소속은 있는데 명부 줄이 없는 계정(예: 이 기능 전에 들어온 사람)에 줄을 만든다. 임시 부서에는 명부가 없다.</summary>
    private async Task EnsureAsync(AppDbContext ctx, CancellationToken ct)
    {
        if (tenant.IsLobby) return;
        var missing = await (from d in ctx.DeptRoles
                             join u in ctx.Users on d.UserId equals u.Id
                             where !ctx.People.Any(p => p.UserId == d.UserId)
                             select new { u.Id, u.LastName, u.FirstName, d.InsertDT }).ToListAsync(ct);
        if (missing.Count == 0) return;
        foreach (var m in missing)
            ctx.People.Add(new Person { TenantId = tenant.Current.Id, Name = Cut(AppUser.FullName(m.LastName, m.FirstName)), UserId = m.Id, CreatedUtc = m.InsertDT });
        await ctx.SaveChangesAsync(ct);
    }

    /// <summary>명부에 올리기(부서 관리자 · 교사, R-227). 가입하지 않은 사람 · 새친구.</summary>
    public async Task<(PeopleError Error, int Id)> AddAsync(string actor, PersonInput input, CancellationToken ct = default)
    {
        var (e, name, bm, bd, p1, p2) = Check(input, needName: true);
        if (e != PeopleError.None) return (e, 0);
        await using var ctx = db.Create();
        var p = new Person
        {
            TenantId = tenant.Current.Id, Name = name!, RoleCode = Clean(input.RoleCode), BirthMonth = bm, BirthDay = bd, Phone1 = p1, Phone2 = p2,
            CreatedUtc = time.GetUtcNow().UtcDateTime, CreatedByUserName = actor,
        };
        ctx.People.Add(p);
        await ctx.SaveChangesAsync(ct);
        return (PeopleError.None, p.Id);
    }

    /// <summary>고치기(부서 관리자 · 교사). 연결된 줄은 생일 · 연락처만 — 이름은 계정, 역할은 소속에서 바꾼다.</summary>
    public async Task<PeopleError> UpdateAsync(int id, PersonInput input, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var p = await ctx.People.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return PeopleError.NotFound;
        var (e, name, bm, bd, p1, p2) = Check(input, needName: p.UserId is null);
        if (e != PeopleError.None) return e;
        if (p.UserId is null) { p.Name = name!; p.RoleCode = Clean(input.RoleCode); }
        (p.BirthMonth, p.BirthDay, p.Phone1, p.Phone2) = (bm, bd, p1, p2);
        await ctx.SaveChangesAsync(ct);
        return PeopleError.None;
    }

    /// <summary>명부에서 지우기(부서 관리자만, R-227). 계정과 연결된 줄은 지우지 않는다(먼저 연결을 푼다).</summary>
    public async Task<PeopleError> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var p = await ctx.People.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return PeopleError.NotFound;
        if (p.UserId is not null) return PeopleError.Linked;
        ctx.ClassMembers.RemoveRange(await ctx.ClassMembers.Where(m => m.PersonId == p.Id).ToListAsync(ct));   // 반에서도 빠진다
        ctx.AttendanceChecks.RemoveRange(await ctx.AttendanceChecks.Where(a => a.PersonId == p.Id).ToListAsync(ct));   // 출석 기록도 지운다
        ctx.People.Remove(p);
        await ctx.SaveChangesAsync(ct);
        return PeopleError.None;
    }

    /// <summary>
    /// 연결 풀기(부서 관리자만, R-229): 이 줄은 "가입 안 함"으로 돌아가고(생일 · 연락처 · 그때 역할 그대로),
    /// 계정은 새 명부 줄을 받는다. 소속 · 미션 기록은 그대로다.
    /// </summary>
    public async Task<PeopleError> UnlinkAsync(string actor, int id, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var p = await ctx.People.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return PeopleError.NotFound;
        if (p.UserId is null) return PeopleError.NotLinked;
        var member = await (from d in ctx.DeptRoles join u in ctx.Users on d.UserId equals u.Id where d.UserId == p.UserId
                            select new { d.RoleCode, u.LastName, u.FirstName }).FirstOrDefaultAsync(ct);
        var userId = p.UserId;
        p.UserId = null;
        p.RoleCode = member?.RoleCode;
        await ctx.SaveChangesAsync(ct);
        if (member is not null)
        {
            ctx.People.Add(new Person { TenantId = tenant.Current.Id, Name = Cut(AppUser.FullName(member.LastName, member.FirstName)), UserId = userId,
                CreatedUtc = time.GetUtcNow().UtcDateTime, CreatedByUserName = actor });
            await ctx.SaveChangesAsync(ct);
        }
        return PeopleError.None;
    }

    /// <summary>승인할 때 보일 명부 후보: 이름이 같고(띄어쓰기 무시) 아직 계정과 잇지 않은 줄(R-228).</summary>
    public async Task<Dictionary<string, List<PersonCandidate>>> CandidatesAsync(IEnumerable<string> names, CancellationToken ct = default)
    {
        var keys = names.Select(Key).Where(k => k.Length > 0).ToHashSet();
        if (keys.Count == 0 || tenant.IsLobby) return [];
        await using var ctx = db.Create();
        var free = await ctx.People.AsNoTracking().Where(p => p.UserId == null).OrderBy(p => p.Id).ToListAsync(ct);
        return free.Where(p => keys.Contains(Key(p.Name))).GroupBy(p => Key(p.Name))
            .ToDictionary(g => g.Key, g => g.Select(p => new PersonCandidate(p.Id, p.Name, BirthdayText(p.BirthMonth, p.BirthDay), p.Phone1, p.CreatedUtc)).ToList());
    }

    public static string Key(string? name) => new string((name ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());

    /// <summary>
    /// 소속을 바꿀 때 명부도 맞춘다(DeptMembership.PlaceAsync 에서, 시스템 연결). 저장은 부르는 쪽에서.
    /// 받는 부서에 이 계정의 줄이 없으면 고른 줄(linkPersonId)을 잇거나 새 줄을 만든다(R-228).
    /// 다른 부서에서 옮겨 오면 그 부서의 줄은 지우고 빈 칸만 받는 줄로 옮긴다(진급, R-230). 임시 부서로 갈 때는 옛 줄의 연결만 푼다.
    /// </summary>
    public static async Task PlaceAsync(AppDbContext sys, AppUser user, int target, string? oldRoleCode, int? linkPersonId, string? actor, DateTime now, CancellationToken ct)
    {
        var lobbies = await sys.Tenants.Where(t => t.IsLobby).Select(t => t.Id).ToListAsync(ct);
        var mine = await sys.People.Where(p => p.UserId == user.Id).ToListAsync(ct);
        var old = mine.Where(p => p.TenantId != target).ToList();
        if (lobbies.Contains(target))
        {
            foreach (var p in old) { p.UserId = null; p.RoleCode = oldRoleCode; }
            return;
        }
        var here = mine.FirstOrDefault(p => p.TenantId == target);
        if (here is null)
        {
            here = linkPersonId is int pid ? await sys.People.SingleOrDefaultAsync(p => p.TenantId == target && p.Id == pid && p.UserId == null, ct) : null;
            if (here is null)
            {
                here = new Person { TenantId = target, Name = Cut(AppUser.FullName(user.LastName, user.FirstName)), CreatedUtc = now, CreatedByUserName = actor };
                sys.People.Add(here);
            }
            here.UserId = user.Id;
            here.RoleCode = null;
        }
        foreach (var o in old)
        {
            // 생일 · 연락처 · 새친구 · 꼬리표가 따라간다
            CarryOver(o, here);
            // 옛 부서의 반에서도 빠진다(R-232). 반은 부서마다 따로라 옮기지 않는다
            sys.ClassMembers.RemoveRange(await sys.ClassMembers.Where(m => m.TenantId == o.TenantId && m.PersonId == o.Id).ToListAsync(ct));
            // 출석 기록은 새 부서 줄로 옮긴다(0093 사람 · 소속 9, R-230)
            if (here.Id == 0) await sys.SaveChangesAsync(ct);
            await MoveChecksAsync(sys, o, here, ct);
            sys.People.Remove(o);
        }
    }

    // ── 값 검사 ──

    public static (PeopleError Error, string? Name, byte? Month, byte? Day, string? Phone1, string? Phone2) Check(PersonInput input, bool needName)
    {
        var name = Cut((input.Name ?? "").Trim());
        if (needName && name.Length == 0) return (PeopleError.NoName, null, null, null, null, null);
        if (!Birthday(input.BirthMonth, input.BirthDay, out var bm, out var bd)) return (PeopleError.BadBirthday, null, null, null, null, null);
        if (!Phone(input.Phone1, out var p1) || !Phone(input.Phone2, out var p2)) return (PeopleError.BadPhone, null, null, null, null, null);
        return (PeopleError.None, name, bm, bd, p1, p2);
    }

    /// <summary>생일은 월 · 일 둘 다 비거나 둘 다 있다. 2월 29일은 된다.</summary>
    public static bool Birthday(int? month, int? day, out byte? m, out byte? d)
    {
        (m, d) = (null, null);
        if (month is null && day is null) return true;
        if (month is not int mm || day is not int dd || mm is < 1 or > 12 || dd < 1 || dd > DateTime.DaysInMonth(2024, mm)) return false;
        (m, d) = ((byte)mm, (byte)dd);
        return true;
    }

    /// <summary>연락처: 숫자 · 하이픈 · + · 괄호 · 띄어쓰기만, 숫자 8 ~ 15개. 빈 값은 없음.</summary>
    public static bool Phone(string? raw, out string? phone)
    {
        phone = null;
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return true;
        if (s.Any(c => !(char.IsAsciiDigit(c) || c is '-' or '+' or '(' or ')' or ' '))) return false;
        var n = s.Count(char.IsAsciiDigit);
        if (n is < 8 or > 15) return false;
        s = string.Join("-", s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (s.Length > 20) return false;
        phone = s;
        return true;
    }

    /// <summary>"이전: 유년부 3학년5반 · 담임 정교사 010-1234-5678"(ADR 0093 반별 학생 이동 2).</summary>
    public static string? PrevText(Person p)
    {
        if (p.PrevDeptName is null && p.PrevClassName is null) return null;
        var t = $"이전: {string.Join(" ", new[] { p.PrevDeptName, p.PrevClassName }.Where(s => !string.IsNullOrEmpty(s)))}";
        var who = string.Join(" ", new[] { p.PrevTeacherName, p.PrevTeacherPhone }.Where(s => !string.IsNullOrEmpty(s)));
        return who.Length == 0 ? t : $"{t} · 담임 {who}";
    }

    /// <summary>타부서로 갈 때 명부 줄에 따라가는 값(생일 · 연락처 · 새친구 · 꼬리표).</summary>
    public static void CarryOver(Person from, Person to)
    {
        if (to.BirthMonth is null) (to.BirthMonth, to.BirthDay) = (from.BirthMonth, from.BirthDay);
        to.Phone1 ??= from.Phone1;
        to.Phone2 ??= from.Phone2;
        to.NewcomerFirstVisit ??= from.NewcomerFirstVisit;
        to.NewcomerVoluntary ??= from.NewcomerVoluntary;
        to.NewcomerEvangelist ??= from.NewcomerEvangelist;
        if (from.PrevDeptName is not null || from.PrevClassName is not null)
            (to.PrevDeptName, to.PrevClassName, to.PrevTeacherName, to.PrevTeacherPhone) = (from.PrevDeptName, from.PrevClassName, from.PrevTeacherName, from.PrevTeacherPhone);
    }

    /// <summary>한 사람의 출석 기록을 다른 부서 명부 줄로 옮긴다(겹치는 주일은 건너뛴다). 저장은 부르는 쪽에서 — to.Id 는 저장된 값이어야 한다.</summary>
    public static async Task MoveChecksAsync(AppDbContext sys, Person from, Person to, CancellationToken ct)
    {
        var checks = await sys.AttendanceChecks.Where(a => a.TenantId == from.TenantId && a.PersonId == from.Id).ToListAsync(ct);
        if (checks.Count == 0) return;
        var have = (await sys.AttendanceChecks.Where(a => a.TenantId == to.TenantId && a.PersonId == to.Id).Select(a => a.Sunday).ToListAsync(ct)).ToHashSet();
        foreach (var a in checks.Where(a => !have.Contains(a.Sunday)))
            sys.AttendanceChecks.Add(new AttendanceCheck { TenantId = to.TenantId, PersonId = to.Id, Sunday = a.Sunday, CreatedUtc = a.CreatedUtc, CreatedByUserName = a.CreatedByUserName });
        sys.AttendanceChecks.RemoveRange(checks);
    }

    public static string BirthdayText(byte? m, byte? d) => m is null || d is null ? "생일 없음" : $"{m}월 {d}일";

    public static string Message(PeopleError e) => e switch
    {
        PeopleError.NotFound => "명부에서 찾을 수 없어요.",
        PeopleError.NoName => "이름을 넣어 주세요.",
        PeopleError.BadBirthday => "생일의 월 · 일을 둘 다 맞게 골라 주세요.",
        PeopleError.BadPhone => "연락처는 숫자와 - 로 넣어 주세요(예: 010-1234-5678).",
        PeopleError.Linked => "가입한 사람과 연결된 줄은 지울 수 없어요.",
        PeopleError.NotLinked => "연결된 계정이 없어요.",
        _ => "",
    };

    private static string Digits(string? s) => new string((s ?? "").Where(char.IsAsciiDigit).ToArray());
    private static string Cut(string s) => s.Length > 50 ? s[..50] : s;
    private static string? Clean(string? code) => string.IsNullOrWhiteSpace(code) ? null : code.Trim();

    private static string Label(string? code, Dictionary<string, string> names) =>
        string.IsNullOrWhiteSpace(code) ? UserRole.LabelOf(UserRoleKind.Parent) : names.GetValueOrDefault(code.Trim(), UserRole.LabelOf(UserRole.KindOf(code)));
}
