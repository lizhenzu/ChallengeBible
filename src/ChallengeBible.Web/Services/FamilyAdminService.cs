using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>신청 한 건(플랫폼 목록). RequesterDepts = 신청한 사람의 승인된 부서와 역할 요약.</summary>
public sealed record FamilyRequestRow(int Id, int RequesterId, string Requester, string RequesterDepts, string Body, DateTime RequestedUtc);
/// <summary>계정 찾기 결과. Depts = "유년부 참여자" 처럼, FamilyId = 이미 든 온가족(있으면 넣을 수 없음).</summary>
public sealed record FamilyHit(int Id, string Name, string Email, string Depts, int? FamilyId);
/// <summary>온가족 상세의 구성원 한 명: 소속 부서 · 역할, 보호자면 시상 부서와 고를 수 있는 부서.</summary>
public sealed record FamilyAdminMember(FamilyPerson Person, string Depts, bool IsGuardian, int? AwardTenantId, IReadOnlyList<(int Id, string Name)> AwardOptions);
public sealed record FamilyAdminDetail(FamilyBoardView Board, IReadOnlyList<FamilyAdminMember> Members, string? RawName);

/// <summary>
/// 온가족 관리(0065, R-175 · R-176 · R-179, 확정 시안: 플랫폼-온가족) — 플랫폼 관리자만, 시작마다 확인.
/// 여러 부서 계정을 보고 온가족 표를 쓰므로 시스템 연결(물리 설계 3-3 "온가족 관리"). 모든 변경은 AuditLog 에 남긴다.
/// 시상 부서 정하기는 FamilyTenantService(0046 처리기)를 플랫폼 관리자가 부른다.
/// </summary>
public sealed class FamilyAdminService(ISystemDb systemDb, FamilyTenantService familyTenant, TimeProvider time)
{
    public const int NameMax = 30;

    private async Task<AppDbContext> OpenAsync(int actor, CancellationToken ct)
    {
        var ctx = systemDb.Create(actor);
        if (!await ctx.Accounts.AnyAsync(a => a.Id == actor && a.IsPlatformAdmin && a.Status == "Active", ct))
        {
            await ctx.DisposeAsync();
            throw new UnauthorizedAccessException("플랫폼 관리자만 할 수 있어요.");
        }
        return ctx;
    }

    /// <summary>이메일은 앞 두 글자만 보인다(시안: yh***@example.com).</summary>
    public static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at <= 0 ? email : email[..Math.Min(2, at)] + "***" + email[at..];
    }

    private static async Task<Dictionary<int, string>> DeptSummaryAsync(AppDbContext ctx, IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        var rows = await (from m in ctx.Memberships
                          join t in ctx.Tenants on m.TenantId equals t.Id
                          where ids.Contains(m.AccountId) && m.Status == MembershipStatus.Approved && t.Status == TenantStatus.Active
                          orderby t.Name
                          select new { m.AccountId, t.Name, t.IsLobby, Roles = m.Roles.Select(r => r.Role).ToList() }).ToListAsync(ct);
        return rows.GroupBy(r => r.AccountId).ToDictionary(g => g.Key, g => string.Join(" / ", g.Select(r =>
            $"{(r.IsLobby ? r.Name : JoinDept(r.Name))} {string.Join(" · ", r.Roles.Where(x => x is Roles.Participant or Roles.Guardian).OrderByDescending(x => x == Roles.Guardian).Select(RoleLabel))}".Trim())));
    }

    private static string JoinDept(string name) => Pages.JoinModel.Split(name).Dept;
    private static string RoleLabel(string r) => r == Roles.Guardian ? "보호자" : "참여자";

    public async Task<List<FamilyRequestRow>> RequestsAsync(int actor, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var rows = await (from r in ctx.FamilyRequests
                          join a in ctx.Accounts on r.RequesterAccountId equals a.Id
                          where r.Status == FamilyRequestStatus.Requested
                          orderby r.RequestedUtc
                          select new { r.Id, r.RequesterAccountId, a.DisplayName, r.Body, r.RequestedUtc }).ToListAsync(ct);
        var depts = await DeptSummaryAsync(ctx, rows.Select(r => r.RequesterAccountId).ToList(), ct);
        return rows.Select(r => new FamilyRequestRow(r.Id, r.RequesterAccountId, r.DisplayName, depts.GetValueOrDefault(r.RequesterAccountId, "소속 없음"), r.Body, r.RequestedUtc)).ToList();
    }

    public async Task<FamilyRequestRow?> RequestAsync(int actor, int id, CancellationToken ct = default) =>
        (await RequestsAsync(actor, ct)).FirstOrDefault(r => r.Id == id);

    /// <summary>온가족 목록(이름 · 구성원 수 · 부서 · 누적 총점). q 가 있으면 구성원 이름 · 이메일로 거른다.</summary>
    public async Task<List<FamilyBoardView>> FamiliesAsync(int actor, string? q, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var term = q?.Trim() ?? "";
        var ids = term.Length == 0
            ? await ctx.Families.OrderBy(f => f.Id).Select(f => f.Id).ToListAsync(ct)
            : await (from l in ctx.FamilyLinks join a in ctx.Accounts on l.AccountId equals a.Id
                     where a.DisplayName.Contains(term) || a.Email.Contains(term.ToLowerInvariant())
                     select l.FamilyId).Distinct().ToListAsync(ct);
        var list = new List<FamilyBoardView>();
        foreach (var id in ids) if (await OnFamilyService.LoadAsync(ctx, id, ct) is { } b) list.Add(b);
        return list.OrderByDescending(b => b.Total).ThenBy(b => b.Name).ToList();
    }

    public async Task<List<FamilyHit>> SearchAsync(int actor, string? q, CancellationToken ct = default)
    {
        var term = q?.Trim() ?? "";
        if (term.Length == 0) return [];
        await using var ctx = await OpenAsync(actor, ct);
        var lower = term.ToLowerInvariant();
        var accounts = await ctx.Accounts.AsNoTracking().Where(a => a.Status == "Active" && (a.Email.Contains(lower) || a.DisplayName.Contains(term)))
            .OrderBy(a => a.DisplayName).ThenBy(a => a.Email).Take(20).ToListAsync(ct);
        var ids = accounts.Select(a => a.Id).ToList();
        var depts = await DeptSummaryAsync(ctx, ids, ct);
        var fam = await ctx.FamilyLinks.Where(l => ids.Contains(l.AccountId)).ToDictionaryAsync(l => l.AccountId, l => l.FamilyId, ct);
        return accounts.Select(a => new FamilyHit(a.Id, a.DisplayName, MaskEmail(a.Email), depts.GetValueOrDefault(a.Id, "소속 없음"),
            fam.TryGetValue(a.Id, out var f) ? f : null)).ToList();
    }

    /// <summary>고른 계정들의 이름 · 부서 요약(새로 묶기 화면의 "넣을 구성원").</summary>
    public async Task<List<FamilyHit>> AccountsAsync(int actor, IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return [];
        await using var ctx = await OpenAsync(actor, ct);
        var accounts = await ctx.Accounts.AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync(ct);
        var depts = await DeptSummaryAsync(ctx, ids, ct);
        var fam = await ctx.FamilyLinks.Where(l => ids.Contains(l.AccountId)).ToDictionaryAsync(l => l.AccountId, l => l.FamilyId, ct);
        return ids.Select(id => accounts.FirstOrDefault(a => a.Id == id)).OfType<Account>()
            .Select(a => new FamilyHit(a.Id, a.DisplayName, MaskEmail(a.Email), depts.GetValueOrDefault(a.Id, "소속 없음"), fam.TryGetValue(a.Id, out var f) ? f : null)).ToList();
    }

    public async Task<FamilyAdminDetail?> DetailAsync(int actor, int id, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var raw = await ctx.Families.Where(f => f.Id == id).Select(f => new { f.Name }).SingleOrDefaultAsync(ct);
        if (raw is null || await OnFamilyService.LoadAsync(ctx, id, ct) is not { } board) return null;
        var ids = board.People.Select(p => p.AccountId).ToList();
        var depts = await DeptSummaryAsync(ctx, ids, ct);
        var award = await ctx.Accounts.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.FamilyTenantId, ct);
        var mems = await (from m in ctx.Memberships
                          join t in ctx.Tenants on m.TenantId equals t.Id
                          where ids.Contains(m.AccountId) && m.Status == MembershipStatus.Approved && t.Status == TenantStatus.Active && !t.IsLobby
                          orderby t.Name
                          select new { m.AccountId, t.Id, t.Name, Guardian = m.Roles.Any(r => r.Role == Roles.Guardian) }).ToListAsync(ct);
        var members = board.People.Select(p =>
        {
            var mine = mems.Where(m => m.AccountId == p.AccountId).ToList();
            return new FamilyAdminMember(p, depts.GetValueOrDefault(p.AccountId, "소속 없음"), mine.Any(m => m.Guardian),
                award.GetValueOrDefault(p.AccountId), mine.Select(m => (m.Id, m.Name)).ToList());
        }).ToList();
        return new FamilyAdminDetail(board, members, raw.Name);
    }

    private static string? CleanName(string? name)
    {
        var n = name?.Trim();
        return string.IsNullOrEmpty(n) ? null : n[..Math.Min(n.Length, NameMax)];
    }

    private void Log(AppDbContext ctx, int actor, string action, string detail) =>
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorAccountId = actor, Action = action, Detail = detail });

    /// <summary>새로 묶기(R-175). 신청에서 왔으면 그 신청을 승인으로 닫는다(R-176). 이미 다른 온가족인 계정이 있으면 하나도 묶지 않는다.</summary>
    public async Task<(OnFamilyError Error, int Id)> CreateAsync(int actor, string? name, int? requestId,
        IReadOnlyList<(int AccountId, string Relation)> members, CancellationToken ct = default)
    {
        var want = members.Where(m => FamilyRelation.All.Contains(m.Relation)).DistinctBy(m => m.AccountId).ToList();
        if (want.Count == 0) return (OnFamilyError.Empty, 0);
        await using var ctx = await OpenAsync(actor, ct);
        var ids = want.Select(m => m.AccountId).ToList();
        if (await ctx.Accounts.CountAsync(a => ids.Contains(a.Id), ct) != ids.Count) return (OnFamilyError.NotFound, 0);
        if (await ctx.FamilyLinks.AnyAsync(l => ids.Contains(l.AccountId), ct)) return (OnFamilyError.AlreadyInFamily, 0);
        var now = time.GetUtcNow().UtcDateTime;
        var f = new Family { Name = CleanName(name), CreatedUtc = now, CreatedByAccountId = actor };
        byte order = 0;
        foreach (var m in want) f.Links.Add(new FamilyLink { AccountId = m.AccountId, Relation = m.Relation, SortOrder = order++, AddedUtc = now, AddedByAccountId = actor });
        ctx.Families.Add(f);
        await ctx.SaveChangesAsync(ct);
        if (requestId is int rid && await ctx.FamilyRequests.SingleOrDefaultAsync(r => r.Id == rid && r.Status == FamilyRequestStatus.Requested, ct) is { } req)
        {
            req.Status = FamilyRequestStatus.Approved;
            req.DecidedUtc = now;
            req.DecidedByAccountId = actor;
            req.FamilyId = f.Id;
            Log(ctx, actor, "FamilyRequestDecided", $"request={rid} account={req.RequesterAccountId} approved family={f.Id}");
        }
        Log(ctx, actor, "FamilyLinked", $"family={f.Id} accounts={string.Join(",", ids)}");
        await ctx.SaveChangesAsync(ct);
        return (OnFamilyError.None, f.Id);
    }

    public async Task<bool> RejectAsync(int actor, int requestId, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var r = await ctx.FamilyRequests.SingleOrDefaultAsync(x => x.Id == requestId && x.Status == FamilyRequestStatus.Requested, ct);
        if (r is null) return false;
        r.Status = FamilyRequestStatus.Rejected;
        r.DecidedUtc = time.GetUtcNow().UtcDateTime;
        r.DecidedByAccountId = actor;
        Log(ctx, actor, "FamilyRequestDecided", $"request={requestId} account={r.RequesterAccountId} rejected");
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    public async Task<OnFamilyError> AddAsync(int actor, int familyId, int accountId, string relation, CancellationToken ct = default)
    {
        if (!FamilyRelation.All.Contains(relation)) return OnFamilyError.NotFound;
        await using var ctx = await OpenAsync(actor, ct);
        var f = await ctx.Families.Include(x => x.Links).SingleOrDefaultAsync(x => x.Id == familyId, ct);
        if (f is null || !await ctx.Accounts.AnyAsync(a => a.Id == accountId, ct)) return OnFamilyError.NotFound;
        if (await ctx.FamilyLinks.AnyAsync(l => l.AccountId == accountId, ct)) return OnFamilyError.AlreadyInFamily;
        var now = time.GetUtcNow().UtcDateTime;
        f.Links.Add(new FamilyLink { AccountId = accountId, Relation = relation, SortOrder = (byte)Math.Min(255, f.Links.Count), AddedUtc = now, AddedByAccountId = actor });
        Log(ctx, actor, "FamilyLinked", $"family={familyId} accounts={accountId}");
        await ctx.SaveChangesAsync(ct);
        return OnFamilyError.None;
    }

    /// <summary>빼기(R-175). 마지막 구성원을 빼면 온가족도 없앤다(해체).</summary>
    public async Task<bool> RemoveAsync(int actor, int familyId, int accountId, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var link = await ctx.FamilyLinks.SingleOrDefaultAsync(l => l.FamilyId == familyId && l.AccountId == accountId, ct);
        if (link is null) return false;
        ctx.FamilyLinks.Remove(link);
        Log(ctx, actor, "FamilyUnlinked", $"family={familyId} account={accountId}");
        await ctx.SaveChangesAsync(ct);
        if (!await ctx.FamilyLinks.AnyAsync(l => l.FamilyId == familyId, ct)) await DropAsync(ctx, actor, familyId, ct);
        return true;
    }

    /// <summary>이름 · 순서 · 관계 저장. order = 구성원 계정 Id 순서, relations 는 같은 순서.</summary>
    public async Task<bool> SaveAsync(int actor, int familyId, string? name, IReadOnlyList<(int AccountId, string Relation)> order, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var f = await ctx.Families.Include(x => x.Links).SingleOrDefaultAsync(x => x.Id == familyId, ct);
        if (f is null) return false;
        f.Name = CleanName(name);
        byte i = 0;
        foreach (var (accountId, relation) in order)
        {
            var l = f.Links.FirstOrDefault(x => x.AccountId == accountId);
            if (l is null) continue;
            l.SortOrder = i++;
            if (FamilyRelation.All.Contains(relation)) l.Relation = relation;
        }
        Log(ctx, actor, "FamilySaved", $"family={familyId}");
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>해체: 묶음만 푼다. 부서 기록 · 점수 · 가족팀은 그대로.</summary>
    public async Task<bool> DissolveAsync(int actor, int familyId, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        if (!await ctx.Families.AnyAsync(f => f.Id == familyId, ct)) return false;
        ctx.FamilyLinks.RemoveRange(await ctx.FamilyLinks.Where(l => l.FamilyId == familyId).ToListAsync(ct));
        await ctx.SaveChangesAsync(ct);
        await DropAsync(ctx, actor, familyId, ct);
        return true;
    }

    private async Task DropAsync(AppDbContext ctx, int actor, int familyId, CancellationToken ct)
    {
        foreach (var r in await ctx.FamilyRequests.Where(r => r.FamilyId == familyId).ToListAsync(ct)) r.FamilyId = null;
        ctx.Families.Remove(await ctx.Families.SingleAsync(f => f.Id == familyId, ct));
        Log(ctx, actor, "FamilyUnlinked", $"family={familyId} dissolved");
        await ctx.SaveChangesAsync(ct);
    }

    /// <summary>부모의 시상 부서 정하기 · 바꾸기(R-179) — 플랫폼 관리자 확인 뒤 0046 처리기로.</summary>
    public async Task<bool> SetAwardTenantAsync(int actor, int accountId, int tenantId, CancellationToken ct = default)
    {
        await using (var ctx = await OpenAsync(actor, ct)) { }
        return await familyTenant.ChooseAsync(actor, accountId, tenantId, ct);
    }
}
