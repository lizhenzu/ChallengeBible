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
    IReadOnlyList<FamilyMineMember> Members, IReadOnlyList<FamilyJoinRow> Requests, IReadOnlyList<FamilySentInvite> Sent);
public sealed record FamilyPending(int RequestId, int TeamId, string TeamName, string Relation);
/// <summary>우리 팀이 보내고 수락을 기다리는 초대(R-211).</summary>
public sealed record FamilySentInvite(int InviteId, string Name, string Relation);
/// <summary>내가 받은 초대(R-212). Relation = 초대한 사람이 고른 역할(처음 골라 둘 값). SnoozedToday = 오늘 "다음에 할래요"를 눌러 부서 홈 배너에서 숨김.</summary>
public sealed record FamilyReceivedInvite(int InviteId, string TeamName, string InvitedBy, string Relation, IReadOnlyList<FamilySearchMember> Members, bool SnoozedToday);
/// <summary>초대할 수 있는 사람(R-211). 이름만 보인다.</summary>
public sealed record FamilyCandidate(int ParticipantId, string Name);
public sealed record FamilyState(bool Enabled, int? ParticipantId, bool IsGuardian, FamilyMine? Mine, FamilyPending? Pending, IReadOnlyList<FamilyReceivedInvite> Invites);
/// <summary>미션 홈 입구. Name 이 null 이면 팀 없음(PendingTeam = 신청 중인 팀). 받은 초대는 여기에 싣지 않는다(부서 홈 배너, ADR 0084).</summary>
public sealed record FamilyEntry(string? Name, int Total, int? Next, double Pct, int Waiting, string? PendingTeam);

/// <summary>
/// 가족이 스스로 꾸리는 가족팀(ADR 0083 · 0084, R-204 ~ R-213). 이 부서 연결로 읽고 쓴다(전역 필터 + RLS).
/// 부르는 사람은 이 부서의 참여자여야 하고, 수락 · 거절 · 내보내기 · 이름 바꾸기 · 초대는 그 팀의 활성 구성원만, 받은 초대의 수락 · 거절은 초대받은 본인만 한다.
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
    private async Task<bool> InOtherDeptAsync(int accountId, CancellationToken ct) =>
        (await InOtherDeptAsync([accountId], ct)).Count > 0;

    private async Task<HashSet<int>> InOtherDeptAsync(IReadOnlyList<int> accountIds, CancellationToken ct)
    {
        if (accountIds.Count == 0) return [];
        var here = tenant.Current.Id;
        await using var sys = systemDb.Create(scope.AccountId ?? accountIds[0]);
        var found = await (from f in sys.FamilyMembers
                           join p in sys.Participants on new { f.TenantId, Id = f.ParticipantId } equals new { p.TenantId, p.Id }
                           join m in sys.Memberships on new { p.TenantId, Id = p.MembershipId } equals new { m.TenantId, m.Id }
                           where f.IsActive && f.TenantId != here && accountIds.Contains(m.AccountId)
                           select m.AccountId).ToListAsync(ct);
        return [.. found];
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
        if (!await EnabledAsync(ct)) return new FamilyState(false, null, false, null, null, []);
        await using var ctx = db.Create();
        var me = await MeAsync(ctx, ct);
        if (me is null) return new FamilyState(true, null, false, null, null, []);
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
            var sent = await (from i in ctx.FamilyInvites
                              join p in ctx.Participants on i.ParticipantId equals p.Id
                              join m in ctx.Memberships on p.MembershipId equals m.Id
                              join a in ctx.Accounts on m.AccountId equals a.Id
                              where i.FamilyTeamId == team.Team.Id
                              orderby i.Id
                              select new FamilySentInvite(i.Id, a.DisplayName, i.Relation)).ToListAsync(ct);
            mine = new FamilyMine(team.Team.Id, FamilyTeamService.DisplayName(team.Team.Name, members.Select(m => (m.Name, m.Relation)).ToList()),
                team.Team.Name, total, FamilyTeamService.Next(r, total), FamilyTeamService.StagePct(r, total), members, requests, sent);
        }

        var invites = new List<FamilyReceivedInvite>();
        if (mine is null)
        {
            var rows = await (from i in ctx.FamilyInvites
                              join p in ctx.Participants on i.InvitedByParticipantId equals p.Id
                              join m in ctx.Memberships on p.MembershipId equals m.Id
                              join a in ctx.Accounts on m.AccountId equals a.Id
                              where i.ParticipantId == me.ParticipantId
                              orderby i.Id
                              select new { i.Id, i.FamilyTeamId, i.Relation, i.SnoozedOn, By = a.DisplayName }).ToListAsync(ct);
            foreach (var row in rows)
            {
                var t = all.FirstOrDefault(x => x.Team.Id == row.FamilyTeamId);
                if (t.Team is null) continue;   // 멈춘 팀의 초대는 보이지 않는다
                var ms = t.Members.Select(m => new FamilySearchMember(m.Name, m.M.Relation)).ToList();
                invites.Add(new FamilyReceivedInvite(row.Id, FamilyTeamService.DisplayName(t.Team.Name, ms.Select(m => (m.Name, m.Relation)).ToList()), row.By, row.Relation, ms, row.SnoozedOn == today));
            }
        }

        FamilyPending? pending = null;
        var own = await ctx.FamilyJoinRequests.AsNoTracking().SingleOrDefaultAsync(q => q.ParticipantId == me.ParticipantId, ct);
        if (own is not null)
        {
            var target = all.FirstOrDefault(t => t.Team.Id == own.FamilyTeamId);
            var name = target.Team is null ? "가족팀" : FamilyTeamService.DisplayName(target.Team.Name, target.Members.Select(m => (m.Name, m.M.Relation)).ToList());
            pending = new FamilyPending(own.Id, own.FamilyTeamId, name, own.Relation);
        }
        return new FamilyState(true, me.ParticipantId, me.IsGuardian, mine, pending, invites);
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
        await DropInvitesToAsync(ctx, me.ParticipantId, ct);   // R-213
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
        Seat(team!, q!.ParticipantId, q.Relation);
        await DropInvitesToAsync(ctx, q.ParticipantId, ct);   // R-213
        return await SaveAsync(ctx, FamilyError.AlreadyInTeam, ct);
    }

    /// <summary>팀에 자리를 준다. 예전에 멈춘 자리가 있으면 되살린다.</summary>
    private static void Seat(FamilyTeam team, int participantId, string relation)
    {
        var order = (byte)(team.Members.Max(m => m.SortOrder) + 1);
        var old = team.Members.SingleOrDefault(m => m.ParticipantId == participantId);
        if (old is not null) { old.IsActive = true; old.Relation = relation; old.SortOrder = order; }
        else team.Members.Add(new FamilyMember { TenantId = team.TenantId, ParticipantId = participantId, Relation = relation, SortOrder = order });
    }

    private static async Task DropInvitesToAsync(AppDbContext ctx, int participantId, CancellationToken ct) =>
        ctx.FamilyInvites.RemoveRange(await ctx.FamilyInvites.Where(i => i.ParticipantId == participantId).ToListAsync(ct));

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

    // ── 초대 ──

    /// <summary>R-211: 같은 부서 참여자 중 초대할 수 있는 사람을 이름으로 찾는다. 팀 구성원만, 이름만 보인다.</summary>
    public async Task<IReadOnlyList<FamilyCandidate>> CandidatesAsync(string? q, CancellationToken ct = default)
    {
        var word = q?.Trim();
        if (string.IsNullOrEmpty(word) || !await EnabledAsync(ct)) return [];
        await using var ctx = db.Create();
        if (await MeAsync(ctx, ct) is not { } me || await MyTeamAsync(ctx, me, ct) is not { } team) return [];
        var rows = await (from p in ctx.Participants
                          join m in ctx.Memberships on p.MembershipId equals m.Id
                          join a in ctx.Accounts on m.AccountId equals a.Id
                          where p.IsActive && m.Status == MembershipStatus.Approved && a.DisplayName.Contains(word)
                              && !ctx.FamilyMembers.Any(f => f.ParticipantId == p.Id && f.IsActive)
                              && !ctx.FamilyInvites.Any(i => i.FamilyTeamId == team.Id && i.ParticipantId == p.Id)
                              && !ctx.FamilyJoinRequests.Any(r => r.FamilyTeamId == team.Id && r.ParticipantId == p.Id)
                          orderby a.DisplayName, p.Id
                          select new { p.Id, a.DisplayName, m.AccountId }).Take(SearchMax * 2).ToListAsync(ct);
        var elsewhere = await InOtherDeptAsync(rows.Select(x => x.AccountId).ToList(), ct);
        return rows.Where(x => !elsewhere.Contains(x.AccountId)).Take(SearchMax).Select(x => new FamilyCandidate(x.Id, x.DisplayName)).ToList();
    }

    /// <summary>R-211: 팀 구성원이 역할을 골라 초대한다. 상대가 수락해야 합류된다.</summary>
    public async Task<FamilyError> InviteAsync(int participantId, string relation, CancellationToken ct = default)
    {
        if (!await EnabledAsync(ct)) return FamilyError.Disabled;
        if (!FamilyRelation.All.Contains(relation)) return FamilyError.BadRelation;
        await using var ctx = db.Create();
        if (await MeAsync(ctx, ct) is not { } me) return FamilyError.NotParticipant;
        if (await MyTeamAsync(ctx, me, ct) is not { } team) return FamilyError.NotMember;
        var who = await (from p in ctx.Participants
                         join m in ctx.Memberships on p.MembershipId equals m.Id
                         where p.Id == participantId && p.IsActive && m.Status == MembershipStatus.Approved
                         select (int?)m.AccountId).SingleOrDefaultAsync(ct);
        if (who is null) return FamilyError.NotFound;
        if (await ctx.FamilyMembers.AnyAsync(f => f.ParticipantId == participantId && f.IsActive, ct)) return FamilyError.TargetInTeam;
        if (await ctx.FamilyJoinRequests.AnyAsync(r => r.FamilyTeamId == team.Id && r.ParticipantId == participantId, ct)) return FamilyError.TargetRequested;
        if (await ctx.FamilyInvites.AnyAsync(i => i.FamilyTeamId == team.Id && i.ParticipantId == participantId, ct)) return FamilyError.AlreadyInvited;
        if (await InOtherDeptAsync(who.Value, ct)) return FamilyError.TargetOtherDept;
        ctx.FamilyInvites.Add(new FamilyInvite { TenantId = tenant.Current.Id, FamilyTeamId = team.Id, ParticipantId = participantId,
            InvitedByParticipantId = me.ParticipantId, Relation = relation, CreatedUtc = time.GetUtcNow().UtcDateTime });
        return await SaveAsync(ctx, FamilyError.AlreadyInvited, ct);
    }

    /// <summary>R-211: 우리 팀이 보낸 초대는 구성원 누구든 취소한다.</summary>
    public async Task<FamilyError> CancelInviteAsync(int inviteId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        if (await MeAsync(ctx, ct) is not { } me) return FamilyError.NotParticipant;
        if (await MyTeamAsync(ctx, me, ct) is not { } team) return FamilyError.NotMember;
        var invite = await ctx.FamilyInvites.SingleOrDefaultAsync(i => i.Id == inviteId, ct);
        if (invite is null) return FamilyError.NotFound;
        if (invite.FamilyTeamId != team.Id) return FamilyError.NotMember;
        ctx.FamilyInvites.Remove(invite);
        await ctx.SaveChangesAsync(ct);
        return FamilyError.None;
    }

    /// <summary>R-212: 초대받은 본인이 역할을 확인하고 수락한다. 내 합류 신청과 받은 초대는 함께 지운다(R-213).</summary>
    public async Task<FamilyError> AcceptInviteAsync(int inviteId, string relation, CancellationToken ct = default)
    {
        if (!await EnabledAsync(ct)) return FamilyError.Disabled;
        if (!FamilyRelation.All.Contains(relation)) return FamilyError.BadRelation;
        await using var ctx = db.Create();
        if (await MeAsync(ctx, ct) is not { } me) return FamilyError.NotParticipant;
        var invite = await ctx.FamilyInvites.SingleOrDefaultAsync(i => i.Id == inviteId && i.ParticipantId == me.ParticipantId, ct);
        if (invite is null) return FamilyError.NotFound;
        if (await ctx.FamilyMembers.AnyAsync(f => f.ParticipantId == me.ParticipantId && f.IsActive, ct)) return FamilyError.AlreadyInTeam;
        if (await InOtherDeptAsync(me.AccountId, ct)) return FamilyError.InOtherDept;
        var team = await ctx.FamilyTeams.Include(t => t.Members).SingleOrDefaultAsync(t => t.Id == invite.FamilyTeamId && t.IsActive && t.Members.Any(m => m.IsActive), ct);
        if (team is null) return FamilyError.NotFound;
        Seat(team, me.ParticipantId, relation);
        await DropInvitesToAsync(ctx, me.ParticipantId, ct);
        ctx.FamilyJoinRequests.RemoveRange(await ctx.FamilyJoinRequests.Where(q => q.ParticipantId == me.ParticipantId).ToListAsync(ct));
        return await SaveAsync(ctx, FamilyError.AlreadyInTeam, ct);
    }

    /// <summary>R-212: 초대받은 본인이 거절한다. 기록을 남기지 않는다.</summary>
    public async Task<FamilyError> RejectInviteAsync(int inviteId, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        if (await MeAsync(ctx, ct) is not { } me) return FamilyError.NotParticipant;
        var invite = await ctx.FamilyInvites.SingleOrDefaultAsync(i => i.Id == inviteId && i.ParticipantId == me.ParticipantId, ct);
        if (invite is null) return FamilyError.NotFound;
        ctx.FamilyInvites.Remove(invite);
        await ctx.SaveChangesAsync(ct);
        return FamilyError.None;
    }

    /// <summary>R-212: "다음에 할래요". 지금 받은 초대를 오늘 하루 부서 홈 배너에서 숨긴다. 초대는 그대로 남고, 이후 새로 온 초대는 다시 보인다.</summary>
    public async Task<FamilyError> SnoozeInvitesAsync(CancellationToken ct = default)
    {
        if (!await EnabledAsync(ct)) return FamilyError.Disabled;
        await using var ctx = db.Create();
        if (await MeAsync(ctx, ct) is not { } me) return FamilyError.NotParticipant;
        var (_, today) = await teams.RulesAsync(ct);
        foreach (var i in await ctx.FamilyInvites.Where(i => i.ParticipantId == me.ParticipantId).ToListAsync(ct)) i.SnoozedOn = today;
        await ctx.SaveChangesAsync(ct);
        return FamilyError.None;
    }

    /// <summary>R-208: 나가기. 마지막 사람이면 팀도 사라진다.</summary>
    public async Task<FamilyError> LeaveAsync(CancellationToken ct = default) => (await LeaveReportAsync(ct)).Error;

    /// <summary>R-208: 나가기 + 팀이 함께 삭제됐는지(안내 글에 쓴다).</summary>
    public async Task<(FamilyError Error, bool TeamGone)> LeaveReportAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        if (await MeAsync(ctx, ct) is not { } me) return (FamilyError.NotParticipant, false);
        var team = await MyTeamAsync(ctx, me, ct);
        if (team is null) return (FamilyError.NotMember, false);
        var gone = await FamilyTeamService.DropMemberAsync(ctx, team, team.Members.Single(m => m.ParticipantId == me.ParticipantId), ct);
        await ctx.SaveChangesAsync(ct);
        return (FamilyError.None, gone);
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
