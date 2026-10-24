using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record BoardMember(int AccountId, string Name, string Badge, IReadOnlyList<string> Roles, int RoundNo, int Completed,
    int ReadChapters, double Percent, byte? LastBook, short? LastChapter, DateTime? LastUtc, int? DaysAgo, bool Me);

public sealed record ReadingBoard(bool Public, bool CanSee, int Members, int Readers, int Finishers, double AvgPercent, int TotalCompleted,
    int ReadThisWeek, DateOnly Today, IReadOnlyList<BoardMember> Rows, IReadOnlyList<BoardMember> HallOfFame);

/// <summary>
/// 통독 참여자 현황(R-159, 0041) · 관리자 통독 현황(0040 규칙 7). 점수는 없고 진도율만(R-157).
/// 보이는 사람: 이 부서의 승인 소속원뿐(앱에서 소속으로 거르고, DB 는 sec.fn_ReadingRead 가 한 번 더 막는다).
/// 부서가 공개를 끄면 관리자 · 교사만 본다. 통독 기록은 계정에 붙어 다른 부서에서 읽은 것도 함께 보인다(0040).
/// </summary>
public sealed class ReadingBoardService(IAppDb db, TenantContext tenant, Infrastructure.Tenancy.RequestScope scope, TenantClock clock)
{
    public bool IsStaff => tenant.IsStaff;

    public async Task<ReadingBoard> GetAsync(CancellationToken ct = default)
    {
        var t = tenant.Current;
        var today = clock.Today(t.TimeZoneId);
        var canSee = t.ReadingBoardPublic || IsStaff;
        await using var ctx = db.Create();

        var members = await (from m in ctx.Memberships.Include(x => x.Roles)
                             join a in ctx.Accounts on m.AccountId equals a.Id
                             where m.Status == MembershipStatus.Approved
                             select new { a.Id, a.DisplayName, m.GroupLabel, Roles = m.Roles.Select(r => r.Role).ToList() }).ToListAsync(ct);
        if (!canSee) return new(t.ReadingBoardPublic, false, members.Count, 0, 0, 0, 0, 0, today, [], []);

        var ids = members.Select(m => m.Id).ToList();
        // 통독 표는 계정 범위라 앱 전역 필터가 "본인만"이다. 여기서만 끄고, 위에서 고른 이 부서 승인 소속원으로 직접 거른다.
        var rounds = await ctx.ReadingRounds.IgnoreQueryFilters().AsNoTracking().Where(r => ids.Contains(r.AccountId))
            .Select(r => new { r.AccountId, r.RoundNo, r.CompletedUtc, r.ClosedUtc }).ToListAsync(ct);
        var open = rounds.Where(r => r.CompletedUtc == null && r.ClosedUtc == null).ToDictionary(r => r.AccountId, r => r.RoundNo);
        var reads = await ctx.ChapterReads.IgnoreQueryFilters().AsNoTracking().Where(c => ids.Contains(c.AccountId))
            .Select(c => new { c.AccountId, c.RoundNo, c.BookId, c.Chapter, c.ReadUtc }).ToListAsync(ct);
        var weekStart = today.AddDays(-(int)today.DayOfWeek);
        DateOnly Local(DateTime utc) => TenantClock.ToTenantDate(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)), t.TimeZoneId);

        var me = scope.AccountId;
        var rows = members.Select(m =>
        {
            var roundNo = open.GetValueOrDefault(m.Id, rounds.Where(r => r.AccountId == m.Id).Select(r => r.RoundNo).DefaultIfEmpty(0).Max());
            var mine = reads.Where(r => r.AccountId == m.Id && r.RoundNo == roundNo).ToList();
            var last = reads.Where(r => r.AccountId == m.Id).OrderByDescending(r => r.ReadUtc).FirstOrDefault();
            return new BoardMember(m.Id, m.DisplayName, Badge(m.GroupLabel, m.Roles), m.Roles, roundNo,
                rounds.Count(r => r.AccountId == m.Id && r.CompletedUtc != null), mine.Count,
                ReadingSummary.Percent(mine.Count, ReadingService.TotalChapters),
                last?.BookId, last?.Chapter, last?.ReadUtc, last is null ? null : today.DayNumber - Local(last.ReadUtc).DayNumber, m.Id == me);
        }).ToList();

        var readers = rows.Where(r => r.RoundNo > 0).ToList();
        var thisWeek = reads.Where(r => Local(r.ReadUtc) >= weekStart).Select(r => r.AccountId).Distinct().Count();
        var hall = rows.Where(r => r.Completed > 0).OrderByDescending(r => r.Completed).ThenBy(r => r.Name).ToList();
        return new(t.ReadingBoardPublic, true, members.Count, readers.Count, hall.Count,
            readers.Count == 0 ? 0 : Math.Round(readers.Average(r => r.Percent), 1), rows.Sum(r => r.Completed), thisWeek, today, rows, hall);
    }

    /// <summary>부서의 "통독 참여자 현황 공개" 켜고 끄기(관리자만 — 부르는 화면이 검사한다).</summary>
    public async Task SetPublicAsync(bool on, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var t = await ctx.Tenants.SingleAsync(x => x.Id == tenant.Current.Id, ct);
        t.ReadingBoardPublic = on;
        await ctx.SaveChangesAsync(ct);
    }

    /// <summary>반 이름이 있으면 반, 없으면 역할(교사 · 관리자처럼 여럿이면 함께).</summary>
    public static string Badge(string? group, IReadOnlyList<string> roles)
    {
        if (!string.IsNullOrWhiteSpace(group)) return group;
        var names = new[] { (Roles.Teacher, "교사"), (Roles.TenantAdmin, "관리자"), (Roles.Guardian, "보호자"), (Roles.Participant, "참여자") }
            .Where(x => roles.Contains(x.Item1)).Select(x => x.Item2).ToList();
        return names.Count == 0 ? "소속원" : string.Join(" · ", names.Take(2));
    }

    public static string Ago(int days) => days switch { <= 0 => "오늘", 1 => "어제", _ => $"{days}일 전" };

    /// <summary>이름 찾기 + 정렬: 진도율순(완독 횟수 → 이번 회차 장 수) 또는 최근순(마지막으로 읽은 때).</summary>
    public static IEnumerable<BoardMember> Sort(IEnumerable<BoardMember> rows, string sort, string? q)
    {
        var f = string.IsNullOrWhiteSpace(q) ? rows : rows.Where(r => r.Name.Contains(q.Trim()));
        return sort == "recent"
            ? f.OrderByDescending(r => r.LastUtc ?? DateTime.MinValue).ThenBy(r => r.Name)
            : f.OrderByDescending(r => r.Completed).ThenByDescending(r => r.ReadChapters).ThenByDescending(r => r.LastUtc ?? DateTime.MinValue).ThenBy(r => r.Name);
    }
}
