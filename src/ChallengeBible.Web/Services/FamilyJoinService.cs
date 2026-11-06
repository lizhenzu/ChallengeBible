using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record FamilySearchMember(string Name, string Relation);
/// <summary>찾기 결과(R-205): 팀 이름과 구성원 이름 · 역할만. 점수는 보이지 않는다.</summary>
public sealed record FamilySearchHit(int TeamId, string Name, IReadOnlyList<FamilySearchMember> Members);
public sealed record FamilyMineMember(int MemberId, string Name, string Relation, int Points, bool Me);
public sealed record FamilyJoinRow(int RequestId, string Name, string Relation);
/// <summary>우리 가족팀. RawName = 사람이 지은 이름(비었으면 null). Pct = 지난 단계에서 다음 단계까지 온 정도.</summary>
public sealed record FamilyMine(int TeamId, string Name, string? RawName, int Total, int? Next, double Pct,
    IReadOnlyList<FamilyMineMember> Members, IReadOnlyList<FamilyJoinRow> Requests);
public sealed record FamilyPending(int RequestId, int TeamId, string TeamName, string Relation);
public sealed record FamilyState(bool Enabled, int? ParticipantId, bool IsGuardian, FamilyMine? Mine, FamilyPending? Pending);
/// <summary>미션 홈 입구. Name 이 null 이면 팀 없음(PendingTeam = 신청 중인 팀).</summary>
public sealed record FamilyEntry(string? Name, int Total, int? Next, double Pct, int Waiting, string? PendingTeam);

/// <summary>
/// 가족이 스스로 꾸리는 가족팀(ADR 0083, R-204 ~ R-210). 이 부서 연결로 읽고 쓴다(전역 필터 + RLS).
/// 부르는 사람은 이 부서의 참여자여야 하고, 수락 · 거절 · 내보내기 · 이름 바꾸기는 그 팀의 활성 구성원만 한다.
/// 다른 부서 가족팀 확인(R-210)만 시스템 연결로 읽는다.
/// </summary>
public sealed class FamilyJoinService(IAppDb db, ISystemDb systemDb, RequestScope scope, TenantContext tenant, FamilyTeamService teams, TimeProvider time)
{
    private const int SearchMax = 20;
    private sealed record Me(int ParticipantId, int AccountId, bool IsGuardian);

    private async Task<bool> EnabledAsync(CancellationToken ct) =>
        !tenant.IsLobby && tenant.Current.MissionEnabled && await teams.EnabledAsync(ct);   // R-172 · 0012

    private async Task<Me?> MeAsync(AppDbContext ctx, CancellationToken ct)
    {
        if (tenant.MembershipId is not { } mid) return null;
        return await (from p in ctx.Participants
                      join m in ctx.Memberships on p.MembershipId equals m.Id
                      where m.Id == mid && p.IsActive && m.Status == MembershipStatus.Approved
                      select new Me(p.Id, m.AccountId, m.Roles.Any(r => r.Role == Roles.Guardian))).SingleOrDefaultAsync(ct);
    }

    /// <summary>R-210: 이 계정이 다른 부서의 가족팀에 들어 있는지.</summary>
    private async Task<bool> InOtherDeptAsync(int accountId, CancellationToken ct)
    {
        var here = tenant.Current.Id;
        await using var sys = systemDb.Create(scope.AccountId ?? accountId);
        return await (from f in sys.FamilyMembers
                      join p in sys.Participants on new { f.TenantId, Id = f.ParticipantId } equals new { p.TenantId, p.Id }
                      join m in sys.Memberships on new { p.TenantId, Id = p.MembershipId } equals new { m.TenantId, m.Id }
                      where f.IsActive && f.TenantId != here && m.AccountId == accountId
                      select f.Id).AnyAsync(ct);
    }

    /// <summary>만들기 · 신청 앞 공통 검사.</summary>
    private async Task<FamilyError> CheckFreeAsync(AppDbContext ctx, Me? me, string relation, CancellationToken ct)
    {
        if (!FamilyRelation.All.Contains(relation)) return FamilyError.BadRelation;
        if (me is null) return FamilyError.NotParticipant;
        if (await ctx.FamilyMembers.AnyAsync(f => f.ParticipantId == me.ParticipantId && f.IsActive, ct)) return FamilyError.AlreadyInTeam;
        if (await ctx.FamilyJoinRequests.AnyAsync(r => r.ParticipantId == me.ParticipantId, ct)) return FamilyError.HasPending;
        if (await InOtherDeptAsync(me.AccountId, ct)) return FamilyError.InOtherDept;
        return FamilyError.None;
    }

    /// <summary>내가 활성 구성원인 활성 팀(구성원 포함). 없으면 null.</summary>
    private static async Task<FamilyTeam?> MyTeamAsync(AppDbContext ctx, Me me, CancellationToken ct)
    {
        var id = await ctx.FamilyMembers.Where(f => f.ParticipantId == me.ParticipantId && f.IsActive).Select(f => (int?)f.FamilyTeamId).SingleOrDefaultAsync(ct);
        return id is null ? null : await ctx.FamilyTeams.Include(t => t.Members).SingleOrDefaultAsync(t => t.Id == id && t.IsActive, ct);
    }

    // ── 보기 ──

    public async Task<FamilyState> StateAsync(CancellationToken ct = default)
    {
        if (!await EnabledAsync(ct)) return new FamilyState(false, null, false, null, null);
        await using var ctx = db.Create();
        var me = await MeAsync(ctx, ct);
        if (me is null) return new FamilyState(true, null, false, null, null);
        var (r, today) = await teams.RulesAsync(ct);
        var (all, season, _, _) = await teams.LoadTeamsAsync(ctx, today, ct);

        FamilyMine? mine = null;
        var team = all.FirstOrDefault(t => t.Members.Any(m => m.Pid == me.ParticipantId));
        if (team.Team is not null)
        {
            var members = team.Members.Select(m => new FamilyMineMember(m.M.Id, m.Name, m.M.Relation, season.GetValueOrDefault(m.Pid), m.Pid == me.ParticipantId)).ToList();
            var total = members.Sum(m => m.Points);   // R-92
            var requests = await (from q in ctx.FamilyJoinRequests
                                  join p in ctx.Participants on q.ParticipantId equals p.Id
                                  join m in ctx.Memberships on p.MembershipId equals m.Id
                                  join a in ctx.Accounts on m.AccountId equals a.Id
                                  where q.FamilyTeamId == team.Team.Id
                                  orderby q.Id
                                  select new FamilyJoinRow(q.Id, a.DisplayName, q.Relation)).ToListAsync(ct);
            mine = new FamilyMine(team.Team.Id, FamilyTeamService.DisplayName(team.Team.Name, members.Select(m => (m.Name, m.Relation)).ToList()),
                team.Team.Name, total, FamilyTeamService.Next(r, total), FamilyTeamService.StagePct(r, total), members, requests);
        }

        FamilyPending? pending = null;
        var own = await ctx.FamilyJoinRequests.AsNoTracking().SingleOrDefaultAsync(q => q.ParticipantId == me.ParticipantId, ct);
        if (own is not null)
        {
            var target = all.FirstOrDefault(t => t.Team.Id == own.FamilyTeamId);
            var name = target.Team is null ? "가족팀" : FamilyTeamService.DisplayName(target.Team.Name, target.Members.Select(m => (m.Name, m.M.Relation)).ToList());
            pending = new FamilyPending(own.Id, own.FamilyTeamId, name, own.Relation);
        }
        return new FamilyState(true, me.ParticipantId, me.IsGuardian, mine, pending);
    }

    /// <summary>미션 홈 입구. 쓸 수 없거나 참여자가 아니면 null.</summary>
    public async Task<FamilyEntry?> EntryAsync(CancellationToken ct = default)
    {
        var s = await StateAsync(ct);
        if (!s.Enabled || s.ParticipantId is null) return null;
        return s.Mine is { } m
            ? new FamilyEntry(m.Name, m.Total, m.Next, m.Pct, m.Requests.Count, null)
            : new FamilyEntry(null, 0, null, 0, 0, s.Pending?.TeamName);
    }

    /// <summary>R-205: 사람 이름 · 팀 이름으로 찾기.</summary>
    public async Task<IReadOnlyList<FamilySearchHit>> SearchAsync(string? q, CancellationToken ct = default)
    {
        var word = q?.Trim();
        if (string.IsNullOrEmpty(word) || !await EnabledAsync(ct)) return [];
        await using var ctx = db.Create();
        var (_, today) = await teams.RulesAsync(ct);
        var (all, _, _, _) = await teams.LoadTeamsAsync(ctx, today, ct);
        return all.Select(t =>
            {
                var members = t.Members.Select(m => new FamilySearchMember(m.Name, m.M.Relation)).ToList();
                return new FamilySearchHit(t.Team.Id, FamilyTeamService.DisplayName(t.Team.Name, members.Select(m => (m.Name, m.Relation)).ToList()), members);
            })
            .Where(h => h.Name.Contains(word, StringComparison.OrdinalIgnoreCase) || h.Members.Any(m => m.Name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Take(SearchMax).ToList();
    }

    /// <summary>신청 화면에 보일 팀(없으면 null).</summary>
    public async Task<FamilySearchHit?> TeamAsync(int teamId, CancellationToken ct = default)
    {
        if (!await EnabledAsync(ct)) return null;
        await using var ctx = db.Create();
        var (_, today) = await teams.RulesAsync(ct);
        var (all, _, _, _) = await teams.LoadTeamsAsync(ctx, today, ct);
        var t = all.FirstOrDefault(x => x.Team.Id == teamId);
        if (t.Team is null) return null;
        var members = t.Members.Select(m => new FamilySearchMember(m.Name, m.M.Relation)).ToList();
        return new FamilySearchHit(t.Team.Id, FamilyTeamService.DisplayName(t.Team.Name, members.Select(m => (m.Name, m.Relation)).ToList()), members);
    }

    // ── 만들기 · 신청 ──

    /// <summary>R-204: 승인 없이 만들고 바로 첫 구성원이 된다.</summary>
    public async Task<FamilyError> CreateAsync(string? name, string relation, CancellationToken ct = default)
    {
        if (!await EnabledAsync(ct)) return FamilyError.Disabled;
        await using var ctx = db.Create();
        var me = await MeAsync(ctx, ct);
        var e = await CheckFreeAsync(ctx, me, relation, ct);
        if (e != FamilyError.None) return e;
        var t = new FamilyTeam { TenantId = tenant.Current.Id, Name = FamilyTeamService.Clean(name, 50), CreatedUtc = time.GetUtcNow().UtcDateTime };
        t.Members.Add(new FamilyMember { TenantId = t.TenantId, ParticipantId = me!.ParticipantId, Relation = relation });
        ctx.FamilyTeams.Add(t);
        return await SaveAsync(ctx, FamilyError.AlreadyInTeam, ct);
    }

    /// <summary>R-206: 역할을 골라 한 번에 한 팀에만 신청.</summary>
    public async Task<FamilyError> RequestAsync(int teamId, string relation, CancellationToken ct = default)
    {
        if (!await EnabledAsync(ct)) return FamilyError.Disabled;
        await using var ctx = db.Create();
        var me = await MeAsync(ctx, ct);
        var e = await CheckFreeAsync(ctx, me, relation, ct);
        if (e != FamilyError.None) return e;
        if (!await ctx.FamilyTeams.AnyAsync(t => t.Id == teamId && t.IsActive && t.Members.Any(m => m.IsActive), ct)) return FamilyError.NotFound;
        ctx.FamilyJoinRequests.Add(new FamilyJoinRequest { TenantId = tenant.Current.Id, FamilyTeamId = teamId, ParticipantId = me!.ParticipantId,
            Relation = relation, CreatedUtc = time.GetUtcNow().UtcDateTime });
        return await SaveAsync(ctx, FamilyError.HasPending, ct);
    }

    public async Task<FamilyError> CancelAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        if (await MeAsync(ctx, ct) is not { } me) return FamilyError.NotParticipant;
        var own = await ctx.FamilyJoinRequests.SingleOrDefaultAsync(q => q.ParticipantId == me.ParticipantId, ct);
        if (own is null) return FamilyError.NotFound;
        ctx.FamilyJoinRequests.Remove(own);
        await ctx.SaveChangesAsync(ct);
        return FamilyError.None;
    }

    // ── 팀 안에서 ──

    /// <summary>R-207: 그 팀 구성원이 수락. 신청한 사람이 이제 들어올 수 없으면 신청을 지우고 이유를 돌려준다.</summary>
    public async Task<FamilyError> AcceptAsync(int requestId, CancellationToken ct = default)
    {
        if (!await EnabledAsync(ct)) return FamilyError.Disabled;
        await using var ctx = db.Create();
        var (team, q, err) = await RequestOfMyTeamAsync(ctx, requestId, ct);
        if (err != FamilyError.None) return err;

        var who = await (from p in ctx.Participants
                         join m in ctx.Memberships on p.MembershipId equals m.Id
                         where p.Id == q!.ParticipantId && p.IsActive && m.Status == MembershipStatus.Approved
                         select (int?)m.AccountId).SingleOrDefaultAsync(ct);
        var block = who is null ? FamilyError.NotParticipant
            : await ctx.FamilyMembers.AnyAsync(f => f.ParticipantId == q!.ParticipantId && f.IsActive, ct) ? FamilyError.AlreadyInTeam
            : await InOtherDeptAsync(who.Value, ct) ? FamilyError.InOtherDept : FamilyError.None;
        ctx.FamilyJoinRequests.Remove(q!);
        if (block != FamilyError.None)
        {
            await ctx.SaveChangesAsync(ct);
            return block;
        }
        var order = (byte)(team!.Members.Max(m => m.SortOrder) + 1);
        var old = team.Members.SingleOrDefault(m => m.ParticipantId == q!.ParticipantId);   // 예전에 멈춘 자리
        if (old is not null) { old.IsActive = true; old.Relation = q!.Relation; old.SortOrder = order; }
        else team.Members.Add(new FamilyMember { TenantId = team.TenantId, ParticipantId = q!.ParticipantId, Relation = q.Relation, SortOrder = order });
        return await SaveAsync(ctx, FamilyError.AlreadyInTeam, ct);
    }

    public async Task<FamilyError> RejectAsync(int requestId, CancellationToken ct = default)
    {
        if (!await EnabledAsync(ct)) return FamilyError.Disabled;
        await using var ctx = db.Create();
        var (_, q, err) = await RequestOfMyTeamAsync(ctx, requestId, ct);
        if (err != FamilyError.None) return err;
        ctx.FamilyJoinRequests.Remove(q!);
        await ctx.SaveChangesAsync(ct);
        return FamilyError.None;
    }

    private async Task<(FamilyTeam? Team, FamilyJoinRequest? Request, FamilyError Error)> RequestOfMyTeamAsync(AppDbContext ctx, int requestId, CancellationToken ct)
    {
        if (await MeAsync(ctx, ct) is not { } me) return (null, null, FamilyError.NotParticipant);
        var team = await MyTeamAsync(ctx, me, ct);
        if (team is null) return (null, null, FamilyError.NotMember);
        var q = await ctx.FamilyJoinRequests.SingleOrDefaultAsync(x => x.Id == requestId, ct);
        if (q is null) return (null, null, FamilyError.NotFound);
        return q.FamilyTeamId == team.Id ? (team, q, FamilyError.None) : (null, null, FamilyError.NotMember);
    }

    /// <summary>R-208: 나가기. 마지막 사람이면 팀도 사라진다.</summary>
    public async Task<FamilyError> LeaveAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        if (await MeAsync(ctx, ct) is not { } me) return FamilyError.NotParticipant;
        var team = await MyTeamAsync(ctx, me, ct);
        if (team is null) return FamilyError.NotMember;
        await FamilyTeamService.DropMemberAsync(ctx, team, team.Members.Single(m => m.ParticipantId == me.ParticipantId), ct);
        await ctx.SaveChangesAsync(ct);
        return FamilyError.None;
    }

    /// <summary>R-208: 같은 팀 구성원 내보내기(나는 나가기로).</summary>
    public async Task<FamilyError> KickAsync(int memberId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        if (await MeAsync(ctx, ct) is not { } me) return FamilyError.NotParticipant;
        var team = await MyTeamAsync(ctx, me, ct);
        if (team is null) return FamilyError.NotMember;
        var m = team.Members.SingleOrDefault(x => x.Id == memberId && x.IsActive);
        if (m is null || m.ParticipantId == me.ParticipantId) return FamilyError.NotFound;
        await FamilyTeamService.DropMemberAsync(ctx, team, m, ct);
        await ctx.SaveChangesAsync(ct);
        return FamilyError.None;
    }

    /// <summary>R-209: 구성원 누구든 팀 이름을 바꾼다. 비우면 R-93.</summary>
    public async Task<FamilyError> RenameAsync(string? name, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        if (await MeAsync(ctx, ct) is not { } me) return FamilyError.NotParticipant;
        var team = await MyTeamAsync(ctx, me, ct);
        if (team is null) return FamilyError.NotMember;
        team.Name = FamilyTeamService.Clean(name, 50);
        await ctx.SaveChangesAsync(ct);
        return FamilyError.None;
    }

    /// <summary>R-209: 내 역할은 내가 바꾼다.</summary>
    public async Task<FamilyError> SetRelationAsync(string relation, CancellationToken ct = default)
    {
        if (!FamilyRelation.All.Contains(relation)) return FamilyError.BadRelation;
        await using var ctx = db.Create();
        if (await MeAsync(ctx, ct) is not { } me) return FamilyError.NotParticipant;
        var team = await MyTeamAsync(ctx, me, ct);
        if (team is null) return FamilyError.NotMember;
        team.Members.Single(m => m.ParticipantId == me.ParticipantId).Relation = relation;
        await ctx.SaveChangesAsync(ct);
        return FamilyError.None;
    }

    /// <summary>동시에 눌러 고유 인덱스에 걸리면 raced 를 돌려준다.</summary>
    private static async Task<FamilyError> SaveAsync(AppDbContext ctx, FamilyError raced, CancellationToken ct)
    {
        try { await ctx.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return raced; }
        return FamilyError.None;
    }
}
