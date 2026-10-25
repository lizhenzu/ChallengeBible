using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>온가족 구성원 한 명(R-178). Depts = 승인된 소속 부서 이름. Hidden 이면 부서 관리자에게 이름을 가린 사람.</summary>
public sealed record FamilyPerson(int AccountId, string Name, string Relation, IReadOnlyList<string> Depts, int Points, DateOnly? Last)
{
    public bool Hidden { get; init; }
}

/// <summary>온가족 하나와 처음부터 누적 총점(R-177). 저장하지 않고 볼 때 계산한다.</summary>
public sealed record FamilyBoardView(int Id, string Name, IReadOnlyList<FamilyPerson> People)
{
    public int Total => People.Sum(p => p.Points);
    public DateOnly? Last => People.Max(p => p.Last);
    public int DeptCount => People.Where(p => !p.Hidden).SelectMany(p => p.Depts).Distinct().Count();
}

/// <summary>내 계정 > 온가족. AwardDept 는 보호자일 때 시상 부서 이름(정하지 않았으면 null).</summary>
public sealed record MyOnFamily(FamilyBoardView? Family, FamilyRequest? Open, bool IsGuardian, string? AwardDept, DateOnly Today);

public enum OnFamilyError { None, Empty, TooLong, RequestOpen, AlreadyInFamily, NotFound }

/// <summary>
/// 온가족(0065, R-175 ~ R-178) — 사용자 쪽: 내 온가족 보기 · 신청 · 신청 취소, 부서 홈 한 줄, 부서 관리자 보기.
/// 구성원 점수는 여러 부서에 걸쳐 있어 시스템 연결로 읽는다(물리 설계 3-3 "온가족 총점"). 돌려주는 것은 구성원별 합계 · 마지막 점수일 · 부서 이름뿐.
/// 쓰기는 신청 · 취소(내 행)뿐이고 묶기 · 빼기는 플랫폼 관리자 처리기(FamilyAdminService)만 한다.
/// </summary>
public sealed class OnFamilyService(ISystemDb systemDb, TenantClock clock, TimeProvider time)
{
    public const int BodyMax = 500;

    /// <summary>R-93 처럼: 이름이 비면 "{대표}네 온가족", 대표 = 관계가 학생인 첫 구성원(없으면 첫 구성원).</summary>
    public static string DisplayName(string? name, IReadOnlyList<(string Name, string Relation)> people)
    {
        if (!string.IsNullOrWhiteSpace(name)) return name;
        var lead = people.FirstOrDefault(p => p.Relation == FamilyRelation.Student);
        if (lead.Name is null && people.Count > 0) lead = people[0];
        return lead.Name is null ? "온가족" : $"{lead.Name}네 온가족";
    }

    /// <summary>
    /// 구성원별 누적 점수(R-177). 임시 부서(0064)는 승인 때 기록을 복사하고 원본도 남기므로, 멈춘(옮겨 간) 임시 부서 소속의 점수는 빼고 센다.
    /// 참여자 역할을 뺀(비활성) 참여자의 점수는 센다.
    /// </summary>
    internal static async Task<FamilyBoardView?> LoadAsync(AppDbContext sys, int familyId, CancellationToken ct)
    {
        var f = await sys.Families.AsNoTracking().Include(x => x.Links).SingleOrDefaultAsync(x => x.Id == familyId, ct);
        if (f is null) return null;
        var links = f.Links.OrderBy(l => l.SortOrder).ThenBy(l => l.AddedUtc).ToList();
        var ids = links.Select(l => l.AccountId).ToList();
        var names = await sys.Accounts.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.DisplayName, ct);
        var mems = await (from m in sys.Memberships
                          join t in sys.Tenants on m.TenantId equals t.Id
                          where ids.Contains(m.AccountId)
                          select new { m.Id, m.AccountId, m.Status, t.Name, t.IsLobby, Active = t.Status == TenantStatus.Active }).ToListAsync(ct);
        var counted = mems.Where(m => !(m.IsLobby && m.Status != MembershipStatus.Approved)).ToList();
        var memIds = counted.Select(m => m.Id).ToList();
        var parts = await sys.Participants.Where(p => memIds.Contains(p.MembershipId)).Select(p => new { p.Id, p.MembershipId }).ToListAsync(ct);
        var pids = parts.Select(p => p.Id).ToList();
        var sums = await sys.PointLedgers.Where(l => pids.Contains(l.ParticipantId))
            .GroupBy(l => l.ParticipantId).Select(g => new { g.Key, Sum = g.Sum(x => x.Points), Last = g.Max(x => x.MissionDate) }).ToListAsync(ct);
        var owner = counted.ToDictionary(m => m.Id, m => m.AccountId);
        var byAccount = parts.Join(sums, p => p.Id, s => s.Key, (p, s) => new { Account = owner[p.MembershipId], s.Sum, s.Last })
            .GroupBy(x => x.Account).ToDictionary(g => g.Key, g => (Sum: g.Sum(x => x.Sum), Last: g.Max(x => x.Last)));

        var people = links.Select(l =>
        {
            var depts = mems.Where(m => m.AccountId == l.AccountId && m.Status == MembershipStatus.Approved && m.Active).Select(m => m.Name).ToList();
            var got = byAccount.TryGetValue(l.AccountId, out var s) ? s : (Sum: 0, Last: (DateOnly?)null);
            return new FamilyPerson(l.AccountId, names.GetValueOrDefault(l.AccountId, "?"), l.Relation, depts, got.Sum, got.Last);
        }).ToList();
        return new FamilyBoardView(f.Id, DisplayName(f.Name, people.Select(p => (p.Name, p.Relation)).ToList()), people);
    }

    public async Task<MyOnFamily> MyAsync(int accountId, CancellationToken ct = default)
    {
        await using var sys = systemDb.Create(accountId);
        var fid = await sys.FamilyLinks.Where(l => l.AccountId == accountId).Select(l => (int?)l.FamilyId).SingleOrDefaultAsync(ct);
        var board = fid is int id ? await LoadAsync(sys, id, ct) : null;
        var open = board is null
            ? await sys.FamilyRequests.AsNoTracking().SingleOrDefaultAsync(r => r.RequesterAccountId == accountId && r.Status == FamilyRequestStatus.Requested, ct)
            : null;
        var guardian = await sys.Memberships.AnyAsync(m => m.AccountId == accountId && m.Status == MembershipStatus.Approved
            && m.Roles.Any(r => r.Role == Roles.Guardian), ct);
        var award = guardian
            ? await (from a in sys.Accounts join t in sys.Tenants on a.FamilyTenantId equals t.Id where a.Id == accountId select t.Name).SingleOrDefaultAsync(ct)
            : null;
        return new MyOnFamily(board, open, guardian, award, clock.Today(PlatformHomeService.PlatformTimeZone));
    }

    /// <summary>부서 홈 한 줄(R-177): 내 온가족 이름 · 총점. 없으면 null.</summary>
    public async Task<(string Name, int Total)?> LineAsync(int accountId, CancellationToken ct = default)
    {
        await using var sys = systemDb.Create(accountId);
        var fid = await sys.FamilyLinks.Where(l => l.AccountId == accountId).Select(l => (int?)l.FamilyId).SingleOrDefaultAsync(ct);
        if (fid is not int id || await LoadAsync(sys, id, ct) is not { } b) return null;
        return (b.Name, b.Total);
    }

    /// <summary>온가족 신청(R-176). 이미 온가족이 있거나 처리 안 된 신청이 있으면 막는다.</summary>
    public async Task<OnFamilyError> RequestAsync(int accountId, string? body, CancellationToken ct = default)
    {
        var text = body?.Trim() ?? "";
        if (text.Length == 0) return OnFamilyError.Empty;
        if (text.Length > BodyMax) return OnFamilyError.TooLong;
        await using var sys = systemDb.Create(accountId);
        if (await sys.FamilyLinks.AnyAsync(l => l.AccountId == accountId, ct)) return OnFamilyError.AlreadyInFamily;
        if (await sys.FamilyRequests.AnyAsync(r => r.RequesterAccountId == accountId && r.Status == FamilyRequestStatus.Requested, ct)) return OnFamilyError.RequestOpen;
        var now = time.GetUtcNow().UtcDateTime;
        sys.FamilyRequests.Add(new FamilyRequest { RequesterAccountId = accountId, Body = text, RequestedUtc = now });
        sys.AuditLogs.Add(new AuditLog { OccurredUtc = now, ActorAccountId = accountId, Action = "FamilyRequested", Detail = $"account={accountId}" });
        await sys.SaveChangesAsync(ct);
        return OnFamilyError.None;
    }

    /// <summary>내 신청 취소(처리 안 된 것만). 거절과 같은 상태로 닫는다 — 처리자는 본인.</summary>
    public async Task<OnFamilyError> CancelAsync(int accountId, CancellationToken ct = default)
    {
        await using var sys = systemDb.Create(accountId);
        var r = await sys.FamilyRequests.SingleOrDefaultAsync(x => x.RequesterAccountId == accountId && x.Status == FamilyRequestStatus.Requested, ct);
        if (r is null) return OnFamilyError.NotFound;
        r.Status = FamilyRequestStatus.Rejected;
        r.DecidedUtc = time.GetUtcNow().UtcDateTime;
        r.DecidedByAccountId = accountId;
        await sys.SaveChangesAsync(ct);
        return OnFamilyError.None;
    }

    /// <summary>
    /// 부서 관리자 보기(R-178): 이 부서에 승인된 사람이 든 온가족들. 다른 부서 사람은 이름 · 부서를 가린다(Hidden, 점수만).
    /// 키 = 이 부서 계정 Id → 그 사람의 온가족.
    /// </summary>
    public async Task<Dictionary<int, FamilyBoardView>> ForTenantAsync(int actor, int tenantId, CancellationToken ct = default)
    {
        await using var sys = systemDb.Create(actor);
        var here = await sys.Memberships.Where(m => m.TenantId == tenantId && m.Status == MembershipStatus.Approved).Select(m => m.AccountId).ToListAsync(ct);
        var links = await sys.FamilyLinks.Where(l => here.Contains(l.AccountId)).Select(l => new { l.AccountId, l.FamilyId }).ToListAsync(ct);
        var result = new Dictionary<int, FamilyBoardView>();
        foreach (var fid in links.Select(l => l.FamilyId).Distinct())
        {
            if (await LoadAsync(sys, fid, ct) is not { } b) continue;
            var masked = b with { People = b.People.Select(p => here.Contains(p.AccountId) ? p : p with { Name = "", Depts = [], Hidden = true }).ToList() };
            foreach (var l in links.Where(x => x.FamilyId == fid)) result[l.AccountId] = masked;
        }
        return result;
    }

    public static string Message(OnFamilyError e) => e switch
    {
        OnFamilyError.Empty => "누구와 묶을지 적어 주세요.",
        OnFamilyError.TooLong => $"{BodyMax}자까지 적을 수 있어요.",
        OnFamilyError.RequestOpen => "이미 보낸 온가족 신청이 있어요.",
        OnFamilyError.AlreadyInFamily => "이미 온가족에 들어 있어요.",
        OnFamilyError.NotFound => "취소할 신청이 없어요.",
        _ => "",
    };
}
