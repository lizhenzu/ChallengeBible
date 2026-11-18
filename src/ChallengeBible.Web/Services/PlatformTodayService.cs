using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record TodayDept(int Id, string Name, string ThemeColor, bool Suspended, bool IsLobby, int Members, IReadOnlyList<string> Admins, string Programs);
public sealed record TodayAccount(string UserName, string Name, string? Depts, DateTime JoinedUtc);
public sealed record TodayMission(string UserName, string Name, int TenantId, string Dept, IReadOnlySet<Mission> Done, int Points);
public sealed record TodayReader(string UserName, string Name, string? Dept, string Where, int Chapters);
public sealed record PlatformToday(DateOnly Today, int ActiveDepts, int Accounts, int NewAccounts, int NoDeptAccounts, int Participants, int Targets, int MissionDepts, int Readers,
    IReadOnlyList<(int Id, string Name, int Count)> DeptChips,
    IReadOnlyList<TodayDept> Depts, IReadOnlyList<TodayAccount> AccountRows, IReadOnlyList<TodayMission> Missions, IReadOnlyList<TodayReader> Reads);

/// <summary>
/// 플랫폼 › 오늘 현황(2026-11-18 사용자 확정 시안 플랫폼홈-숫자보기): 플랫폼 홈의 숫자를 누르면 그 숫자에 해당하는 부서 · 계정 · 미션 참여 · 통독 목록.
/// 숫자 세는 법은 플랫폼 홈(PlatformHomeService)과 같다 — 미션은 부서마다 그 부서의 오늘, 통독은 한국 시간 오늘. 플랫폼 관리자만, 시스템 연결.
/// </summary>
public sealed class PlatformTodayService(ISystemDb systemDb, TenantClock clock)
{
    public const string Depts = "dept", Accounts = "acct", Missions = "mission", Reads = "read";
    public const int MaxRows = 500;

    /// <param name="kind">dept · acct · mission · read</param>
    /// <param name="tenantId">부서로 좁히기(계정 · 미션 · 통독)</param>
    /// <param name="filter">계정: new(이번 주 새로, 기본) · all · none(소속 없음)</param>
    public async Task<PlatformToday> GetAsync(string actor, string kind, int? tenantId, string? filter, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        if (!await AccountService.IsActivePlatformAdminAsync(ctx, actor, ct)) throw new UnauthorizedAccessException("플랫폼 관리자만 볼 수 있어요.");

        var nowUtc = clock.UtcNow.UtcDateTime;
        var today = clock.Today(PlatformHomeService.PlatformTimeZone);
        var tenants = await ctx.Tenants.AsNoTracking().ToListAsync(ct);
        var active = tenants.Where(t => t.Status == TenantStatus.Active).ToList();
        var names = tenants.ToDictionary(t => t.Id, t => t.Name);
        string Short(int id) => ShortDept(names.GetValueOrDefault(id) ?? "");

        var members = await (from d in ctx.DeptRoles join u in ctx.Users on d.UserId equals u.Id
                             select new { d.TenantId, UserName = u.UserName!, u.Id, d.RoleCode }).ToListAsync(ct);
        var deptOf = members.GroupBy(m => m.UserName).ToDictionary(g => g.Key, g => g.Select(m => m.TenantId).ToList());
        var users = await ctx.Users.AsNoTracking().Select(u => new { u.Id, u.UserName, u.LastName, u.FirstName, u.InsertDT }).ToListAsync(ct);
        var nameOf = users.Where(u => u.UserName != null).ToDictionary(u => u.UserName!, u => AppUser.FullName(u.LastName, u.FirstName));
        var weekAgo = nowUtc.AddDays(-7);

        // 미션: 부서마다 그 부서의 오늘(플랫폼 홈과 같다)
        var todays = active.ToDictionary(t => t.Id, t => clock.Today(t.TimeZoneId));
        var dates = todays.Values.Distinct().ToList();
        var acts = (await ctx.MissionActivities.AsNoTracking().Where(a => dates.Contains(a.MissionDate))
                .Select(a => new { a.TenantId, a.MissionDate, a.UserName, a.MissionItem, a.Points }).ToListAsync(ct))
            .Where(a => todays.GetValueOrDefault(a.TenantId) == a.MissionDate).ToList();
        var missionRows = acts.GroupBy(a => (a.TenantId, a.UserName)).Select(g => new TodayMission(g.Key.UserName, nameOf.GetValueOrDefault(g.Key.UserName) ?? g.Key.UserName,
                g.Key.TenantId, Short(g.Key.TenantId),
                g.Select(a => MissionItemCodes.TryParse(a.MissionItem, out var it) ? it.Mission() : (Mission?)null).Where(m => m is not null).Select(m => m!.Value).ToHashSet(),
                g.Sum(a => a.Points)))
            .OrderBy(r => DeptOrder.Key(names.GetValueOrDefault(r.TenantId))).ThenByDescending(r => r.Points).ThenBy(r => r.Name, StringComparer.Ordinal).ToList();
        var targets = members.Count(m => m.RoleCode == UserRole.StudentCode && active.Any(t => t.Id == m.TenantId && t.MissionEnabled));

        // 통독: 한국 시간 오늘 읽은 장(계정 기록)
        var tz = TimeZoneInfo.FindSystemTimeZoneById(PlatformHomeService.PlatformTimeZone);
        var dayStart = TimeZoneInfo.ConvertTimeToUtc(today.ToDateTime(TimeOnly.MinValue), tz);
        var reads = await ctx.ReadingChapterReads.AsNoTracking().Where(r => r.ReadAt >= dayStart && r.ReadAt < dayStart.AddDays(1))
            .Select(r => new { r.UserName, r.BookId, r.Chapter }).ToListAsync(ct);
        var readRows = reads.GroupBy(r => r.UserName).Select(g =>
        {
            var d = deptOf.GetValueOrDefault(g.Key)?.FirstOrDefault();
            return new TodayReader(g.Key, nameOf.GetValueOrDefault(g.Key) ?? g.Key, d is int x ? Short(x) : null, Where(g.Select(r => (r.BookId, r.Chapter))), g.Select(r => (r.BookId, r.Chapter)).Distinct().Count());
        }).OrderByDescending(r => r.Chapters).ThenBy(r => r.Name, StringComparer.Ordinal).ToList();

        // 부서
        var adminIds = await DeptMembership.AdminIdsAsync(ctx, ct);
        var deptRows = tenants.Where(t => t.Status == TenantStatus.Active || kind == Depts)
            .OrderBy(t => t.Status != TenantStatus.Active).ThenBy(t => DeptOrder.AgeLastKey(t.Name)).ThenBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new TodayDept(t.Id, t.Name, t.ThemeColor, t.Status != TenantStatus.Active, t.IsLobby, members.Count(m => m.TenantId == t.Id),
                members.Where(m => m.TenantId == t.Id && adminIds.Contains(m.Id)).Select(m => nameOf.GetValueOrDefault(m.UserName) ?? m.UserName).ToList(),
                string.Join(" · ", new[] { t.MissionEnabled ? "미션" : null, t.ReadingEnabled ? "통독" : null, t.CopyEnabled ? "필사" : null }.Where(x => x is not null))))
            .ToList();

        // 계정
        var acctAll = users.Where(u => u.UserName != null).Select(u => new TodayAccount(u.UserName!, AppUser.FullName(u.LastName, u.FirstName),
            deptOf.TryGetValue(u.UserName!, out var ds) ? string.Join(" · ", ds.Select(Short)) : null, u.InsertDT)).ToList();
        var acctRows = (filter switch
        {
            "all" => acctAll,
            "none" => acctAll.Where(a => a.Depts is null),
            _ => acctAll.Where(a => a.JoinedUtc >= weekAgo),
        }).Where(a => tenantId is not int t || (deptOf.GetValueOrDefault(a.UserName)?.Contains(t) ?? false))
          .OrderByDescending(a => a.JoinedUtc).Take(MaxRows).ToList();

        // 부서 칩(지금 탭 기준 숫자)
        var chips = kind switch
        {
            Missions => missionRows.GroupBy(r => r.TenantId).Select(g => (g.Key, g.Count())),
            Reads => readRows.SelectMany(r => (deptOf.GetValueOrDefault(r.UserName) ?? []).Distinct().Select(d => (d, 1))).GroupBy(x => x.d).Select(g => (g.Key, g.Count())),
            Accounts => acctAll.Where(a => filter switch { "all" => true, "none" => false, _ => a.JoinedUtc >= weekAgo })
                .SelectMany(a => (deptOf.GetValueOrDefault(a.UserName) ?? []).Distinct().Select(d => (d, 1))).GroupBy(x => x.d).Select(g => (g.Key, g.Count())),
            _ => [],
        };
        var chipList = chips.Where(c => names.ContainsKey(c.Item1)).OrderBy(c => DeptOrder.Key(names[c.Item1])).Select(c => (c.Item1, Short(c.Item1), c.Item2)).ToList();

        if (tenantId is int tid)
        {
            missionRows = missionRows.Where(r => r.TenantId == tid).ToList();
            readRows = readRows.Where(r => deptOf.GetValueOrDefault(r.UserName)?.Contains(tid) ?? false).ToList();
        }
        return new(today, active.Count, users.Count, users.Count(u => u.InsertDT >= weekAgo), acctAll.Count(a => a.Depts is null),
            acts.Select(a => (a.TenantId, a.UserName)).Distinct().Count(), targets, active.Count(t => t.MissionEnabled), reads.Select(r => r.UserName).Distinct().Count(),
            chipList, deptRows, acctRows, missionRows, readRows);
    }

    /// <summary>"잠실새내교회 유년부" → "유년부"(교회 이름은 머리말에 있어 줄인다).</summary>
    public static string ShortDept(string name) => name.StartsWith("잠실새내교회 ") ? name["잠실새내교회 ".Length..] : name;

    /// <summary>읽은 곳: 책마다 "창세기 12~15장", 이어지지 않으면 "창세기 1 · 3장". 책이 여럿이면 " · "로 잇는다.</summary>
    public static string Where(IEnumerable<(byte Book, short Chapter)> read)
    {
        var parts = new List<string>();
        foreach (var g in read.Distinct().GroupBy(r => r.Book).OrderBy(g => g.Key))
        {
            var chs = g.Select(r => r.Chapter).OrderBy(c => c).ToList();
            var runs = new List<string>();
            for (var i = 0; i < chs.Count;)
            {
                var j = i;
                while (j + 1 < chs.Count && chs[j + 1] == chs[j] + 1) j++;
                runs.Add(i == j ? $"{chs[i]}" : $"{chs[i]}~{chs[j]}");
                i = j + 1;
            }
            parts.Add($"{BibleBooks.NameOf(g.Key)} {string.Join(" · ", runs)}장");
        }
        return string.Join(" · ", parts);
    }
}
