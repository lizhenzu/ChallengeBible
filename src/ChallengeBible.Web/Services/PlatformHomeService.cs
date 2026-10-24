using System.Globalization;
using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record PlatformTenantToday(int Id, string Name, string Slug, string ThemeColor, bool Mission, bool Suspended,
    int TodayParticipants, int Participants, int Readers, int Pending);
public sealed record PlatformTodo(string Kind, int Count, string Title, string Text, string Href);
public sealed record PlatformLog(DateTime OccurredUtc, string Actor, string? Tenant, string Text);
public sealed record PlatformHome(DateTime NowLocal, int ActiveTenants, int SuspendedTenants, int Accounts, int NewAccounts,
    int TodayParticipants, int MissionTenants, int TodayReaders, int TodayChapters,
    IReadOnlyList<PlatformTodo> Todos, IReadOnlyList<PlatformTenantToday> Tenants, IReadOnlyList<PlatformLog> Log);

/// <summary>
/// 플랫폼 홈(확정 시안: 플랫폼-홈). 여러 부서를 함께 보므로 시스템 연결로만 읽고, 시작마다 플랫폼 관리자인지 DB 로 다시 확인한다(물리 설계 3-3).
/// "오늘": 미션은 부서마다 자기 시간대, 통독(계정 기록) · 공용 콘텐츠는 한국 시간(플랫폼 기본).
/// </summary>
public sealed class PlatformHomeService(ISystemDb systemDb, TenantClock clock)
{
    public const string PlatformTimeZone = "Asia/Seoul";
    public const int SoonDays = 14;
    public const int OldRequestDays = 3;
    private static readonly CultureInfo Ko = new("ko-KR");
    private static RuleSet Rules => RuleSet.Default;

    public async Task<PlatformHome> GetAsync(int actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        if (!await ctx.Accounts.AnyAsync(a => a.Id == actor && a.IsPlatformAdmin && a.Status == "Active", ct))
            throw new UnauthorizedAccessException("플랫폼 관리자만 볼 수 있어요.");

        var nowUtc = clock.UtcNow;
        var today = clock.Today(PlatformTimeZone);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(PlatformTimeZone);
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, tz).DateTime;

        var tenants = await ctx.Tenants.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
        var active = tenants.Where(t => t.Status == TenantStatus.Active).ToList();
        var accounts = await ctx.Accounts.CountAsync(ct);
        var weekAgo = nowUtc.UtcDateTime.AddDays(-7);
        var newAccounts = await ctx.Accounts.CountAsync(a => a.CreatedUtc >= weekAgo, ct);

        // 미션: 부서마다 그 부서의 오늘
        var todays = active.ToDictionary(t => t.Id, t => clock.Today(t.TimeZoneId));
        var dates = todays.Values.Distinct().ToList();
        var acts = await ctx.Activities.AsNoTracking().Where(a => dates.Contains(a.MissionDate))
            .Select(a => new { a.TenantId, a.MissionDate, a.ParticipantId }).Distinct().ToListAsync(ct);
        var todayBy = acts.Where(a => todays.GetValueOrDefault(a.TenantId) == a.MissionDate)
            .GroupBy(a => a.TenantId).ToDictionary(g => g.Key, g => g.Select(a => a.ParticipantId).Distinct().Count());
        var participants = await ctx.Participants.Where(p => p.IsActive).GroupBy(p => p.TenantId)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);

        // 통독: 한국 시간 오늘 읽은 장(계정 기록) → 부서별은 그 부서 승인 소속원 중 읽은 사람
        var dayStart = TimeZoneInfo.ConvertTimeToUtc(today.ToDateTime(TimeOnly.MinValue), tz);
        var dayEnd = dayStart.AddDays(1);
        var reads = await ctx.ChapterReads.AsNoTracking().Where(r => r.ReadUtc >= dayStart && r.ReadUtc < dayEnd)
            .Select(r => r.AccountId).ToListAsync(ct);
        var readers = reads.Distinct().ToList();
        var memberOf = await ctx.Memberships.Where(m => m.Status == MembershipStatus.Approved && readers.Contains(m.AccountId))
            .Select(m => new { m.TenantId, m.AccountId }).ToListAsync(ct);
        var readersBy = memberOf.GroupBy(m => m.TenantId).ToDictionary(g => g.Key, g => g.Select(m => m.AccountId).Distinct().Count());

        var requested = await ctx.Memberships.Where(m => m.Status == MembershipStatus.Requested)
            .Select(m => new { m.TenantId, m.RequestedUtc }).ToListAsync(ct);
        var pendingBy = requested.GroupBy(m => m.TenantId).ToDictionary(g => g.Key, g => g.Count());

        var rows = tenants.Select(t => new PlatformTenantToday(t.Id, t.Name, t.Slug, t.ThemeColor, t.MissionEnabled, t.Status != TenantStatus.Active,
            todayBy.GetValueOrDefault(t.Id), participants.GetValueOrDefault(t.Id), readersBy.GetValueOrDefault(t.Id), pendingBy.GetValueOrDefault(t.Id))).ToList();

        // 챙길 일(0건이면 숨김)
        var todos = new List<PlatformTodo>();
        var (empty, drafts) = await CommonContentAsync(ctx, today, ct);
        if (empty.Count > 0)
        {
            var (d, label) = empty[0];
            var inDays = d.DayNumber - today.DayNumber;
            var href = label switch
            {
                "말씀" => $"/platform/content/word?month={d:yyyy-MM}&date={d:yyyy-MM-dd}",
                "퀴즈" => $"/platform/content/quiz?month={d:yyyy-MM}&date={d:yyyy-MM-dd}",
                "기도" => $"/platform/content/prayer?no={d.Day}",
                _ => $"/platform/content/recite?no={Week.Of(d, Rules.WeekStart).NumberInYear}",
            };
            todos.Add(new("a", empty.Count, "곧 비는 공용 콘텐츠", $"{d.ToString("M월 d일(ddd)", Ko)} {label} · {(inDays == 0 ? "오늘" : $"{inDays}일 뒤")}", href));
        }
        if (drafts.Count > 0)
            todos.Add(new("c", drafts.Count, "공개 전 공용 초안", string.Join(" · ", drafts.Take(3)) + (drafts.Count > 3 ? $" 외 {drafts.Count - 3}건" : ""), "/platform/content"));
        var admins = await (from r in ctx.MembershipRoles
                            join m in ctx.Memberships on new { r.TenantId, Id = r.MembershipId } equals new { m.TenantId, m.Id }
                            where r.Role == Roles.TenantAdmin && m.Status == MembershipStatus.Approved
                            select m.TenantId).Distinct().ToListAsync(ct);
        var noAdmin = active.Where(t => !admins.Contains(t.Id)).ToList();
        if (noAdmin.Count > 0)
            todos.Add(new("b", noAdmin.Count, "관리자가 없는 부서", string.Join(" · ", noAdmin.Take(2).Select(t => t.Name)) + (noAdmin.Count > 2 ? $" 외 {noAdmin.Count - 2}곳" : ""), "/platform/tenants"));
        var oldBefore = nowUtc.UtcDateTime.AddDays(-OldRequestDays);
        var old = requested.Where(m => m.RequestedUtc < oldBefore).ToList();
        if (old.Count > 0)
        {
            var names = old.GroupBy(m => m.TenantId).Select(g => tenants.FirstOrDefault(t => t.Id == g.Key)?.Name).Where(n => n is not null).ToList();
            todos.Add(new("c", old.Count,$"{OldRequestDays}일 넘게 기다리는 참여 신청", string.Join(" · ", names.Take(2)) + (names.Count > 2 ? $" 외 {names.Count - 2}곳" : ""), "/platform/tenants"));
        }

        // 최근 관리 기록(지금은 진급 승인 · 이름 · 색 · 시즌 시작일만 남는다)
        var logs = await (from l in ctx.AuditLogs
                          join a in ctx.Accounts on l.ActorAccountId equals a.Id
                          orderby l.OccurredUtc descending, l.Id descending
                          select new { l.OccurredUtc, a.DisplayName, a.IsPlatformAdmin, l.TenantId, l.Action, l.Detail }).Take(8).ToListAsync(ct);
        var log = logs.Select(l => new PlatformLog(l.OccurredUtc, l.IsPlatformAdmin ? $"{l.DisplayName}(운영자)" : l.DisplayName,
            tenants.FirstOrDefault(t => t.Id == l.TenantId)?.Name, LogText(l.Action, l.Detail))).ToList();

        return new(nowLocal, active.Count, tenants.Count - active.Count, accounts, newAccounts,
            todayBy.Values.Sum(), active.Count(t => t.MissionEnabled), readers.Count, reads.Count, todos, rows, log);
    }

    public static string LogText(string action, string? detail) => action switch
    {
        "PromotionApproved" => "진급 승인",
        BrandingService.LogAction or SeasonService.LogAction => detail ?? "",
        _ => detail is null ? action : $"{action} · {detail}",
    };

    /// <summary>앞으로 14일 공용 콘텐츠 빈 칸(종류별)과 공개 전 초안 목록. 고르기는 미션 화면과 같다(공용 공개본만 보인다).</summary>
    private static async Task<(List<(DateOnly Date, string Label)> Empty, List<string> Drafts)> CommonContentAsync(AppDbContext ctx, DateOnly today, CancellationToken ct)
    {
        var last = today.AddDays(SoonDays - 1);
        var words = await ctx.DailyWords.AsNoTracking().Where(w => w.TenantId == null && w.IsCurrent && w.ExposeDate >= today)
            .Select(w => new { w.ExposeDate, w.Status }).ToListAsync(ct);
        var quizzes = await ctx.DailyQuizzes.AsNoTracking().Where(q => q.TenantId == null && q.IsCurrent && q.ExposeDate >= today)
            .Select(q => new { q.ExposeDate, q.Status }).ToListAsync(ct);
        var prayers = await ctx.DailyPrayers.AsNoTracking().Where(p => p.TenantId == null && p.IsCurrent && p.DayNo != null)
            .Select(p => new { DayNo = (int)p.DayNo!, p.Status }).ToListAsync(ct);
        var verses = await ctx.WeeklyVerses.AsNoTracking().Where(v => v.TenantId == null && v.IsCurrent && v.WeekNo != null)
            .Select(v => new { WeekNo = (int)v.WeekNo!, v.Status }).ToListAsync(ct);
        static bool Pub(string s) => s == ContentStatus.Published;

        var empty = new List<(DateOnly, string)>();
        for (var d = today; d <= last; d = d.AddDays(1))
        {
            var day = d;
            if (!words.Any(w => w.ExposeDate == day && Pub(w.Status))) empty.Add((d, "말씀"));
            if (!quizzes.Any(q => q.ExposeDate == day && Pub(q.Status))) empty.Add((d, "퀴즈"));
            if (!prayers.Any(p => p.DayNo == day.Day && Pub(p.Status))) empty.Add((d, "기도"));
            var wk = Week.Of(d, Rules.WeekStart).NumberInYear;
            if (!verses.Any(v => v.WeekNo == wk && Pub(v.Status))) empty.Add((d, "암송"));
        }

        var drafts = new List<string>();
        var wd = words.Where(w => !Pub(w.Status)).Select(w => w.ExposeDate).Order().ToList();
        if (wd.Count > 0) drafts.Add($"말씀 {wd.Count}건({wd[0]:M'/'d}~)");
        var qd = quizzes.Where(q => !Pub(q.Status)).Select(q => q.ExposeDate).Order().ToList();
        if (qd.Count > 0) drafts.Add($"퀴즈 {qd.Count}건({qd[0]:M'/'d}~)");
        var pd = prayers.Where(p => !Pub(p.Status) && !prayers.Any(x => x.DayNo == p.DayNo && Pub(x.Status))).Count();
        if (pd > 0) drafts.Add($"기도 {pd}건");
        var vd = verses.Where(v => !Pub(v.Status) && !verses.Any(x => x.WeekNo == v.WeekNo && Pub(x.Status))).Count();
        if (vd > 0) drafts.Add($"암송 {vd}건");
        return (empty, drafts);
    }
}
