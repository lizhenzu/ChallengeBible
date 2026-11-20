using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ChallengeBible.Web.Services;

/// <summary>명부 같은 이름 후보 한 줄: 가린 힌트만 보인다(R-341). 전화도 생일도 없으면 고를 수 없다.</summary>
public sealed record RosterCandidate(int PersonId, int TenantId, string Dept, string? PhoneHint, string? BirthHint)
{
    public bool CanVerify => PhoneHint is not null || BirthHint is not null;
}

public enum LinkError { None, NotFound, Locked, Wrong, NeedInput, AlreadyMember }

/// <summary>
/// 가입한 뒤 명부 연결(ADR 0119, R-341): 모든 부서 명부에서 내 이름과 같은 · 아직 계정 없는 줄을 찾아 가린 힌트로 보이고,
/// 본인이 고르면 명부에 있는 정보(전화 끝 4자리 · 생일)로 확인한다. 맞으면 그 줄에 계정을 잇고 그 부서 · 명부의 역할로 바로 들어간다.
/// 5번 틀리면 30분 막는다.
/// </summary>
public sealed class RosterLinkService(ISystemDb systemDb, IMemoryCache cache, TimeProvider time)
{
    public const int MaxTries = 5;
    public static readonly TimeSpan LockFor = TimeSpan.FromMinutes(30);

    private static string Key(string userName) => "roster-link:" + userName;

    public bool IsLocked(string userName) => cache.TryGetValue(Key(userName), out int n) && n >= MaxTries;

    public int TriesLeft(string userName) => MaxTries - (cache.TryGetValue(Key(userName), out int n) ? n : 0);

    /// <summary>"●●34" — 전화 끝 두 자리만.</summary>
    public static string? PhoneHint(Person p)
    {
        var d = AccountService.PhoneDigits(p.Phone1) ?? AccountService.PhoneDigits(p.Phone2);
        return d is { Length: >= 4 } ? "●●" + d[^2..] : null;
    }

    /// <summary>"●월 ●5일" — 날의 끝자리만.</summary>
    public static string? BirthHint(Person p) =>
        p is { BirthMonth: > 0, BirthDay: > 0 } ? $"●월 ●{p.BirthDay % 10}일" : null;

    private static IQueryable<Person> Open(AppDbContext sys, string name) =>
        from p in sys.People.IgnoreQueryFilters()
        join t in sys.Tenants on p.TenantId equals t.Id
        where p.Name == name && p.UserId == null && p.LeftOn == null && !t.IsLobby && t.Status == TenantStatus.Active
        select p;

    /// <summary>내 이름과 같은 명부 줄들. 이미 정식 부서에 있으면 없다.</summary>
    public async Task<IReadOnlyList<RosterCandidate>> CandidatesAsync(string userName, CancellationToken ct = default)
    {
        await using var sys = systemDb.Create(userName);
        var me = await sys.Users.AsNoTracking().SingleOrDefaultAsync(u => u.UserName == userName, ct);
        if (me is null || await InDeptAsync(sys, me.Id, ct)) return [];
        var name = AppUser.FullName(me.LastName, me.FirstName).Trim();
        if (name.Length == 0) return [];
        var rows = await (from p in Open(sys, name) join t in sys.Tenants on p.TenantId equals t.Id orderby t.Name, p.Id select new { p, t.Name }).ToListAsync(ct);
        return rows.Select(r => new RosterCandidate(r.p.Id, r.p.TenantId, ShortDept(r.Name), PhoneHint(r.p), BirthHint(r.p))).ToList();
    }

    public async Task<(Person? Person, string Dept)> FindAsync(string userName, int personId, CancellationToken ct = default)
    {
        await using var sys = systemDb.Create(userName);
        var me = await sys.Users.AsNoTracking().SingleOrDefaultAsync(u => u.UserName == userName, ct);
        if (me is null) return (null, "");
        var name = AppUser.FullName(me.LastName, me.FirstName).Trim();
        var row = await (from p in Open(sys, name) join t in sys.Tenants on p.TenantId equals t.Id where p.Id == personId select new { p, t.Name }).AsNoTracking().FirstOrDefaultAsync(ct);
        return row is null ? (null, "") : (row.p, ShortDept(row.Name));
    }

    /// <summary>본인 확인 → 맞으면 잇고 그 부서로. 성공하면 부서 주소(Slug)를 돌려준다.</summary>
    public async Task<(LinkError Error, string? Slug, string Dept)> VerifyAsync(string userName, int personId, string? last4, int month, int day, CancellationToken ct = default)
    {
        if (IsLocked(userName)) return (LinkError.Locked, null, "");
        await using var sys = systemDb.Create(userName);
        var me = await sys.Users.SingleOrDefaultAsync(u => u.UserName == userName, ct);
        if (me is null) return (LinkError.NotFound, null, "");
        if (await InDeptAsync(sys, me.Id, ct)) return (LinkError.AlreadyMember, null, "");
        var name = AppUser.FullName(me.LastName, me.FirstName).Trim();
        var p = await Open(sys, name).FirstOrDefaultAsync(x => x.Id == personId, ct);
        if (p is null) return (LinkError.NotFound, null, "");
        var t = await sys.Tenants.AsNoTracking().SingleAsync(x => x.Id == p.TenantId, ct);
        var phones = new[] { p.Phone1, p.Phone2 }.Select(AccountService.PhoneDigits).Where(d => d is { Length: >= 4 }).Select(d => d![^4..]).ToList();
        var hasBirth = p is { BirthMonth: > 0, BirthDay: > 0 };
        if (phones.Count == 0 && !hasBirth) return (LinkError.NotFound, null, "");
        var typed = AccountService.PhoneDigits(last4);
        if ((phones.Count > 0 && typed is null) || (hasBirth && (month == 0 || day == 0))) return (LinkError.NeedInput, null, ShortDept(t.Name));

        var ok = (phones.Count == 0 || phones.Contains(typed!)) && (!hasBirth || (p.BirthMonth == month && p.BirthDay == day));
        if (!ok)
        {
            var n = (cache.TryGetValue(Key(userName), out int c) ? c : 0) + 1;
            cache.Set(Key(userName), n, LockFor);
            return (n >= MaxTries ? LinkError.Locked : LinkError.Wrong, null, ShortDept(t.Name));
        }
        cache.Remove(Key(userName));
        var role = string.IsNullOrEmpty(p.RoleCode) ? UserRole.StudentCode : p.RoleCode;
        var (err, _) = await DeptMembership.PlaceAsync(sys, me, p.TenantId, role, time.GetUtcNow().UtcDateTime, ct, linkPersonId: p.Id, actor: userName);
        return err == MoveError.None ? (LinkError.None, t.Slug, ShortDept(t.Name)) : (LinkError.AlreadyMember, null, ShortDept(t.Name));
    }

    public async Task<int> TenantOfSlugAsync(string slug, CancellationToken ct = default)
    {
        await using var sys = systemDb.Create("system");
        return await sys.Tenants.Where(t => t.Slug == slug).Select(t => t.Id).SingleAsync(ct);
    }

    private static async Task<bool> InDeptAsync(AppDbContext sys, string userId, CancellationToken ct) =>
        await (from d in sys.DeptRoles join t in sys.Tenants on d.TenantId equals t.Id where d.UserId == userId && !t.IsLobby select d).AnyAsync(ct);

    public static string ShortDept(string name)
    {
        var i = name.Trim().LastIndexOf(' ');
        return i <= 0 ? name.Trim() : name[(i + 1)..].Trim();
    }

    public static string Message(LinkError e, int left) => e switch
    {
        LinkError.Locked => $"{MaxTries}번 틀려서 잠시 막혔어요. 30분 뒤에 다시 하거나 부서 관리자에게 이어 달라고 해 주세요.",
        LinkError.Wrong => $"정보가 맞지 않아요. ({left}번 더 할 수 있어요)",
        LinkError.NeedInput => "명부에 있는 정보를 모두 넣어 주세요.",
        LinkError.AlreadyMember => "이미 부서에 함께하고 있어요. 한 사람은 한 부서에만 참여할 수 있어요.",
        _ => "이 명부 줄은 고를 수 없어요. 다시 골라 주세요.",
    };
}
