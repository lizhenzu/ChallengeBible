using System.Text.Json;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Infrastructure.Tenancy;
using ChallengeBible.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>"나의 기도 기록"(ADR 0106, R-319): 그 사람 전체(부서와 상관없이). 유년부 Prayer365PrayStats 와 같은 셈.</summary>
public sealed record Prayer365Stats(int PrayedDays, int Streak, int Longest, int RatePct, bool PrayedToday, DateOnly? FirstDate,
    DateOnly Today, IReadOnlySet<DateOnly> Dates, IReadOnlyList<(int Month, int Prayed, int Elapsed)> Months)
{
    public const int TotalDays = 366;
    public int RemainDays => Math.Max(0, TotalDays - PrayedDays);
    public double ProgressPct => Math.Min(100.0, PrayedDays * 100.0 / TotalDays);

    /// <summary>기도율 = 처음 기도한 날 ~ 오늘(안 눌렀으면 어제)까지 지난 날 중 기도한 날. 연속 = 그 끝날부터 거꾸로 끊기지 않은 날.</summary>
    public static Prayer365Stats Compute(IEnumerable<DateOnly> days, DateOnly today)
    {
        var set = days.Where(d => d <= today).ToHashSet();
        var prayedToday = set.Contains(today);
        if (set.Count == 0) return new(0, 0, 0, -1, false, null, today, set, []);
        var first = set.Min();
        var end = prayedToday ? today : today.AddDays(-1);
        var elapsed = end.DayNumber - first.DayNumber + 1;
        var rate = elapsed > 0 ? (int)Math.Round(set.Count(d => d <= end) * 100.0 / elapsed) : -1;
        var streak = 0;
        for (var d = end; set.Contains(d); d = d.AddDays(-1)) streak++;
        int longest = 0, run = 0; DateOnly? prev = null;
        foreach (var d in set.OrderBy(x => x)) { run = prev is { } p && d.DayNumber - p.DayNumber == 1 ? run + 1 : 1; longest = Math.Max(longest, run); prev = d; }
        var months = new List<(int, int, int)>();
        for (var m = new DateOnly(first.Year, first.Month, 1); m <= new DateOnly(today.Year, today.Month, 1); m = m.AddMonths(1))
        {
            var from = m < first ? first : m;
            var last = m.AddMonths(1).AddDays(-1);
            var to = last < end ? last : end;
            if (to < from) continue;
            months.Add((m.Month, set.Count(d => d >= from && d <= to), to.DayNumber - from.DayNumber + 1));
        }
        return new(set.Count, streak, longest, rate, prayedToday, first, today, set, months.Count > 6 ? months[^6..] : months);
    }
}

public sealed record Prayer365Participant(string UserName, string Name, string Role, int PrayedDays, int Streak, int RatePct, DateOnly Last, int DaysAgo);
public sealed record Prayer365DeptRow(int TenantId, string Dept, int Participants, int Today, int RatePct, int Resting);
public sealed record Prayer365Dashboard(int Today, int Week, int Total, int AvgRate, IReadOnlyList<(DateOnly Day, int People)> Days,
    IReadOnlyList<Prayer365DeptRow> Depts, IReadOnlyList<(string MonthDay, string Title, int Views)> TopViewed, IReadOnlyList<(DateOnly Day, int People)> TopDays);

/// <summary>
/// 자녀를 위한 365 기도(ADR 0106, R-316 ~ R-319). 기도문은 플랫폼 공용(월일마다 한 편, 비어 있는 날은 샘플을 보여 준다),
/// 기도 기록 · 조회 수는 부서 표. 학생 · 새친구에게는 보이지 않는다.
/// </summary>
public sealed class Prayer365Service(IAppDb db, ISystemDb systemDb, RequestScope scope, TenantContext tenant, TenantClock clock, TimeProvider time)
{
    public const string BookTitle = "자녀를 위한 365일 축복 기도문";
    public const string Author = "한치호";
    public const string Publisher = "도서출판 두돌비";
    public const int RestDays = 3;   // 쉬는 사람 = 마지막 기도가 3일보다 더 전
    private static readonly int[] MonthLen = [31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

    // ── 날짜(월일) ──
    public static string Md(DateOnly d) => $"{d.Month:00}{d.Day:00}";
    public static bool TryParse(string? md, out int month, out int day)
    {
        month = day = 0;
        if (md is not { Length: 4 } || !md.All(char.IsAsciiDigit)) return false;
        month = int.Parse(md[..2]); day = int.Parse(md[2..]);
        return month is >= 1 and <= 12 && day >= 1 && day <= MonthLen[month - 1];
    }
    public static string Label(string md) => TryParse(md, out var m, out var d) ? $"{m}월 {d}일" : md;
    /// <summary>한 해의 몇 번째 날(2/29 포함 366일 기준, 1부터).</summary>
    public static int DayIndex(string md) => TryParse(md, out var m, out var d) ? MonthLen.Take(m - 1).Sum() + d : 0;
    public static IEnumerable<string> AllDays() => Enumerable.Range(0, 12).SelectMany(m => Enumerable.Range(1, MonthLen[m]).Select(d => $"{m + 1:00}{d:00}"));

    /// <summary>샘플 기도문(진짜를 옮기기 전, ADR 0106 정한 것 3). 날마다 제목 · 말씀만 조금씩 다르다.</summary>
    public static Prayer365 Sample(string md)
    {
        string[] themes = ["믿음 안에서 자라게 하소서", "감사가 넘치게 하소서", "친구를 사랑하게 하소서", "건강하게 지켜 주소서", "지혜를 주소서", "말씀을 사랑하게 하소서", "기쁨으로 예배하게 하소서"];
        (string Ref, string Text)[] verses = [("잠 22:6", "마땅히 행할 길을 아이에게 가르치라"), ("시 127:3", "자식들은 여호와의 기업이요"), ("눅 2:52", "예수는 지혜와 키가 자라가며"), ("살전 5:16-18", "항상 기뻐하라 쉬지 말고 기도하라 범사에 감사하라")];
        var i = DayIndex(md);
        var t = themes[i % themes.Length];
        var v = verses[i % verses.Length];
        return new Prayer365
        {
            MonthDay = md, Title = $"(샘플) {t}", VerseRef = v.Ref, VerseText = $"(샘플 말씀) {v.Text}", IsSample = true,
            Body = $"사랑의 하나님,\n(샘플 기도문 · {Label(md)}) 오늘도 우리 아이들을 지켜 주셔서 감사합니다.\n말씀 안에서 날마다 자라게 하시고 서로 사랑하게 하소서.\n주님,\n아이들의 길을 인도하여 주소서.\n아멘.",
            UpdatedUtc = DateTime.UnixEpoch,
        };
    }

    // ── 누가 보나(R-316) ──
    /// <summary>학생 · 새친구 역할인가(365 기도를 보지 않는다).</summary>
    public static bool IsStudentRole(string? roleCode) => UserRole.KindOf(roleCode) == UserRoleKind.Student || (roleCode ?? "").Trim() == ClassService.NewcomerCode;
    /// <summary>이 부서에서 볼 수 있나: 로그인 · 승인된 소속 · 학생 · 새친구가 아님 · 임시 부서가 아님. 플랫폼 관리자는 늘.</summary>
    public bool CanSee(bool platformAdmin) => tenant.Tenant is not null && !tenant.IsLobby && scope.UserName is not null
        && (platformAdmin || (tenant.IsApproved && !IsStudentRole(tenant.RoleCode)));

    private int T => tenant.Current.Id;
    public DateOnly Today => clock.Today(tenant.Current.TimeZoneId);

    public async Task<Prayer365> GetAsync(string md, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        return await ctx.Prayers365.AsNoTracking().SingleOrDefaultAsync(p => p.MonthDay == md, ct) ?? Sample(md);
    }

    /// <summary>오늘 이 부서에서 기도한 사람 수(R-318: "우리 부서 N명이 함께 기도했어요").</summary>
    public async Task<int> TogetherTodayAsync(CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var today = Today;
        return await ctx.Prayer365Logs.CountAsync(l => l.PrayDate == today, ct);
    }

    /// <summary>나의 기도 기록(R-319): 그 사람 전체 — 시스템 연결로 내 아이디의 기록만 읽는다.</summary>
    public async Task<Prayer365Stats> MyStatsAsync(CancellationToken ct = default)
    {
        var me = scope.UserName ?? throw new InvalidOperationException();
        await using var ctx = systemDb.Create(me);
        var days = await ctx.Prayer365Logs.AsNoTracking().Where(l => l.UserName == me).Select(l => l.PrayDate).ToListAsync(ct);
        return Prayer365Stats.Compute(days, Today);
    }

    /// <summary>[오늘 기도 했어요](R-317): 한 사람 하루 한 번(부서와 상관없이). 새로 남겼으면 true.</summary>
    public async Task<bool> PrayAsync(string md, CancellationToken ct = default)
    {
        var me = scope.UserName ?? throw new InvalidOperationException();
        var today = Today;
        await using (var sys = systemDb.Create(me))
            if (await sys.Prayer365Logs.AnyAsync(l => l.UserName == me && l.PrayDate == today, ct)) return false;
        await using var ctx = db.Create();
        ctx.Prayer365Logs.Add(new Prayer365Log { TenantId = T, UserName = me, PrayDate = today, MonthDay = md, PrayedUtc = time.GetUtcNow().UtcDateTime });
        try { await ctx.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return false; }   // 두 번 누름(UX_Prayer365Log_User_Day)
        return true;
    }

    /// <summary>기도문을 연 횟수(부서 · 날짜 · 월일). 관리자 보기는 부르지 않는다.</summary>
    public async Task AddViewAsync(string md, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var today = Today;
        var row = await ctx.Prayer365Views.SingleOrDefaultAsync(v => v.ViewDate == today && v.MonthDay == md, ct);
        if (row is null) ctx.Prayer365Views.Add(new Prayer365View { TenantId = T, ViewDate = today, MonthDay = md, Count = 1 });
        else row.Count++;
        try { await ctx.SaveChangesAsync(ct); } catch (DbUpdateException) { }   // 동시에 처음 연 경우 — 한 번은 놓쳐도 된다
    }

    // ── 부서 관리자: 우리 부서 참여자 ──
    public async Task<IReadOnlyList<Prayer365Participant>> ParticipantsAsync(string sort, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var today = Today;
        var logs = await ctx.Prayer365Logs.AsNoTracking().Select(l => new { l.UserName, l.PrayDate }).ToListAsync(ct);
        var names = logs.Select(l => l.UserName).Distinct().ToList();
        var users = await ctx.Users.AsNoTracking().Where(u => names.Contains(u.UserName!)).Select(u => new { u.Id, u.UserName, u.LastName, u.FirstName }).ToListAsync(ct);
        var ids = users.Select(u => u.Id).ToList();
        var roles = await ctx.DeptRoles.AsNoTracking().Where(d => ids.Contains(d.UserId!)).Select(d => new { d.UserId, d.RoleCode }).ToListAsync(ct);
        var rows = logs.GroupBy(l => l.UserName).Select(g =>
        {
            var st = Prayer365Stats.Compute(g.Select(x => x.PrayDate), today);
            var u = users.FirstOrDefault(x => x.UserName == g.Key);
            var rc = u is null ? null : roles.FirstOrDefault(r => r.UserId == u.Id)?.RoleCode;
            var last = g.Max(x => x.PrayDate);
            return new Prayer365Participant(g.Key, u is null ? g.Key : AppUser.FullName(u.LastName, u.FirstName), UserRole.LabelOf(UserRole.KindOf(rc)),
                st.PrayedDays, st.Streak, st.RatePct, last, Math.Max(0, today.DayNumber - last.DayNumber));
        });
        return (sort switch
        {
            "days" => rows.OrderByDescending(p => p.PrayedDays).ThenBy(p => p.DaysAgo),
            "rest" => rows.OrderByDescending(p => p.DaysAgo).ThenBy(p => p.Name),
            _ => rows.OrderBy(p => p.DaysAgo).ThenByDescending(p => p.PrayedDays),
        }).ToList();
    }

    // ── 플랫폼 관리자 ──
    /// <summary>365 기도 통계(대시보드, ADR 0106 질문 7): 오늘 · 이번 주 · 누적 · 평균 기도율 · 최근 30일 · 부서별 · 많이 읽힌 기도문 · 함께 기도가 많은 날.</summary>
    public async Task<Prayer365Dashboard> DashboardAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        var today = TenantClock.ToTenantDate(time.GetUtcNow(), PlatformHomeService.PlatformTimeZone);
        var logs = await ctx.Prayer365Logs.AsNoTracking().Select(l => new { l.TenantId, l.UserName, l.PrayDate }).ToListAsync(ct);
        var weekStart = today.AddDays(-(int)today.DayOfWeek);
        var perUser = logs.GroupBy(l => l.UserName).Select(g => Prayer365Stats.Compute(g.Select(x => x.PrayDate), today)).ToList();
        var rates = perUser.Where(s => s.RatePct >= 0).Select(s => s.RatePct).ToList();
        var days = Enumerable.Range(0, 30).Select(i => today.AddDays(i - 29)).Select(d => (d, logs.Count(l => l.PrayDate == d))).ToList();
        var tenants = await ctx.Tenants.AsNoTracking().Where(t => !t.IsLobby).Select(t => new { t.Id, t.Name }).ToListAsync(ct);
        var depts = tenants.OrderBy(t => DeptOrder.AgeLastKey(t.Name)).ThenBy(t => t.Name).Select(t =>
        {
            var mine = logs.Where(l => l.TenantId == t.Id).GroupBy(l => l.UserName).Select(g => (Last: g.Max(x => x.PrayDate), St: Prayer365Stats.Compute(g.Select(x => x.PrayDate), today))).ToList();
            var r = mine.Where(m => m.St.RatePct >= 0).Select(m => m.St.RatePct).ToList();
            return new Prayer365DeptRow(t.Id, t.Name, mine.Count, logs.Count(l => l.TenantId == t.Id && l.PrayDate == today),
                r.Count == 0 ? 0 : (int)Math.Round(r.Average()), mine.Count(m => today.DayNumber - m.Last.DayNumber > RestDays));
        }).ToList();
        var views = await ctx.Prayer365Views.AsNoTracking().GroupBy(v => v.MonthDay).Select(g => new { Md = g.Key, N = g.Sum(x => x.Count) })
            .OrderByDescending(x => x.N).Take(5).ToListAsync(ct);
        var mds = views.Select(v => v.Md).ToList();
        var titles = await ctx.Prayers365.AsNoTracking().Where(p => mds.Contains(p.MonthDay)).ToDictionaryAsync(p => p.MonthDay, p => p.Title, ct);
        return new Prayer365Dashboard(
            logs.Count(l => l.PrayDate == today), logs.Where(l => l.PrayDate >= weekStart && l.PrayDate <= today).Select(l => l.UserName).Distinct().Count(),
            perUser.Count, rates.Count == 0 ? 0 : (int)Math.Round(rates.Average()), days, depts,
            views.Select(v => (v.Md, titles.GetValueOrDefault(v.Md) ?? Sample(v.Md).Title, v.N)).ToList(),
            logs.GroupBy(l => l.PrayDate).Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).ThenByDescending(x => x.Key).Take(5).ToList());
    }

    /// <summary>366칸 달력판: 월일 → 들어간 기도문(없으면 null = 빈 날, 화면에는 샘플이 보인다).</summary>
    public async Task<IReadOnlyDictionary<string, Prayer365>> AllAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        return await ctx.Prayers365.AsNoTracking().ToDictionaryAsync(p => p.MonthDay, ct);
    }

    public async Task<Prayer365?> FindAsync(string actor, string md, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        return await ctx.Prayers365.AsNoTracking().SingleOrDefaultAsync(p => p.MonthDay == md, ct);
    }

    /// <summary>고치기 · 검수(플랫폼 관리자). 고치면 샘플 표시를 뗀다.</summary>
    public async Task SaveAsync(string actor, string md, string title, string? verseRef, string? verseText, string body, bool reviewed, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        var row = await ctx.Prayers365.SingleOrDefaultAsync(p => p.MonthDay == md, ct);
        if (row is null) { row = new Prayer365 { MonthDay = md, Title = "", Body = "" }; ctx.Prayers365.Add(row); }
        (row.Title, row.VerseRef, row.VerseText, row.Body, row.IsReviewed, row.IsSample) = (title.Trim(), Blank(verseRef), Blank(verseText), body.Trim(), reviewed, false);
        (row.UpdatedUtc, row.UpdatedBy) = (time.GetUtcNow().UtcDateTime, actor);
        await ctx.SaveChangesAsync(ct);
    }

    /// <summary>비어 있는 날을 샘플로 채운다(이미 있는 날은 그대로).</summary>
    public async Task<int> FillSamplesAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        var have = (await ctx.Prayers365.Select(p => p.MonthDay).ToListAsync(ct)).ToHashSet();
        var now = time.GetUtcNow().UtcDateTime;
        var add = AllDays().Where(md => !have.Contains(md)).Select(md => { var s = Sample(md); (s.UpdatedUtc, s.UpdatedBy) = (now, actor); return s; }).ToList();
        ctx.Prayers365.AddRange(add);
        await ctx.SaveChangesAsync(ct);
        return add.Count;
    }

    /// <summary>
    /// 진짜 기도문 넣기(ADR 0106 정한 것 3): 유년부 `SELECT … FROM dbo.Prayer365 FOR JSON PATH` 결과(배열)를 받아 같은 월일을 바꿔 넣는다.
    /// 칸: MonthDay · Title · VerseRef · VerseText · Body · Fixes(문자열 또는 배열) · IsReviewed. 틀린 줄은 건너뛰고 (넣은 수, 건너뛴 수)를 돌려준다.
    /// </summary>
    public async Task<(int Saved, int Skipped)> ImportAsync(string actor, string json, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new FormatException("JSON 배열이 아니에요.");
        await using var ctx = systemDb.Create(actor);
        var rows = await ctx.Prayers365.ToDictionaryAsync(p => p.MonthDay, ct);
        var now = time.GetUtcNow().UtcDateTime;
        int saved = 0, skipped = 0;
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            string? S(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var md = S("MonthDay")?.Trim();
            var title = S("Title")?.Trim();
            var body = S("Body")?.Replace("\r\n", "\n").Trim();
            if (!TryParse(md, out _, out _) || string.IsNullOrEmpty(title) || string.IsNullOrEmpty(body)) { skipped++; continue; }
            var fixes = e.TryGetProperty("Fixes", out var f) ? f.ValueKind switch { JsonValueKind.String => f.GetString(), JsonValueKind.Array => f.GetRawText(), _ => null } : null;
            var reviewed = e.TryGetProperty("IsReviewed", out var r) && (r.ValueKind == JsonValueKind.True || (r.ValueKind == JsonValueKind.Number && r.GetInt32() == 1));
            if (!rows.TryGetValue(md!, out var row)) { row = new Prayer365 { MonthDay = md!, Title = "", Body = "" }; ctx.Prayers365.Add(row); rows[md!] = row; }
            (row.Title, row.VerseRef, row.VerseText, row.Body, row.Fixes, row.IsReviewed, row.IsSample) = (title!, Blank(S("VerseRef")), Blank(S("VerseText")), body!, fixes, reviewed, false);
            (row.UpdatedUtc, row.UpdatedBy) = (now, actor);
            saved++;
        }
        await ctx.SaveChangesAsync(ct);
        return (saved, skipped);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
