using ChallengeBible.Domain.Seasons;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>구분(확정: 학생 = 참여자만, 부모님 = 보호자 역할도, 선생님 = 교사 · 관리자 역할도).</summary>
public enum RankKind { Student, Parent, Teacher }

public sealed record RankRow(int ParticipantId, string Name, string? GroupLabel, RankKind Kind, int Points, double Percent, DateOnly? LastDate, int Streak, int Rank, bool Me);
public sealed record Ranking(bool Enabled, int Year, int Target, DateOnly Today, IReadOnlyList<RankRow> Rows)
{
    public int Achieved => Rows.Count(r => r.Points >= Target && Target > 0);
}

/// <summary>
/// 참여자 순위(확정 시안: 말씀-순위 "개인", R-80 ~ R-84). 이 부서의 승인 소속 · 활성 참여자 가운데 시즌 점수 > 0 인 사람(R-80).
/// 점수 내림차순, 같으면 이름순. 달성률 = 시즌 점수 ÷ 목표(가장 높은 축하 단계, R-82), 0 ~ 100% 로 자른다(R-81).
/// 연속 참여일(R-83) = 마지막 점수일부터 거꾸로 하루도 빠짐없이 점수가 있었던 날 수. 기본 규칙의 "순위" 스위치가 꺼지면 보이지 않는다.
/// </summary>
public sealed class RankingService(IAppDb db, TenantContext tenant, TenantClock clock, RuleService rules)
{
    public async Task<Ranking> GetAsync(CancellationToken ct = default)
    {
        var today = clock.Today(tenant.Current.TimeZoneId);
        var r = await rules.ForDateAsync(today, ct);
        if (!r.Features.Ranking) return new Ranking(false, today.Year, r.TargetPoints, today, []);

        await using var ctx = db.Create();
        var start = await ctx.TenantSeasons.Where(s => s.Year == today.Year).Select(s => (DateOnly?)s.StartDate).SingleOrDefaultAsync(ct);
        var season = start is { } d ? Season.Create(today.Year, d) : Season.DefaultFor(today.Year);
        var (from, to) = season.ScoringRange(today);

        var people = await (from p in ctx.Participants
                            join m in ctx.Memberships.Include(x => x.Roles) on p.MembershipId equals m.Id
                            join a in ctx.Accounts on m.AccountId equals a.Id
                            where p.IsActive && m.Status == MembershipStatus.Approved
                            select new { p.Id, a.DisplayName, m.GroupLabel, Roles = m.Roles.Select(x => x.Role).ToList(), MembershipId = m.Id }).ToListAsync(ct);
        var ids = people.Select(p => p.Id).ToList();
        var points = from > to ? [] : await ctx.PointLedgers.Where(l => ids.Contains(l.ParticipantId) && l.MissionDate >= from && l.MissionDate <= to)
            .GroupBy(l => l.ParticipantId).Select(g => new { g.Key, P = g.Sum(x => x.Points) }).ToListAsync(ct);
        var scored = points.Where(x => x.P > 0).ToDictionary(x => x.Key, x => x.P);
        var scoredIds = scored.Keys.ToList();
        // 마지막 참여일 · 연속(R-83): 점수 있는 날짜(최근 400일)
        var since = today.AddDays(-400);
        var days = (await ctx.PointLedgers.Where(l => scoredIds.Contains(l.ParticipantId) && l.MissionDate >= since && l.MissionDate <= today && l.Points > 0)
            .Select(l => new { l.ParticipantId, l.MissionDate }).Distinct().ToListAsync(ct))
            .GroupBy(x => x.ParticipantId).ToDictionary(g => g.Key, g => g.Select(x => x.MissionDate).ToHashSet());

        var ordered = people.Where(p => scored.ContainsKey(p.Id))
            .Select(p =>
            {
                var set = days.GetValueOrDefault(p.Id) ?? [];
                DateOnly? last = set.Count == 0 ? null : set.Max();
                var streak = 0;
                if (last is { } l) for (var x = l; set.Contains(x); x = x.AddDays(-1)) streak++;
                var kind = p.Roles.Any(x => x is Roles.Teacher or Roles.TenantAdmin) ? RankKind.Teacher
                    : p.Roles.Contains(Roles.Guardian) ? RankKind.Parent : RankKind.Student;
                var pts = scored[p.Id];
                return (p, kind, pts, last, streak);
            })
            .OrderByDescending(x => x.pts).ThenBy(x => x.p.DisplayName, StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false))
            .ToList();
        var rows = ordered.Select((x, i) => new RankRow(x.p.Id, x.p.DisplayName, x.p.GroupLabel, x.kind, x.pts,
            r.TargetPoints <= 0 ? 0 : Math.Min(100, x.pts * 100.0 / r.TargetPoints), x.last, x.streak, i + 1, x.p.MembershipId == tenant.MembershipId)).ToList();
        return new Ranking(true, today.Year, r.TargetPoints, today, rows);
    }

    /// <summary>화면 거르기 · 정렬(순위 번호는 점수순 그대로).</summary>
    public static IEnumerable<RankRow> View(IEnumerable<RankRow> rows, string? kind, string? q, string? sort)
    {
        var f = rows.Where(r => kind switch { "student" => r.Kind == RankKind.Student, "parent" => r.Kind == RankKind.Parent, "teacher" => r.Kind == RankKind.Teacher, _ => true })
            .Where(r => string.IsNullOrWhiteSpace(q) || r.Name.Contains(q.Trim()));
        var ko = StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false);
        return sort switch
        {
            "recent" => f.OrderByDescending(r => r.LastDate ?? DateOnly.MinValue).ThenByDescending(r => r.Points),
            "name" => f.OrderBy(r => r.Name, ko),
            _ => f.OrderBy(r => r.Rank),
        };
    }
}
