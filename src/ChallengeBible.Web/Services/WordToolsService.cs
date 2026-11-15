using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>한 달 입력의 하루(유년부 MonthlyBulk 의 칸: 제목 · 성경 · 장절). Id 없으면 새 날.</summary>
public sealed class WordMonthRow
{
    public DateOnly Date { get; set; }
    public int? Id { get; set; }
    public string? Status { get; set; }
    public string? Title { get; set; }
    public byte Book { get; set; }
    public short? StartChapter { get; set; }
    public short? StartVerse { get; set; }
    public short? EndChapter { get; set; }
    public short? EndVerse { get; set; }
    /// <summary>화면에서 바뀐 날(스크립트가 표시). 스크립트가 없으면 모두 true 로 와도 값이 같으면 건너뛴다.</summary>
    public bool Changed { get; set; }
}

/// <summary>영상 구간 편집의 한 장(유년부 SegmentEditor 의 한 줄). Hint = "30절부터" 같은 안내.</summary>
public sealed record ClipChapter(short Chapter, string? VideoId, int? LengthSec, int? StartSec, int? EndSec, string Hint);

public sealed record ClipDay(DateOnly Date, string Ref, byte Book, IReadOnlyList<ClipChapter> Chapters);

/// <summary>
/// 말씀 입력 · 퀴즈 출제에 더한 것(유년부 /JuManNa · /Question 을 챌린지에 옮김, 확정 시안 퀴즈출제-말씀입력, 2026-11-15).
/// ① 한 달 입력(제목 · 성경 · 장절만, 바꾼 날만, 새 날은 초안) ② 영상 구간 편집(그날 목록 통째로, R-127) ③ 퀴즈 삭제(지금 개정을 내림 — 기록이 가리키므로 지우지 않는다).
/// 플랫폼 관리자만(시스템 연결, 공용 콘텐츠).
/// </summary>
public sealed class WordToolsService(ISystemDb systemDb, ContentAdminService content, TimeProvider time)
{
    /// <summary>한 날짜의 영상 구간 장 수 상한(장을 길게 걸치는 날을 막는다).</summary>
    public const int MaxClipChapters = 5;

    private async Task<AppDbContext> OpenAsync(string actor, CancellationToken ct)
    {
        var ctx = systemDb.Create(actor);
        if (!await AccountService.IsActivePlatformAdminAsync(ctx, actor, ct))
        {
            await ctx.DisposeAsync();
            throw new UnauthorizedAccessException("플랫폼 관리자만 할 수 있어요.");
        }
        return ctx;
    }

    private static IQueryable<DailyWord> Words(AppDbContext ctx) => ctx.DailyWords.Where(w => w.TenantId == null && w.IsCurrent);

    public async Task<List<WordMonthRow>> MonthAsync(string actor, DateOnly first, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var last = first.AddMonths(1).AddDays(-1);
        var words = await Words(ctx).AsNoTracking().Where(w => w.ExposeDate >= first && w.ExposeDate <= last).ToDictionaryAsync(w => w.ExposeDate, ct);
        var rows = new List<WordMonthRow>();
        for (var d = first; d <= last; d = d.AddDays(1))
            rows.Add(words.TryGetValue(d, out var w)
                ? new WordMonthRow { Date = d, Id = w.Id, Status = w.Status, Title = w.Title, Book = w.BookId, StartChapter = w.StartChapter, StartVerse = w.StartVerse, EndChapter = w.EndChapter, EndVerse = w.EndVerse }
                : new WordMonthRow { Date = d });
        return rows;
    }

    /// <summary>
    /// 한 달 저장: 바뀐 날만. 있는 날은 제목 · 범위만 바꾸고(공개면 새 개정으로 공개, 초안이면 그 자리), 없는 날은 초안으로 새로 넣는다(이야기는 비움).
    /// 비운 줄은 건너뛴다. 틀린 줄은 저장하지 않고 날짜별 오류로 돌려준다.
    /// </summary>
    public async Task<(int Added, int Updated, Dictionary<DateOnly, string> Errors)> SaveMonthAsync(string actor, DateOnly first, IReadOnlyList<WordMonthRow> rows, CancellationToken ct = default)
    {
        var errors = new Dictionary<DateOnly, string>();
        int added = 0, updated = 0;
        var current = (await MonthAsync(actor, first, ct)).ToDictionary(r => r.Date);
        foreach (var r in rows.Where(r => r.Changed && current.ContainsKey(r.Date)))
        {
            var was = current[r.Date];
            var blank = string.IsNullOrWhiteSpace(r.Title) && r.Book == 0;
            if (was.Id is null && blank) continue;
            if (was.Id is not null && (r.Title ?? "").Trim() == (was.Title ?? "") && r.Book == was.Book && r.StartChapter == was.StartChapter
                && r.StartVerse == was.StartVerse && r.EndChapter == was.EndChapter && r.EndVerse == was.EndVerse) continue;
            var input = new ContentInput
            {
                Date = r.Date, Title = r.Title?.Trim(), Book = r.Book, Summary = "-",
                StartChapter = (short)(r.StartChapter ?? 0), StartVerse = (short)(r.StartVerse ?? 0), EndChapter = (short)(r.EndChapter ?? r.StartChapter ?? 0), EndVerse = (short)(r.EndVerse ?? 0),
            };
            var check = ContentAdminService.Validate(ContentKind.Word, input);   // 이야기는 이 화면에서 쓰지 않으니 "-" 로 검사만
            if (!check.Ok) { errors[r.Date] = string.Join(" ", check.Values); continue; }

            if (was.Id is null || was.Status != ContentStatus.Published)
            {
                await using var ctx = await OpenAsync(actor, ct);
                var w = await Words(ctx).SingleOrDefaultAsync(x => x.ExposeDate == r.Date, ct);
                if (w is null)
                {
                    w = new DailyWord { Title = "", Summary = "", ExposeDate = r.Date, Status = ContentStatus.Draft, CreatedUtc = time.GetUtcNow().UtcDateTime, CreatedByUserName = actor };
                    ctx.DailyWords.Add(w);
                    added++;
                }
                else updated++;
                (w.Title, w.BookId, w.StartChapter, w.StartVerse, w.EndChapter, w.EndVerse) = (input.Title!, input.Book, input.StartChapter, input.StartVerse, input.EndChapter, input.EndVerse);
                await ctx.SaveChangesAsync(ct);
            }
            else
            {
                var (full, _, _) = await content.LoadAsync(actor, ContentKind.Word, r.Date, ct);
                (full.Title, full.Book, full.StartChapter, full.StartVerse, full.EndChapter, full.EndVerse) = (input.Title, input.Book, input.StartChapter, input.StartVerse, input.EndChapter, input.EndVerse);
                var e = await content.SaveAsync(actor, ContentKind.Word, full, publish: true, ct);
                if (!e.Ok) { errors[r.Date] = string.Join(" ", e.Values); continue; }
                updated++;
            }
        }
        return (added, updated, errors);
    }

    /// <summary>영상 구간 편집의 한 달: 말씀이 있는 날마다 범위의 장(최대 MaxClipChapters) · 장 듣기 영상 · 정한 구간.</summary>
    public async Task<List<ClipDay>> ClipMonthAsync(string actor, DateOnly first, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var last = first.AddMonths(1).AddDays(-1);
        var words = await Words(ctx).AsNoTracking().Where(w => w.ExposeDate >= first && w.ExposeDate <= last).OrderBy(w => w.ExposeDate).ToListAsync(ct);
        var clips = await ctx.DailyWordClips.AsNoTracking().Where(c => c.WordDate >= first && c.WordDate <= last).ToListAsync(ct);
        var books = words.Select(w => w.BookId).Distinct().ToList();
        var media = await ctx.ChapterMedia.AsNoTracking().Where(m => books.Contains(m.BookId) && m.Kind == ChapterMediaKind.Listen)
            .OrderBy(m => m.SortOrder).ToListAsync(ct);
        return words.Select(w =>
        {
            var chapters = new List<ClipChapter>();
            for (var ch = w.StartChapter; ch <= w.EndChapter && chapters.Count < MaxClipChapters; ch++)
            {
                var m = media.FirstOrDefault(x => x.BookId == w.BookId && x.Chapter == ch);
                var c = clips.FirstOrDefault(x => x.WordDate == w.ExposeDate && x.Chapter == ch);
                var cross = w.StartChapter != w.EndChapter;
                var hint = !cross ? $"{w.StartVerse} ~ {w.EndVerse}절" : ch == w.StartChapter ? $"{w.StartVerse}절부터" : ch == w.EndChapter ? $"{w.EndVerse}절까지" : "장 전체";
                chapters.Add(new ClipChapter(ch, c?.YoutubeVideoId ?? m?.YoutubeVideoId, m?.DurationSec, c?.StartSec, c?.EndSec, hint));
            }
            return new ClipDay(w.ExposeDate, BibleBooks.Range(w.BookId, w.StartChapter, w.StartVerse, w.EndChapter, w.EndVerse), w.BookId, chapters);
        }).ToList();
    }

    /// <summary>
    /// 한 장의 시작(s) · 끝(e)을 sec 로 정한다(null = 기본: 처음 · 끝). 그날 범위의 모든 장을 차례대로 다시 저장한다 —
    /// 목록이 있으면 목록의 장만 재생하므로(R-127) 장을 걸치는 날은 모든 장이 있어야 한다.
    /// </summary>
    public async Task<bool> SaveClipAsync(string actor, DateOnly date, short chapter, string which, int? sec, CancellationToken ct = default)
    {
        var first = new DateOnly(date.Year, date.Month, 1);
        var day = (await ClipMonthAsync(actor, first, ct)).FirstOrDefault(d => d.Date == date);
        if (day is null || !day.Chapters.Any(c => c.Chapter == chapter) || which is not ("s" or "e")) return false;
        if (sec is < 0) sec = null;
        List<DailyWordClip> list;
        await using (var ctx = await OpenAsync(actor, ct))
        {
            var saved = await ctx.DailyWordClips.AsNoTracking().Where(c => c.WordDate == date).ToListAsync(ct);
            list = day.Chapters.Select(c =>
            {
                var s = saved.FirstOrDefault(x => x.Chapter == c.Chapter);
                var clip = new DailyWordClip { Chapter = c.Chapter, YoutubeVideoId = s?.YoutubeVideoId, StartSec = s?.StartSec, EndSec = s?.EndSec };
                if (c.Chapter == chapter) { if (which == "s") clip.StartSec = sec; else clip.EndSec = sec; }
                if (clip.StartSec is int a && clip.EndSec is int b && b <= a) clip.EndSec = null;   // 끝이 시작보다 앞이면 끝을 기본으로
                return clip;
            }).ToList();
        }
        if (list.All(c => c.StartSec is null && c.EndSec is null && c.YoutubeVideoId is null)) list = [];   // 모두 기본이면 목록을 비운다(장 기본 영상)
        await content.SaveWordClipsAsync(actor, date, list, ct);
        return true;
    }

    /// <summary>퀴즈 삭제(유년부 [퀴즈 삭제]): 그날 공용 퀴즈의 지금 개정을 내린다(보관). 아이 화면은 "오늘은 없음".</summary>
    public async Task<bool> ArchiveQuizAsync(string actor, DateOnly date, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var q = await ctx.DailyQuizzes.SingleOrDefaultAsync(x => x.TenantId == null && x.IsCurrent && x.ExposeDate == date, ct);
        if (q is null) return false;
        q.IsCurrent = false;
        q.Status = ContentStatus.Archived;
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = "QuizArchive", Detail = $"{date:yyyy-MM-dd} #{q.Id}" });
        await ctx.SaveChangesAsync(ct);
        return true;
    }
}
