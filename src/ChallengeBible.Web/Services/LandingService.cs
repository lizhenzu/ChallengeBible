using ChallengeBible.Domain.Rules;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ChallengeBible.Web.Services;

public sealed record LandingDay(DateOnly Date, int People);
/// <summary>모든 부서를 합친 숫자(0061). 부서 · 사람을 알 수 있는 값은 담지 않는다.</summary>
public sealed record LandingStats(int Tenants, int People, int TodayPeople, int YearChapters, IReadOnlyList<LandingDay> Week);

public sealed class LandingView
{
    public DateOnly Today { get; init; }
    public RuleSet Rules { get; init; } = RuleSet.Default;
    public string? VerseText { get; init; }
    public string? VerseRef { get; init; }
    public string? WordRange { get; init; }
    public string? ReciteRange { get; init; }
    public DateOnly? ReciteStart { get; init; }
    public DateOnly? ReciteEnd { get; init; }
    public string? PrayerTrait { get; init; }
    public LandingStats Stats { get; init; } = new(0, 0, 0, 0, []);
}

/// <summary>
/// 로그인 전 첫 화면(확정 시안 로그인전-첫화면-v2, 0061). 오늘 = 한국 시간(플랫폼 기본).
/// 오늘의 말씀은 공용 콘텐츠만 일반 연결로 읽는다. 숫자는 여러 부서에 걸쳐 있어 시스템 연결로 합계만 세고(물리 설계 3-3), 5분 캐시한다.
/// </summary>
public sealed class LandingService(IAppDb db, ISystemDb systemDb, TenantClock clock, BibleTextService bible, RuleService rules, IMemoryCache cache)
{
    public const string CacheKey = "landing:stats";
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    public async Task<LandingView> GetAsync(CancellationToken ct = default)
    {
        var today = clock.Today(PlatformHomeService.PlatformTimeZone);
        var r = await rules.ForDateAsync(today, ct);
        await using var ctx = db.Create();

        // 공용만(TenantId 없음): 공개된 현재 개정
        var word = await ctx.DailyWords.Where(w => w.TenantId == null && w.ExposeDate == today && w.IsCurrent && w.Status == ContentStatus.Published).FirstOrDefaultAsync(ct);
        var day = (byte)today.Day;
        var prayer = await ctx.DailyPrayers.Where(p => p.TenantId == null && p.DayNo == day && p.IsCurrent && p.Status == ContentStatus.Published).FirstOrDefaultAsync(ct);
        var week = Week.Of(today, r.WeekStart);
        var no = (byte)week.NumberInYear;
        var verse = await ctx.WeeklyVerses.Where(v => v.TenantId == null && v.WeekNo == no && v.IsCurrent && v.Status == ContentStatus.Published).FirstOrDefaultAsync(ct);

        string? text = null;
        if (word is not null)
            text = (await bible.RangeAsync(new BibleRef(word.BookId, word.StartChapter, word.StartVerse, word.StartChapter, word.StartVerse), [BibleTranslation.Krv], ct))
                .FirstOrDefault()?.Texts.GetValueOrDefault(BibleTranslation.Krv);

        return new LandingView
        {
            Today = today, Rules = r,
            VerseText = text,
            VerseRef = word is null ? null : $"{BibleBooks.NameOf(word.BookId)} {word.StartChapter}:{word.StartVerse}",
            WordRange = word is null ? null : BibleBooks.Range(word.BookId, word.StartChapter, word.StartVerse, word.EndChapter, word.EndVerse),
            ReciteRange = verse is null ? null : BibleBooks.Range(verse.BookId, verse.StartChapter, verse.StartVerse, verse.EndChapter, verse.EndVerse),
            ReciteStart = verse is null ? null : week.Start, ReciteEnd = verse is null ? null : week.End,
            PrayerTrait = prayer?.TraitName,
            Stats = await cache.GetOrCreateAsync(CacheKey, e => { e.AbsoluteExpirationRelativeToNow = CacheFor; return StatsAsync(today, ct); }) ?? new(0, 0, 0, 0, []),
        };
    }

    /// <summary>합계만 센다. 시스템 연결이지만 읽기만 하고 부서 · 사람을 알 수 있는 값은 돌려주지 않는다(0061).</summary>
    private async Task<LandingStats> StatsAsync(DateOnly today, CancellationToken ct)
    {
        await using var ctx = systemDb.Create(0);   // 계정 없음(로그인 전)
        var active = await ctx.Tenants.Where(t => t.Status == TenantStatus.Active).Select(t => t.Id).ToListAsync(ct);
        var people = await ctx.Memberships.Where(m => m.Status == MembershipStatus.Approved && active.Contains(m.TenantId))
            .Select(m => m.AccountId).Distinct().CountAsync(ct);

        var tz = TimeZoneInfo.FindSystemTimeZoneById(PlatformHomeService.PlatformTimeZone);
        var start = today.AddDays(-6);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(start.ToDateTime(TimeOnly.MinValue), tz);
        var yearUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(today.Year, 1, 1), tz);

        // 최근 7일: 미션을 한 계정(참여자 → 소속 → 계정) + 통독 장을 읽은 계정, 날마다 중복 없이
        var missions = await (from a in ctx.Activities
                              where a.MissionDate >= start && a.MissionDate <= today
                              join p in ctx.Participants on a.ParticipantId equals p.Id
                              join m in ctx.Memberships on p.MembershipId equals m.Id
                              select new { a.MissionDate, m.AccountId }).Distinct().ToListAsync(ct);
        var reads = await ctx.ChapterReads.Where(c => c.ReadUtc >= fromUtc).Select(c => new { c.ReadUtc, c.AccountId }).ToListAsync(ct);
        var byDay = missions.Select(x => (x.MissionDate, x.AccountId))
            .Concat(reads.Select(x => (DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(x.ReadUtc, DateTimeKind.Utc), tz)), x.AccountId)))
            .GroupBy(x => x.Item1).ToDictionary(g => g.Key, g => g.Select(x => x.Item2).Distinct().Count());
        var week = Enumerable.Range(0, 7).Select(i => start.AddDays(i)).Select(d => new LandingDay(d, byDay.GetValueOrDefault(d))).ToList();

        var chapters = await ctx.ChapterReads.CountAsync(c => c.ReadUtc >= yearUtc, ct);
        var depts = await ctx.Tenants.CountAsync(t => t.Status == TenantStatus.Active && !t.IsLobby, ct);   // 임시 부서(0064)는 "함께하는 부서"에 세지 않는다
        return new LandingStats(depts, people, byDay.GetValueOrDefault(today), chapters, week);
    }
}
