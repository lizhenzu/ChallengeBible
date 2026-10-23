using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record MemberRow(int MembershipId, int AccountId, string DisplayName, string Email, string Status,
    IReadOnlyList<string> Roles, string? GroupLabel, DateTime RequestedUtc);

public sealed record MemberFilter(string Status, string? Query, string? Role, string? Group);

public enum MemberError
{
    None, NotFound, NoRole, BadState, SelfAdminRemove, SelfSuspend, FamilyTenantNotChosen, FamilyTenantElsewhere,
}

/// <summary>
/// 소속 관리(API 계약 4-4 · 5-4): 신청 승인 · 거절, 역할, 반(그룹 라벨), 정지 · 해제.
/// 모든 조회 · 저장은 이 부서 안에서만 된다(전역 필터 + RLS). 부르는 화면이 관리자인지 먼저 검사한다.
/// </summary>
public sealed class MemberAdminService(IAppDb db, ISystemDb systemDb, TenantContext tenant, TimeProvider time)
{
    public async Task<List<MemberRow>> ListAsync(MemberFilter f, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var q = from m in ctx.Memberships.Include(x => x.Roles)
                join a in ctx.Accounts on m.AccountId equals a.Id
                where m.Status == f.Status
                select new { m, a };
        if (!string.IsNullOrWhiteSpace(f.Query))
        {
            var s = f.Query.Trim();
            q = q.Where(x => x.a.DisplayName.Contains(s) || x.a.Email.Contains(s.ToLower()));
        }
        if (!string.IsNullOrWhiteSpace(f.Role)) q = q.Where(x => x.m.Roles.Any(r => r.Role == f.Role));
        if (f.Group == "-") q = q.Where(x => x.m.GroupLabel == null);
        else if (!string.IsNullOrWhiteSpace(f.Group)) q = q.Where(x => x.m.GroupLabel == f.Group);

        var rows = await q.ToListAsync(ct);
        return rows
            .Select(x => new MemberRow(x.m.Id, x.a.Id, x.a.DisplayName, x.a.Email, x.m.Status,
                Infrastructure.Data.Roles.All.Where(r => x.m.Roles.Any(mr => mr.Role == r)).ToList(), x.m.GroupLabel, x.m.RequestedUtc))
            .OrderBy(r => r.DisplayName, StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false))
            .ToList();
    }

    public async Task<Dictionary<string, int>> CountsAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return await ctx.Memberships.GroupBy(m => m.Status).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
    }

    public async Task<List<string>> GroupsAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return await ctx.Memberships.Where(m => m.GroupLabel != null).Select(m => m.GroupLabel!).Distinct().OrderBy(g => g).ToListAsync(ct);
    }

    /// <summary>승인 + 역할 + 반. 신청 상태만 승인할 수 있다.</summary>
    public Task<MemberError> ApproveAsync(int actorAccountId, int membershipId, IReadOnlyCollection<string> roles, string? group, CancellationToken ct = default) =>
        SaveAsync(actorAccountId, membershipId, roles, group, approve: true, ct);

    /// <summary>승인된 소속의 역할 · 반 바꾸기.</summary>
    public Task<MemberError> UpdateAsync(int actorAccountId, int membershipId, IReadOnlyCollection<string> roles, string? group, CancellationToken ct = default) =>
        SaveAsync(actorAccountId, membershipId, roles, group, approve: false, ct);

    private async Task<MemberError> SaveAsync(int actor, int membershipId, IReadOnlyCollection<string> roles, string? group, bool approve, CancellationToken ct)
    {
        var want = roles.Where(r => Infrastructure.Data.Roles.All.Contains(r)).Distinct().ToList();
        if (want.Count == 0) return MemberError.NoRole;

        await using var ctx = db.Create();
        var m = await ctx.Memberships.Include(x => x.Roles).SingleOrDefaultAsync(x => x.Id == membershipId, ct);
        if (m is null) return MemberError.NotFound;
        if (approve ? m.Status != MembershipStatus.Requested : m.Status != MembershipStatus.Approved) return MemberError.BadState;
        if (m.AccountId == actor && m.Roles.Any(r => r.Role == Infrastructure.Data.Roles.TenantAdmin) && !want.Contains(Infrastructure.Data.Roles.TenantAdmin))
            return MemberError.SelfAdminRemove;

        var family = await CheckFamilyTenantAsync(actor, m.AccountId, want, ct);
        if (family != MemberError.None) return family;

        ctx.MembershipRoles.RemoveRange(m.Roles.Where(r => !want.Contains(r.Role)));
        foreach (var r in want.Where(r => m.Roles.All(x => x.Role != r)))
            m.Roles.Add(new MembershipRole { TenantId = m.TenantId, MembershipId = m.Id, Role = r });
        m.GroupLabel = string.IsNullOrWhiteSpace(group) ? null : group.Trim()[..Math.Min(group.Trim().Length, 50)];

        // 참여자 역할 ↔ 참여자 행(점수 · 수행 기록의 주인). 역할을 빼면 멈추기만 하고 기록은 남긴다.
        var participant = await ctx.Participants.SingleOrDefaultAsync(p => p.MembershipId == m.Id, ct);
        if (want.Contains(Infrastructure.Data.Roles.Participant))
        {
            if (participant is null) ctx.Participants.Add(new Participant { TenantId = m.TenantId, MembershipId = m.Id });
            else participant.IsActive = true;
        }
        else if (participant is not null) participant.IsActive = false;
        if (approve)
        {
            m.Status = MembershipStatus.Approved;
            m.DecidedUtc = time.GetUtcNow().UtcDateTime;
            m.DecidedByAccountId = actor;
        }
        await ctx.SaveChangesAsync(ct);
        return MemberError.None;
    }

    public async Task<MemberError> RejectAsync(int actor, int membershipId, CancellationToken ct = default) =>
        await ChangeStatusAsync(actor, membershipId, MembershipStatus.Requested, MembershipStatus.Rejected, ct);

    public async Task<MemberError> SuspendAsync(int actor, int membershipId, CancellationToken ct = default) =>
        await ChangeStatusAsync(actor, membershipId, MembershipStatus.Approved, MembershipStatus.Suspended, ct);

    public async Task<MemberError> ResumeAsync(int actor, int membershipId, CancellationToken ct = default) =>
        await ChangeStatusAsync(actor, membershipId, MembershipStatus.Suspended, MembershipStatus.Approved, ct);

    private async Task<MemberError> ChangeStatusAsync(int actor, int membershipId, string from, string to, CancellationToken ct)
    {
        await using var ctx = db.Create();
        var m = await ctx.Memberships.SingleOrDefaultAsync(x => x.Id == membershipId, ct);
        if (m is null) return MemberError.NotFound;
        if (m.Status != from) return MemberError.BadState;
        if (to == MembershipStatus.Suspended && m.AccountId == actor) return MemberError.SelfSuspend;
        m.Status = to;
        m.DecidedUtc = time.GetUtcNow().UtcDateTime;
        m.DecidedByAccountId = actor;
        await ctx.SaveChangesAsync(ct);
        return MemberError.None;
    }

    /// <summary>
    /// R-168 · ADR 0046: 보호자인 계정은 "가족과 함께할 부서"에서만 참여자가 될 수 있다.
    /// 다른 부서의 보호자 역할을 봐야 해서 시스템 연결로 읽는다(물리 설계 3-3 — 읽기만, 관리자 화면에서 이미 권한 확인).
    /// </summary>
    private async Task<MemberError> CheckFamilyTenantAsync(int actor, int accountId, IReadOnlyCollection<string> want, CancellationToken ct)
    {
        if (!want.Contains(Infrastructure.Data.Roles.Participant)) return MemberError.None;
        await using var sys = systemDb.Create(actor);
        var isGuardian = want.Contains(Infrastructure.Data.Roles.Guardian)
            || await sys.MembershipRoles.AnyAsync(r => r.Role == Infrastructure.Data.Roles.Guardian
                && sys.Memberships.Any(m => m.TenantId == r.TenantId && m.Id == r.MembershipId && m.AccountId == accountId), ct);
        if (!isGuardian) return MemberError.None;
        var familyTenant = await sys.Accounts.Where(a => a.Id == accountId).Select(a => a.FamilyTenantId).SingleAsync(ct);
        if (familyTenant is null) return MemberError.FamilyTenantNotChosen;
        return familyTenant == tenant.Current.Id ? MemberError.None : MemberError.FamilyTenantElsewhere;
    }

    // ── 보호 관계(R-132): 1차는 소속 관리의 줄 편집 안에서 연결한다 ──

    /// <summary>이 부서에서 보호자 역할이 있는 승인된 소속.</summary>
    public async Task<List<(int MembershipId, string Name)>> GuardiansAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return (await (from m in ctx.Memberships
                       where m.Status == MembershipStatus.Approved && m.Roles.Any(r => r.Role == Infrastructure.Data.Roles.Guardian)
                       join a in ctx.Accounts on m.AccountId equals a.Id
                       orderby a.DisplayName
                       select new { m.Id, a.DisplayName }).ToListAsync(ct))
            .Select(x => (x.Id, x.DisplayName)).ToList();
    }

    /// <summary>참여자 소속 Id → 연결된 보호자 소속 Id 목록.</summary>
    public async Task<Dictionary<int, List<int>>> LinksAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var rows = await (from l in ctx.GuardianLinks
                          join p in ctx.Participants on l.ParticipantId equals p.Id
                          select new { p.MembershipId, l.GuardianMembershipId }).ToListAsync(ct);
        return rows.GroupBy(r => r.MembershipId).ToDictionary(g => g.Key, g => g.Select(x => x.GuardianMembershipId).ToList());
    }

    public async Task<MemberError> LinkGuardianAsync(int actor, int guardianMembershipId, int participantMembershipId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var participant = await ctx.Participants.SingleOrDefaultAsync(p => p.MembershipId == participantMembershipId && p.IsActive, ct);
        var guardianOk = await ctx.Memberships.AnyAsync(m => m.Id == guardianMembershipId && m.Status == MembershipStatus.Approved
            && m.Roles.Any(r => r.Role == Infrastructure.Data.Roles.Guardian), ct);
        if (participant is null || !guardianOk) return MemberError.NotFound;
        if (await ctx.GuardianLinks.AnyAsync(l => l.GuardianMembershipId == guardianMembershipId && l.ParticipantId == participant.Id, ct)) return MemberError.None;
        ctx.GuardianLinks.Add(new GuardianLink { TenantId = tenant.Current.Id, GuardianMembershipId = guardianMembershipId, ParticipantId = participant.Id,
            CreatedUtc = time.GetUtcNow().UtcDateTime, CreatedByAccountId = actor });
        await ctx.SaveChangesAsync(ct);
        return MemberError.None;
    }

    public async Task<MemberError> UnlinkGuardianAsync(int guardianMembershipId, int participantMembershipId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var link = await (from l in ctx.GuardianLinks
                          join p in ctx.Participants on l.ParticipantId equals p.Id
                          where l.GuardianMembershipId == guardianMembershipId && p.MembershipId == participantMembershipId
                          select l).SingleOrDefaultAsync(ct);
        if (link is null) return MemberError.NotFound;
        ctx.GuardianLinks.Remove(link);
        await ctx.SaveChangesAsync(ct);
        return MemberError.None;
    }

    public static string Message(MemberError e) => e switch
    {
        MemberError.NotFound => "이 부서에서 찾을 수 없어요.",
        MemberError.NoRole => "역할을 하나는 골라 주세요.",
        MemberError.BadState => "이미 처리됐어요. 화면을 새로 고쳐 주세요.",
        MemberError.SelfAdminRemove => "내 관리자 역할은 여기서 뺄 수 없어요.",
        MemberError.SelfSuspend => "나를 정지할 수는 없어요.",
        MemberError.FamilyTenantNotChosen => "부모님이 먼저 가족과 함께할 부서를 골라야 해요.",
        MemberError.FamilyTenantElsewhere => "이 부모님은 다른 부서를 가족과 함께할 부서로 골랐어요. 여기서는 보호자로만 함께해요.",
        _ => "",
    };
}
