using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Seasons;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record FamilyMemberView(int MemberId, int ParticipantId, string Name, string? GroupLabel, string Relation, int Points, int UserName = 0);
public sealed record FamilyTeamView(int Id, string? Name, string DisplayName, bool IsActive, int Points, IReadOnlyList<FamilyMemberView> Members);
/// <summary>팀 없는 참여자.</summary>
public sealed record FamilySolo(int ParticipantId, string Name, string? GroupLabel, bool IsGuardian, int Points);
public sealed record FamilyBoard(bool Enabled, int Year, IReadOnlyList<FamilyTeamView> Teams, IReadOnlyList<FamilySolo> Solo)
{
    public int ActiveTeams => Teams.Count(t => t.IsActive);
    public int InTeams => Teams.Where(t => t.IsActive).Sum(t => t.Members.Count);
}

/// <summary>홈 "우리 가족팀" 칸의 구성원 한 줄. TodayMissions = 오늘 한 미션 수(0 ~ 4, 암송은 이번 주).</summary>
public sealed record FamilyHomeMember(string Name, string Relation, int Season, int Today, int TodayMissions, bool Me);
/// <summary>가족 단계 하나(기본 규칙). On = 이번 시즌 팀 합계가 넘음.</summary>
public sealed record FamilyStageChip(int Points, string Emoji, string Message, FamilyMilestoneKind Kind, bool On);
/// <summary>NextStage = 다음 가족 축하 점수(모든 단계를 넘었으면 null). Cheer = 홈 축하 카드(숨기지 않은 가장 높은 넘은 단계, R-97).</summary>
public sealed record FamilyHome(string Name, int Year, int Total, int TodayPoints, int TodayPeople, int? NextStage, int PrevStage,
    IReadOnlyList<FamilyHomeMember> Members)
{
    public IReadOnlyList<FamilyStageChip> Stages { get; init; } = [];
    public FamilyStageChip? Cheer { get; init; }
    public DateOnly? CheerDate { get; init; }
}
/// <summary>미션 직후 가족 축하 팝업(R-96): 이 점수로 팀 합계가 처음 넘은 가장 높은 단계. By = 이번 점수로 단계를 넘긴 구성원(시안 "○○님이 넘겼어요").</summary>
public sealed record FamilyCelebration(string Team, int Points, string Emoji, string Message, string Kind, int Total, int? Next,
    IReadOnlyList<FamilyCelebrationMember> Members, string? By = null);
public sealed record FamilyCelebrationMember(string Name, int Points);
/// <summary>순위 "가족팀" 탭의 팀 카드(R-94). Last = 구성원 중 가장 최근 점수일.</summary>
public sealed record FamilyRankTeam(int Rank, string Name, int Points, DateOnly? Last, int? NextStage, bool Mine,
    IReadOnlyList<(string Name, string Relation, int Points, bool TodayDone, bool Me)> Members)
{
    /// <summary>넘은 가족 단계 배지(숨김과 관계없이 그대로 — 시안 가족축하 ③).</summary>
    public IReadOnlyList<FamilyStageChip> Badges { get; init; } = [];
}
public sealed record FamilyRanking(bool Enabled, int Year, DateOnly Today, int FirstStage, IReadOnlyList<FamilyRankTeam> Teams)
{
    public int ReachedFirst => FirstStage <= 0 ? 0 : Teams.Count(t => t.Points >= FirstStage);
}

public enum FamilyError
{
    None, Disabled, NotFound, NotParticipant, AlreadyInTeam, BadRelation, HasPending, InOtherDept, NotMember,
    TargetInTeam, TargetRequested, AlreadyInvited, TargetOtherDept,
}

/// <summary>
/// 가족팀 보기와 관리자 바로잡기(R-90 ~ R-97, 0012 · 0083). 이 부서 연결로만 읽고 쓴다(전역 필터 + RLS).
/// 팀 점수 = 활성 구성원 시즌 점수의 합(R-92). 기본 규칙 "가족팀" 스위치가 꺼지면 모든 동작을 막는다(0012).
/// 가족이 직접 하는 만들기 · 합류는 FamilyJoinService.
/// </summary>
public sealed class FamilyTeamService(IAppDb db, TenantContext tenant, TenantClock clock, RuleService rules, TimeProvider time)
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

    internal async Task<(RuleSet Rules, DateOnly Today)> RulesAsync(CancellationToken ct)
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
                            join a in ctx.Users on m.UserName equals a.Id
                            where p.IsActive && m.Status == MembershipStatus.Approved
                            select new { p.Id, m.UserName, a.DisplayName, m.GroupLabel, IsGuardian = m.Roles.Any(x => x.Role == Roles.Guardian) }).ToListAsync(ct);
        var points = await SeasonPointsAsync(ctx, people.Select(p => p.Id).ToList(), today, ct);

        var teams = await ctx.FamilyTeams.AsNoTracking().Include(t => t.Members).OrderBy(t => t.Id).ToListAsync(ct);
        var byId = people.ToDictionary(p => p.Id);

        var views = teams.Select(t =>
        {
            var members = t.Members.Where(m => byId.ContainsKey(m.ParticipantId)).OrderBy(m => m.SortOrder).ThenBy(m => m.Id)
                .Select(m => { var p = byId[m.ParticipantId]; return new FamilyMemberView(m.Id, m.ParticipantId, p.DisplayName, p.GroupLabel, m.Relation, points.GetValueOrDefault(m.ParticipantId), p.UserName); })
                .ToList();
            var sum = members.Sum(m => m.Points);   // R-92 단순 합
            return new FamilyTeamView(t.Id, t.Name, DisplayName(t.Name, members.Select(m => (m.Name, m.Relation)).ToList()), t.IsActive, sum, members);
        }).ToList();

        var inTeam = teams.Where(t => t.IsActive).SelectMany(t => t.Members).Where(m => m.IsActive).Select(m => m.ParticipantId).ToHashSet();
        var ko = StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false);
        var solo = people.Where(p => !inTeam.Contains(p.Id))
            .Select(p => new FamilySolo(p.Id, p.DisplayName, p.GroupLabel, p.IsGuardian, points.GetValueOrDefault(p.Id)))
            .OrderBy(s => s.Name, ko).ToList();
        return new FamilyBoard(true, today.Year, views, solo);
    }

    /// <summary>시즌 점수(시즌 시작일 반영) — 순위와 같은 계산(R-62 · R-80).</summary>
    internal static async Task<Dictionary<int, int>> SeasonPointsAsync(AppDbContext ctx, List<int> ids, DateOnly today, CancellationToken ct)
    {
        var start = await ctx.TenantSeasons.Where(s => s.Year == today.Year).Select(s => (DateOnly?)s.StartDate).SingleOrDefaultAsync(ct);
        var season = start is { } d ? Season.Create(today.Year, d) : Season.DefaultFor(today.Year);
        var (from, to) = season.ScoringRange(today);
        if (from > to || ids.Count == 0) return [];
        return await ctx.PointLedgers.Where(l => ids.Contains(l.ParticipantId) && l.MissionDate >= from && l.MissionDate <= to)
            .GroupBy(l => l.ParticipantId).Select(g => new { g.Key, P = g.Sum(x => x.Points) }).ToDictionaryAsync(x => x.Key, x => x.P, ct);
    }

    /// <summary>관리자 바로잡기(R-210): 팀 이름 고치기. 비우면 R-93.</summary>
    public Task<FamilyError> RenameAsync(int teamId, string? name, CancellationToken ct = default) =>
        WithTeamAsync(teamId, (ctx, t) =>
        {
            t.Name = Clean(name, 50);
            return Task.FromResult(FamilyError.None);
        }, ct);

    /// <summary>관리자 바로잡기(R-210): 구성원 빼기 — 그 사람의 점수 · 기록은 그대로(점수는 참여자에 붙는다). 마지막 사람이면 팀도 사라진다(R-208).</summary>
    public Task<FamilyError> RemoveMemberAsync(int teamId, int memberId, CancellationToken ct = default) =>
        WithTeamAsync(teamId, async (ctx, t) =>
        {
            var m = t.Members.SingleOrDefault(x => x.Id == memberId);
            if (m is null) return FamilyError.NotFound;
            await DropMemberAsync(ctx, t, m, ct);
            return FamilyError.None;
        }, ct);

    /// <summary>R-208: 구성원 한 줄을 지운다. 활성 구성원이 더 없으면 팀 · 축하 기록 · 신청도 지우고 true. 저장은 부르는 쪽에서.</summary>
    internal static async Task<bool> DropMemberAsync(AppDbContext ctx, FamilyTeam t, FamilyMember m, CancellationToken ct)
    {
        ctx.FamilyMembers.Remove(m);
        var rest = t.Members.Where(x => x.Id != m.Id).ToList();
        if (rest.Count > 0 && !(t.IsActive && rest.All(x => !x.IsActive))) return false;
        ctx.FamilyMembers.RemoveRange(rest);
        ctx.FamilyJoinRequests.RemoveRange(await ctx.FamilyJoinRequests.Where(q => q.FamilyTeamId == t.Id).ToListAsync(ct));
        ctx.FamilyInvites.RemoveRange(await ctx.FamilyInvites.Where(i => i.FamilyTeamId == t.Id).ToListAsync(ct));
        ctx.FamilyMilestonesReached.RemoveRange(await ctx.FamilyMilestonesReached.Where(x => x.FamilyTeamId == t.Id).ToListAsync(ct));
        ctx.FamilyMilestoneDismisses.RemoveRange(await ctx.FamilyMilestoneDismisses.Where(x => x.FamilyTeamId == t.Id).ToListAsync(ct));
        ctx.FamilyTeams.Remove(t);
        return true;
    }

    // ── 보기(홈 "우리 가족팀" · 순위 "가족팀" 탭) ──

    /// <summary>활성 팀과 구성원(이 부서 승인 · 활성 참여자만), 구성원 시즌 점수 · 오늘 점수 · 마지막 점수일.</summary>
    internal async Task<(List<(FamilyTeam Team, List<(FamilyMember M, string Name, int Pid)> Members)> Teams,
        Dictionary<int, int> Season, Dictionary<int, int> Today, Dictionary<int, DateOnly> Last)> LoadTeamsAsync(AppDbContext ctx, DateOnly today, CancellationToken ct)
    {
        var names = await (from p in ctx.Participants
                           join m in ctx.Memberships on p.MembershipId equals m.Id
                           join a in ctx.Users on m.UserName equals a.Id
                           where p.IsActive && m.Status == MembershipStatus.Approved
                           select new { p.Id, a.DisplayName, p.MembershipId }).ToDictionaryAsync(x => x.Id, ct);
        var teams = (await ctx.FamilyTeams.AsNoTracking().Include(t => t.Members).Where(t => t.IsActive).OrderBy(t => t.Id).ToListAsync(ct))
            .Select(t => (Team: t, Members: t.Members.Where(m => m.IsActive && names.ContainsKey(m.ParticipantId)).OrderBy(m => m.SortOrder).ThenBy(m => m.Id)
                .Select(m => (M: m, Name: names[m.ParticipantId].DisplayName, Pid: m.ParticipantId)).ToList()))
            .Where(t => t.Members.Count > 0).ToList();
        var ids = teams.SelectMany(t => t.Members.Select(m => m.Pid)).Distinct().ToList();
        var season = await SeasonPointsAsync(ctx, ids, today, ct);
        var todayPts = await ctx.PointLedgers.Where(l => ids.Contains(l.ParticipantId) && l.MissionDate == today)
            .GroupBy(l => l.ParticipantId).Select(g => new { g.Key, P = g.Sum(x => x.Points) }).ToDictionaryAsync(x => x.Key, x => x.P, ct);
        var last = await ctx.PointLedgers.Where(l => ids.Contains(l.ParticipantId) && l.MissionDate <= today && l.Points > 0)
            .GroupBy(l => l.ParticipantId).Select(g => new { g.Key, D = g.Max(x => x.MissionDate) }).ToDictionaryAsync(x => x.Key, x => x.D, ct);
        return (teams, season, todayPts, last);
    }

    internal static int? Next(RuleSet r, int total) =>
        r.FamilyMilestones.Where(m => m.Enabled && m.Points > total).OrderBy(m => m.Points).Select(m => (int?)m.Points).FirstOrDefault();

    /// <summary>홈 "우리 가족팀"(시안 홈메인): 나(이 소속)의 참여자가 든 활성 팀. 없거나 스위치가 꺼지면 null.</summary>
    public async Task<FamilyHome?> MyFamilyAsync(CancellationToken ct = default)
    {
        var (r, today) = await RulesAsync(ct);
        if (!r.Features.FamilyTeam || tenant.MembershipId is not { } me) return null;
        await using var ctx = db.Create();
        var myPid = await ctx.Participants.Where(p => p.MembershipId == me && p.IsActive).Select(p => (int?)p.Id).SingleOrDefaultAsync(ct);
        if (myPid is null) return null;
        var (teams, season, todayPts, _) = await LoadTeamsAsync(ctx, today, ct);
        var mine = teams.FirstOrDefault(t => t.Members.Any(m => m.Pid == myPid));
        if (mine.Team is null) return null;

        // 오늘 한 미션 수: 말씀 · 퀴즈 · 기도는 오늘, 암송은 이번 주(일요일 시작) — 관리 홈과 같은 셈
        var pids = mine.Members.Select(m => m.Pid).ToList();
        var weekStart = today.AddDays(-(int)today.DayOfWeek);
        var acts = (await ctx.Activities.AsNoTracking().Where(a => pids.Contains(a.ParticipantId) && a.MissionDate >= weekStart && a.MissionDate <= today)
                .Select(a => new { a.ParticipantId, a.MissionDate, a.MissionItem }).ToListAsync(ct))
            .Select(a => (a.ParticipantId, a.MissionDate, Mission: MissionItemCodes.Parse(a.MissionItem).Mission())).ToList();
        int Done(int pid) => acts.Where(a => a.ParticipantId == pid && (a.MissionDate == today || a.Mission == Mission.Recite)).Select(a => a.Mission).Distinct().Count();

        var members = mine.Members.Select(m => new FamilyHomeMember(m.Name, m.M.Relation, season.GetValueOrDefault(m.Pid), todayPts.GetValueOrDefault(m.Pid), Done(m.Pid), m.Pid == myPid)).ToList();
        var total = members.Sum(m => m.Season);   // R-92
        var prev = r.FamilyMilestones.Where(s => s.Enabled && s.Points <= total).Select(s => s.Points).DefaultIfEmpty(0).Max();

        // 축하 카드(R-97): 넘은 단계 중 관리자가 숨기지 않은 가장 높은 것. 날짜는 도달 기록이 있으면 그날
        var year = (short)today.Year;
        var hidden = (await ctx.FamilyMilestoneDismisses.Where(d => d.FamilyTeamId == mine.Team.Id && d.Year == year).Select(d => d.Threshold).ToListAsync(ct)).ToHashSet();
        var chips = Chips(r, total);
        var cheer = chips.Where(c => c.On && !hidden.Contains(c.Points)).OrderByDescending(c => c.Points).FirstOrDefault();
        DateOnly? cheerDate = null;
        if (cheer is not null)
        {
            var at = await ctx.FamilyMilestonesReached.Where(x => x.FamilyTeamId == mine.Team.Id && x.Year == year && x.Threshold == cheer.Points)
                .Select(x => (DateTime?)x.ReachedUtc).SingleOrDefaultAsync(ct);
            if (at is { } u) cheerDate = TenantClock.ToTenantDate(new DateTimeOffset(DateTime.SpecifyKind(u, DateTimeKind.Utc)), tenant.Current.TimeZoneId);
        }
        return new FamilyHome(DisplayName(mine.Team.Name, members.Select(m => (m.Name, m.Relation)).ToList()), today.Year, total,
            members.Sum(m => m.Today), members.Count(m => m.Today > 0), Next(r, total), prev, members)
        { Stages = chips, Cheer = cheer, CheerDate = cheerDate };
    }

    /// <summary>켜진 가족 단계(점수순)와 팀 합계로 넘었는지.</summary>
    private static List<FamilyStageChip> Chips(RuleSet r, int total) =>
        r.FamilyMilestones.Where(m => m.Enabled).OrderBy(m => m.Points).Select(m => new FamilyStageChip(m.Points, m.Emoji, m.Message, m.Kind, total >= m.Points)).ToList();

    /// <summary>
    /// R-96: 이 참여자가 점수를 받은 직후 부른다. 그의 활성 가족팀 합계가 새로 넘은 단계를 시즌 · 팀마다 한 번 기록하고,
    /// 가장 높은 새 단계로 축하 팝업 내용을 돌려준다(없으면 null). 스위치가 꺼졌거나 팀이 없으면 아무것도 하지 않는다.
    /// </summary>
    public async Task<FamilyCelebration?> RecordReachedAsync(int participantId, CancellationToken ct = default)
    {
        var (r, today) = await RulesAsync(ct);
        if (!r.Features.FamilyTeam) return null;
        await using var ctx = db.Create();
        var (teams, season, _, _) = await LoadTeamsAsync(ctx, today, ct);
        var team = teams.FirstOrDefault(t => t.Members.Any(m => m.Pid == participantId));
        if (team.Team is null) return null;
        var total = team.Members.Sum(m => season.GetValueOrDefault(m.Pid));
        var year = (short)today.Year;
        var done = await ctx.FamilyMilestonesReached.Where(x => x.FamilyTeamId == team.Team.Id && x.Year == year).Select(x => x.Threshold).ToListAsync(ct);
        var fresh = r.FamilyMilestones.Where(m => m.Enabled && m.Points <= total && !done.Contains(m.Points)).OrderBy(m => m.Points).ToList();
        if (fresh.Count == 0) return null;
        foreach (var m in fresh)
            ctx.FamilyMilestonesReached.Add(new FamilyMilestoneReached { TenantId = tenant.Current.Id, FamilyTeamId = team.Team.Id, Year = year,
                Threshold = m.Points, ReachedUtc = time.GetUtcNow().UtcDateTime });
        try { await ctx.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return null; }   // 같은 팀의 다른 사람이 동시에 기록함 — 그쪽이 축하를 본다
        var top = fresh[^1];
        return new FamilyCelebration(DisplayName(team.Team.Name, team.Members.Select(m => (m.Name, m.M.Relation)).ToList()),
            top.Points, top.Emoji, top.Message, top.Kind.ToString(), total, Next(r, total),
            team.Members.Select(m => new FamilyCelebrationMember(m.Name, season.GetValueOrDefault(m.Pid))).ToList(),
            By: team.Members.FirstOrDefault(m => m.Pid == participantId).Name);
    }

    /// <summary>완료 취소(R-24, ADR 0070)로 가족 합계가 줄면 그 아래로 내려간 가족 축하 도달 기록을 지운다(다시 넘으면 또 축하).</summary>
    public async Task DropUnreachedAsync(int participantId, CancellationToken ct = default)
    {
        var (r, today) = await RulesAsync(ct);
        if (!r.Features.FamilyTeam) return;
        await using var ctx = db.Create();
        var (teams, season, _, _) = await LoadTeamsAsync(ctx, today, ct);
        var team = teams.FirstOrDefault(t => t.Members.Any(m => m.Pid == participantId));
        if (team.Team is null) return;
        var total = team.Members.Sum(m => season.GetValueOrDefault(m.Pid));
        var year = (short)today.Year;
        var above = await ctx.FamilyMilestonesReached.Where(x => x.FamilyTeamId == team.Team.Id && x.Year == year && x.Threshold > total).ToListAsync(ct);
        if (above.Count == 0) return;
        ctx.FamilyMilestonesReached.RemoveRange(above);
        await ctx.SaveChangesAsync(ct);
    }

    /// <summary>순위 "가족팀" 탭(시안 말씀-순위, R-92 · R-94): 활성 팀을 팀 점수순(같으면 이름순).</summary>
    public async Task<FamilyRanking> RankingAsync(CancellationToken ct = default)
    {
        var (r, today) = await RulesAsync(ct);
        var first = r.FamilyMilestones.Where(m => m.Enabled).Select(m => m.Points).DefaultIfEmpty(0).Min();
        if (!r.Features.FamilyTeam) return new FamilyRanking(false, today.Year, today, first, []);
        await using var ctx = db.Create();
        var myPid = tenant.MembershipId is { } me
            ? await ctx.Participants.Where(p => p.MembershipId == me && p.IsActive).Select(p => (int?)p.Id).SingleOrDefaultAsync(ct) : null;
        var (teams, season, todayPts, last) = await LoadTeamsAsync(ctx, today, ct);
        var ko = StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false);
        var rows = teams.Select(t =>
            {
                var name = DisplayName(t.Team.Name, t.Members.Select(m => (m.Name, m.M.Relation)).ToList());
                var pts = t.Members.Sum(m => season.GetValueOrDefault(m.Pid));
                var lastDay = t.Members.Select(m => last.TryGetValue(m.Pid, out var d) ? (DateOnly?)d : null).Max();
                var members = t.Members.Select(m => (m.Name, m.M.Relation, season.GetValueOrDefault(m.Pid), todayPts.GetValueOrDefault(m.Pid) > 0, m.Pid == myPid)).ToList();
                return (name, pts, lastDay, mine: t.Members.Any(m => m.Pid == myPid), members);
            })
            .OrderByDescending(x => x.pts).ThenBy(x => x.name, ko)
            .Select((x, i) => new FamilyRankTeam(i + 1, x.name, x.pts, x.lastDay, Next(r, x.pts), x.mine, x.members) { Badges = Chips(r, x.pts).Where(c => c.On).ToList() }).ToList();
        return new FamilyRanking(true, today.Year, today, first, rows);
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

    internal static string? Clean(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(s.Trim().Length, max)];

    /// <summary>지난 단계에서 다음 단계까지 온 정도(0 ~ 100). 모든 단계를 넘었으면 100.</summary>
    public static double StagePct(RuleSet r, int total)
    {
        if (Next(r, total) is not { } next) return 100;
        var prev = r.FamilyMilestones.Where(s => s.Enabled && s.Points <= total).Select(s => s.Points).DefaultIfEmpty(0).Max();
        return next <= prev ? 0 : Math.Clamp((total - prev) * 100.0 / (next - prev), 0, 100);
    }

    public static string Message(FamilyError e) => e switch
    {
        FamilyError.Disabled => "가족팀을 쓸 수 없어요.",
        FamilyError.NotFound => "찾을 수 없어요. 화면을 새로 고쳐 주세요.",
        FamilyError.NotParticipant => "이 부서의 참여자만 할 수 있어요.",
        FamilyError.AlreadyInTeam => "이미 가족팀에 들어 있어요. 다른 팀에 가려면 먼저 나와 주세요.",
        FamilyError.BadRelation => "나는 누구인지 골라 주세요.",
        FamilyError.HasPending => "이미 신청한 팀이 있어요. 신청을 취소한 뒤 다시 해 주세요.",
        FamilyError.InOtherDept => "다른 부서의 가족팀에 들어 있어요. 한 사람은 한 부서의 가족팀에만 들어가요.",
        FamilyError.NotMember => "이 팀의 구성원만 할 수 있어요.",
        FamilyError.TargetInTeam => "그분은 이미 가족팀에 들어 있어요.",
        FamilyError.TargetRequested => "그분이 이미 합류를 신청했어요. 우리 가족팀 화면에서 수락해 주세요.",
        FamilyError.AlreadyInvited => "이미 초대를 보냈어요.",
        FamilyError.TargetOtherDept => "그분은 다른 부서의 가족팀에 들어 있어요.",
        _ => "",
    };
}
