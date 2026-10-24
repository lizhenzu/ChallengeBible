using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Seasons;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record FamilyMemberView(int MemberId, int ParticipantId, string Name, string? GroupLabel, string Relation, int Points);
public sealed record FamilyStage(int Points, string Message, FamilyMilestoneKind Kind, bool Hidden);
public sealed record FamilyTeamView(int Id, string? Name, string DisplayName, string? Memo, bool IsActive, int Points,
    IReadOnlyList<FamilyMemberView> Members, IReadOnlyList<FamilyStage> Reached);
/// <summary>팀 없는 참여자. Blocked = 부모님이 가족 부서를 이 부서로 고르지 않아 팀에 넣을 수 없는 이유(0046).</summary>
public sealed record FamilySolo(int ParticipantId, string Name, string? GroupLabel, bool IsGuardian, int Points, FamilyError Blocked);
public sealed record FamilyBoard(bool Enabled, int Year, IReadOnlyList<FamilyTeamView> Teams, IReadOnlyList<FamilySolo> Solo)
{
    public int ActiveTeams => Teams.Count(t => t.IsActive);
    public int InTeams => Teams.Where(t => t.IsActive).Sum(t => t.Members.Count);
}

public enum FamilyError
{
    None, Disabled, NotFound, NoMember, NotParticipant, AlreadyInTeam, FamilyTenantNotChosen, FamilyTenantElsewhere, BadRelation, MembersElsewhere, NotReached,
}

/// <summary>
/// 가족팀 관리(확정 시안: 관리-가족팀, R-90 ~ R-97, 0012 · 0046). 이 부서 연결로만 읽고 쓴다(전역 필터 + RLS).
/// 팀 점수 = 활성 구성원 시즌 점수의 합(R-92). 기본 규칙 "가족팀" 스위치가 꺼지면 모든 동작을 막는다(0012).
/// 부모님(다른 부서 포함 어디서든 보호자 역할) 은 "가족과 함께할 부서"가 이 부서일 때만 넣는다 — 다른 부서의 역할을 봐야 해서 시스템 연결로 읽기만 한다.
/// </summary>
public sealed class FamilyTeamService(IAppDb db, ISystemDb systemDb, RequestScope scope, TenantContext tenant, TenantClock clock, RuleService rules, TimeProvider time)
{
    public static string RelationLabel(string r) => r switch
    {
        FamilyRelation.Student => "학생", FamilyRelation.Mother => "엄마", FamilyRelation.Father => "아빠",
        FamilyRelation.Grandmother => "할머니", FamilyRelation.Grandfather => "할아버지", _ => "기타",
    };

    /// <summary>R-93: 이름이 비면 "{대표 학생} 가족", 대표 학생 = 관계가 학생인 첫 구성원(없으면 첫 구성원).</summary>
    public static string DisplayName(string? name, IReadOnlyList<(string Name, string Relation)> members)
    {
        if (!string.IsNullOrWhiteSpace(name)) return name;
        var lead = members.FirstOrDefault(m => m.Relation == FamilyRelation.Student);
        if (lead.Name is null && members.Count > 0) lead = members[0];
        return lead.Name is null ? "가족팀" : $"{lead.Name} 가족";
    }

    private async Task<(RuleSet Rules, DateOnly Today)> RulesAsync(CancellationToken ct)
    {
        var today = clock.Today(tenant.Current.TimeZoneId);
        return (await rules.ForDateAsync(today, ct), today);
    }

    public async Task<bool> EnabledAsync(CancellationToken ct = default) => (await RulesAsync(ct)).Rules.Features.FamilyTeam;

    public async Task<FamilyBoard> GetAsync(CancellationToken ct = default)
    {
        var (r, today) = await RulesAsync(ct);
        if (!r.Features.FamilyTeam) return new FamilyBoard(false, today.Year, [], []);

        await using var ctx = db.Create();
        var people = await (from p in ctx.Participants
                            join m in ctx.Memberships on p.MembershipId equals m.Id
                            join a in ctx.Accounts on m.AccountId equals a.Id
                            where p.IsActive && m.Status == MembershipStatus.Approved
                            select new { p.Id, m.AccountId, a.DisplayName, m.GroupLabel, IsGuardian = m.Roles.Any(x => x.Role == Roles.Guardian) }).ToListAsync(ct);
        var points = await SeasonPointsAsync(ctx, people.Select(p => p.Id).ToList(), today, ct);

        var teams = await ctx.FamilyTeams.AsNoTracking().Include(t => t.Members).OrderBy(t => t.Id).ToListAsync(ct);
        var dismissed = (await ctx.FamilyMilestoneDismisses.Where(d => d.Year == today.Year).Select(d => new { d.FamilyTeamId, d.Threshold }).ToListAsync(ct))
            .Select(d => (d.FamilyTeamId, d.Threshold)).ToHashSet();
        var byId = people.ToDictionary(p => p.Id);
        var stages = r.FamilyMilestones.Where(m => m.Enabled).OrderBy(m => m.Points).ToList();

        var views = teams.Select(t =>
        {
            var members = t.Members.Where(m => byId.ContainsKey(m.ParticipantId)).OrderBy(m => m.SortOrder).ThenBy(m => m.Id)
                .Select(m => { var p = byId[m.ParticipantId]; return new FamilyMemberView(m.Id, m.ParticipantId, p.DisplayName, p.GroupLabel, m.Relation, points.GetValueOrDefault(m.ParticipantId)); })
                .ToList();
            var sum = members.Sum(m => m.Points);   // R-92 단순 합
            var reached = stages.Where(s => sum >= s.Points).Select(s => new FamilyStage(s.Points, s.Message, s.Kind, dismissed.Contains((t.Id, s.Points)))).ToList();
            return new FamilyTeamView(t.Id, t.Name, DisplayName(t.Name, members.Select(m => (m.Name, m.Relation)).ToList()), t.Memo, t.IsActive, sum, members, t.IsActive ? reached : []);
        }).ToList();

        var inTeam = teams.Where(t => t.IsActive).SelectMany(t => t.Members).Where(m => m.IsActive).Select(m => m.ParticipantId).ToHashSet();
        var soloPeople = people.Where(p => !inTeam.Contains(p.Id)).ToList();
        var blocks = await FamilyTenantChecksAsync(soloPeople.Select(p => p.AccountId).ToList(), ct);
        var ko = StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false);
        var solo = soloPeople
            .Select(p => new FamilySolo(p.Id, p.DisplayName, p.GroupLabel, p.IsGuardian, points.GetValueOrDefault(p.Id), blocks.GetValueOrDefault(p.AccountId)))
            .OrderBy(s => s.Blocked != FamilyError.None).ThenBy(s => s.Name, ko).ToList();
        return new FamilyBoard(true, today.Year, views, solo);
    }

    /// <summary>시즌 점수(시즌 시작일 반영) — 순위와 같은 계산(R-62 · R-80).</summary>
    private static async Task<Dictionary<int, int>> SeasonPointsAsync(AppDbContext ctx, List<int> ids, DateOnly today, CancellationToken ct)
    {
        var start = await ctx.TenantSeasons.Where(s => s.Year == today.Year).Select(s => (DateOnly?)s.StartDate).SingleOrDefaultAsync(ct);
        var season = start is { } d ? Season.Create(today.Year, d) : Season.DefaultFor(today.Year);
        var (from, to) = season.ScoringRange(today);
        if (from > to || ids.Count == 0) return [];
        return await ctx.PointLedgers.Where(l => ids.Contains(l.ParticipantId) && l.MissionDate >= from && l.MissionDate <= to)
            .GroupBy(l => l.ParticipantId).Select(g => new { g.Key, P = g.Sum(x => x.Points) }).ToDictionaryAsync(x => x.Key, x => x.P, ct);
    }

    /// <summary>
    /// 0046 · R-168: 어느 부서에서든 보호자 역할이 있는 계정은 가족 부서가 이 부서여야 가족팀에 들어간다.
    /// 다른 부서의 역할 · 계정의 가족 부서를 봐야 해서 시스템 연결로 읽기만 한다(관리자 확인은 화면에서 먼저).
    /// </summary>
    private async Task<Dictionary<int, FamilyError>> FamilyTenantChecksAsync(List<int> accountIds, CancellationToken ct)
    {
        if (accountIds.Count == 0) return [];
        await using var sys = systemDb.Create(scope.AccountId ?? 0);
        var guardians = await (from r in sys.MembershipRoles
                               join m in sys.Memberships on new { r.TenantId, Id = r.MembershipId } equals new { m.TenantId, m.Id }
                               where r.Role == Roles.Guardian && accountIds.Contains(m.AccountId)
                               select m.AccountId).Distinct().ToListAsync(ct);
        if (guardians.Count == 0) return [];
        var family = await sys.Accounts.Where(a => guardians.Contains(a.Id)).Select(a => new { a.Id, a.FamilyTenantId }).ToListAsync(ct);
        return family.ToDictionary(a => a.Id, a => a.FamilyTenantId is null ? FamilyError.FamilyTenantNotChosen
            : a.FamilyTenantId == tenant.Current.Id ? FamilyError.None : FamilyError.FamilyTenantElsewhere);
    }

    /// <summary>넣을 수 있는 참여자인지(이 부서 승인 · 활성 참여자, 활성 팀 없음, 0046). 문제가 없으면 None.</summary>
    private async Task<FamilyError> CheckJoinableAsync(AppDbContext ctx, IReadOnlyCollection<int> participantIds, int? exceptTeam, CancellationToken ct)
    {
        var ok = await (from p in ctx.Participants
                        join m in ctx.Memberships on p.MembershipId equals m.Id
                        where participantIds.Contains(p.Id) && p.IsActive && m.Status == MembershipStatus.Approved
                        select new { p.Id, m.AccountId }).ToListAsync(ct);
        if (ok.Count != participantIds.Count) return FamilyError.NotParticipant;
        if (await ctx.FamilyMembers.AnyAsync(f => participantIds.Contains(f.ParticipantId) && f.IsActive && f.FamilyTeamId != exceptTeam, ct)) return FamilyError.AlreadyInTeam;
        var blocks = await FamilyTenantChecksAsync(ok.Select(x => x.AccountId).ToList(), ct);
        return blocks.Values.FirstOrDefault(v => v != FamilyError.None);
    }

    private async Task<string> DefaultRelationAsync(AppDbContext ctx, int participantId, CancellationToken ct) =>
        await (from p in ctx.Participants join m in ctx.Memberships on p.MembershipId equals m.Id
               where p.Id == participantId select m.Roles.Any(x => x.Role == Roles.Guardian)).SingleAsync(ct)
            ? FamilyRelation.Mother : FamilyRelation.Student;

    public async Task<(FamilyError Error, int? TeamId)> CreateAsync(string? name, IReadOnlyCollection<int> participantIds, CancellationToken ct = default)
    {
        if (!await EnabledAsync(ct)) return (FamilyError.Disabled, null);
        var ids = participantIds.Distinct().ToList();
        if (ids.Count == 0) return (FamilyError.NoMember, null);
        await using var ctx = db.Create();
        var e = await CheckJoinableAsync(ctx, ids, null, ct);
        if (e != FamilyError.None) return (e, null);
        var t = new FamilyTeam { TenantId = tenant.Current.Id, Name = Clean(name, 50), CreatedUtc = time.GetUtcNow().UtcDateTime };
        byte order = 0;
        foreach (var id in ids)
            t.Members.Add(new FamilyMember { TenantId = t.TenantId, ParticipantId = id, Relation = await DefaultRelationAsync(ctx, id, ct), SortOrder = order++ });
        ctx.FamilyTeams.Add(t);
        await ctx.SaveChangesAsync(ct);
        return (FamilyError.None, t.Id);
    }

    /// <summary>이름 · 메모 · 구성원 관계 저장. relations: 구성원 Id → 관계.</summary>
    public async Task<FamilyError> SaveAsync(int teamId, string? name, string? memo, IReadOnlyDictionary<int, string> relations, CancellationToken ct = default)
    {
        if (relations.Values.Any(r => !FamilyRelation.All.Contains(r))) return FamilyError.BadRelation;
        return await WithTeamAsync(teamId, (ctx, t) =>
        {
            t.Name = Clean(name, 50);
            t.Memo = Clean(memo, 500);
            foreach (var m in t.Members) if (relations.TryGetValue(m.Id, out var r)) m.Relation = r;
            return Task.FromResult(FamilyError.None);
        }, ct);
    }

    public Task<FamilyError> AddMemberAsync(int teamId, int participantId, CancellationToken ct = default) =>
        WithTeamAsync(teamId, async (ctx, t) =>
        {
            if (!t.IsActive) return FamilyError.NotFound;
            var e = await CheckJoinableAsync(ctx, [participantId], t.Id, ct);
            if (e != FamilyError.None) return e;
            var order = (byte)(t.Members.Count == 0 ? 0 : t.Members.Max(m => m.SortOrder) + 1);
            var old = t.Members.SingleOrDefault(m => m.ParticipantId == participantId);
            if (old is not null) { old.IsActive = true; old.SortOrder = order; }
            else t.Members.Add(new FamilyMember { TenantId = t.TenantId, ParticipantId = participantId, Relation = await DefaultRelationAsync(ctx, participantId, ct), SortOrder = order });
            return FamilyError.None;
        }, ct);

    /// <summary>구성원 빼기 — 그 사람의 점수 · 기록은 그대로(점수는 참여자에 붙는다).</summary>
    public Task<FamilyError> RemoveMemberAsync(int teamId, int memberId, CancellationToken ct = default) =>
        WithTeamAsync(teamId, (ctx, t) =>
        {
            var m = t.Members.SingleOrDefault(x => x.Id == memberId);
            if (m is null) return Task.FromResult(FamilyError.NotFound);
            ctx.FamilyMembers.Remove(m);
            return Task.FromResult(FamilyError.None);
        }, ct);

    /// <summary>표시 순서를 한 칸 옮긴다(up = 위로).</summary>
    public Task<FamilyError> MoveAsync(int teamId, int memberId, bool up, CancellationToken ct = default) =>
        WithTeamAsync(teamId, (ctx, t) =>
        {
            var list = t.Members.OrderBy(m => m.SortOrder).ThenBy(m => m.Id).ToList();
            var i = list.FindIndex(m => m.Id == memberId);
            if (i < 0) return Task.FromResult(FamilyError.NotFound);
            var j = up ? i - 1 : i + 1;
            if (j >= 0 && j < list.Count) (list[i], list[j]) = (list[j], list[i]);
            for (var k = 0; k < list.Count; k++) list[k].SortOrder = (byte)k;
            return Task.FromResult(FamilyError.None);
        }, ct);

    /// <summary>팀 멈추기: 구성원도 멈춰 다른 팀에 들어갈 수 있게 한다(R-91). 다시 켜기는 구성원이 다른 팀에 없을 때만.</summary>
    public Task<FamilyError> SetActiveAsync(int teamId, bool active, CancellationToken ct = default) =>
        WithTeamAsync(teamId, async (ctx, t) =>
        {
            if (active)
            {
                var ids = t.Members.Select(m => m.ParticipantId).ToList();
                if (await ctx.FamilyMembers.AnyAsync(f => ids.Contains(f.ParticipantId) && f.IsActive && f.FamilyTeamId != t.Id, ct)) return FamilyError.MembersElsewhere;
            }
            t.IsActive = active;
            foreach (var m in t.Members) m.IsActive = active;
            return FamilyError.None;
        }, ct);

    /// <summary>R-97: 넘은 가족 단계의 홈 축하 카드 숨기기 · 다시 보이기(그해 시즌).</summary>
    public async Task<FamilyError> SetStageHiddenAsync(int actor, int teamId, int threshold, bool hidden, CancellationToken ct = default)
    {
        var board = await GetAsync(ct);
        if (!board.Enabled) return FamilyError.Disabled;
        var team = board.Teams.SingleOrDefault(t => t.Id == teamId);
        if (team is null) return FamilyError.NotFound;
        if (team.Reached.All(s => s.Points != threshold)) return FamilyError.NotReached;
        await using var ctx = db.Create();
        var year = (short)board.Year;
        var row = await ctx.FamilyMilestoneDismisses.SingleOrDefaultAsync(d => d.FamilyTeamId == teamId && d.Year == year && d.Threshold == threshold, ct);
        if (hidden && row is null)
            ctx.FamilyMilestoneDismisses.Add(new FamilyMilestoneDismiss { TenantId = tenant.Current.Id, FamilyTeamId = teamId, Year = year, Threshold = threshold,
                DismissedUtc = time.GetUtcNow().UtcDateTime, DismissedByAccountId = actor });
        else if (!hidden && row is not null) ctx.FamilyMilestoneDismisses.Remove(row);
        await ctx.SaveChangesAsync(ct);
        return FamilyError.None;
    }

    private async Task<FamilyError> WithTeamAsync(int teamId, Func<AppDbContext, FamilyTeam, Task<FamilyError>> work, CancellationToken ct)
    {
        if (!await EnabledAsync(ct)) return FamilyError.Disabled;
        await using var ctx = db.Create();
        var t = await ctx.FamilyTeams.Include(x => x.Members).SingleOrDefaultAsync(x => x.Id == teamId, ct);
        if (t is null) return FamilyError.NotFound;
        var e = await work(ctx, t);
        if (e == FamilyError.None) await ctx.SaveChangesAsync(ct);
        return e;
    }

    private static string? Clean(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(s.Trim().Length, max)];

    public static string Message(FamilyError e) => e switch
    {
        FamilyError.Disabled => "기본 규칙에서 가족팀이 꺼져 있어요.",
        FamilyError.NotFound => "이 부서에서 찾을 수 없어요. 화면을 새로 고쳐 주세요.",
        FamilyError.NoMember => "구성원을 한 명 이상 골라 주세요.",
        FamilyError.NotParticipant => "이 부서의 승인된 참여자만 넣을 수 있어요.",
        FamilyError.AlreadyInTeam => "이미 다른 가족팀에 들어 있어요. 한 사람은 한 팀에만 들어가요.",
        FamilyError.FamilyTenantNotChosen => "부모님이 아직 가족과 함께할 부서를 고르지 않았어요.",
        FamilyError.FamilyTenantElsewhere => "이 부모님은 다른 부서를 가족과 함께할 부서로 골랐어요.",
        FamilyError.BadRelation => "관계를 다시 골라 주세요.",
        FamilyError.MembersElsewhere => "다른 팀에 들어간 구성원이 있어 다시 켤 수 없어요.",
        FamilyError.NotReached => "아직 넘지 않은 단계예요.",
        _ => "",
    };
}
