using System.Text.Json;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>"듣기 · 영상" 줄의 버튼 하나. 재생 창에는 유튜브 영상 번호(11자)만 넘긴다.</summary>
/// <param name="Clips">그날 구간 재생 목록(JSON, ADR 0081). null 이면 영상 전체를 반복한다.</param>
/// <param name="ClipLines">버튼에 보여 줄 재생 구간(ADR 0081). null 이면 <paramref name="Sub"/> 를 보여 준다.</param>
public sealed record MediaButton(string Icon, string Title, string Sub, string VideoId, string PlayerTitle, bool Listen, string? Clips = null, IReadOnlyList<ClipLine>? ClipLines = null);

/// <summary>버튼에 보여 줄 구간 한 줄: "3장" · "1:05 ~ 7:40".</summary>
public sealed record ClipLine(string Chapter, string Range);

/// <summary>재생 창에 넘기는 구간 하나: 영상 번호 · 시작(초) · 끝(초, 없으면 영상 끝까지).</summary>
public sealed record PlayClip(string V, int S, int? E);

/// <summary>
/// 듣기 · 영상(R-166, ADR 0063, 확정 시안 통독-본문읽기-듣기영상): 그 장의 듣기 · 해설(`app.ChapterMedia`) + 그 책의 개요 해설(책 개요 영상).
/// 누르면 화면 안 재생 창에서 처음부터 무한 반복(mission.js). 영상 번호로 바꿀 수 없는 값은 버린다.
/// </summary>
public sealed partial class ChapterMediaService(IAppDb db)
{
    public async Task<IReadOnlyList<MediaButton>> ButtonsAsync(byte bookId, short chapter, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var media = await ctx.ChapterMedia.AsNoTracking().Where(m => m.BookId == bookId && m.Chapter == chapter)
            .OrderBy(m => m.Kind == ChapterMediaKind.Listen ? 0 : 1).ThenBy(m => m.SortOrder).ToListAsync(ct);
        var book = await ctx.BibleBookOverviews.AsNoTracking().Where(o => o.BookId == bookId)
            .Select(o => new { o.VideoUrl1, o.VideoUrl2 }).FirstOrDefaultAsync(ct);
        return Buttons(bookId, chapter, media, book is null ? [] : [book.VideoUrl1, book.VideoUrl2]);
    }

    public static IReadOnlyList<MediaButton> Buttons(byte bookId, short chapter, IEnumerable<ChapterMedia> media, IEnumerable<string?> bookVideos)
    {
        var name = BibleBooks.NameOf(bookId);
        var unit = bookId == 19 ? "편" : "장";
        var list = new List<MediaButton>();
        foreach (var m in media)
        {
            if (VideoId(m.YoutubeVideoId) is not { } id) continue;
            var listen = m.Kind == ChapterMediaKind.Listen;
            var title = m.Title ?? (listen ? "유튜브 듣기" : "장 해설");
            var sub = $"{name} {chapter}{unit}" + (m.DurationSec is int s and > 0 ? $" · {s / 60}:{s % 60:00}" : "");
            list.Add(new(listen ? "🎧" : "🎬", title, sub, id, $"{name} {chapter}{unit} {(listen ? "듣기" : title)}", listen));
        }
        var ov = bookVideos.Select(VideoId).OfType<string>().Distinct().ToList();
        for (var i = 0; i < ov.Count; i++)
        {
            var title = ov.Count > 1 ? $"개요 해설 {i + 1}" : "개요 해설";
            list.Add(new("🎬", title, name, ov[i], $"{name} {title}", false));
        }
        return list;
    }

    /// <summary>그날 영상 구간이 있으면 "듣기" 버튼이 장 전체 대신 그 구간을 재생하게 한다(R-127, ADR 0081). 구간이 없는 날은 그대로.</summary>
    public async Task<IReadOnlyList<MediaButton>> WithWordClipsAsync(IReadOnlyList<MediaButton> buttons, byte bookId, short chapter, DateOnly date, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var clips = await ctx.DailyWordClips.AsNoTracking().Where(c => c.WordDate == date).OrderBy(c => c.Seq).ToListAsync(ct);
        if (clips.Count == 0) return WithClips(buttons, bookId, chapter, clips, []);
        var chapters = clips.Where(c => c.YoutubeVideoId is null).Select(c => c.Chapter).Distinct().ToList();
        var listen = await ctx.ChapterMedia.AsNoTracking().Where(m => m.BookId == bookId && m.Kind == ChapterMediaKind.Listen && chapters.Contains(m.Chapter))
            .OrderBy(m => m.SortOrder).ToListAsync(ct);
        return WithClips(buttons, bookId, chapter, clips, listen);
    }

    /// <summary>구간을 재생 목록으로 바꾼다. 영상이 비었으면 그 장의 기본 듣기 영상, 그것도 없으면 그 구간은 건너뛴다. 남는 구간이 없으면 듣기 버튼에 "3장 전체"라고 쓴다.</summary>
    public static IReadOnlyList<MediaButton> WithClips(IReadOnlyList<MediaButton> buttons, byte bookId, short chapter, IEnumerable<DailyWordClip> clips, IEnumerable<ChapterMedia> listen)
    {
        var unit = bookId == 19 ? "편" : "장";
        var name = BibleBooks.NameOf(bookId);
        var place = $"{name} {chapter}{unit}";
        var usable = clips.Select(c => (Clip: c, Id: VideoId(c.YoutubeVideoId) ?? listen.Where(m => m.Chapter == c.Chapter).Select(m => VideoId(m.YoutubeVideoId)).FirstOrDefault(v => v is not null)))
            .Where(c => c.Id is not null).ToList();
        if (usable.Count == 0)
            return buttons.Select(b => b.Listen ? b with { Sub = b.Sub.Replace(place, $"{chapter}{unit} 전체") } : b).ToList();

        var json = JsonSerializer.Serialize(usable.Select(c => new PlayClip(c.Id!, c.Clip.StartSec ?? 0, c.Clip.EndSec)), JsonSerializerOptions.Web);
        var lines = usable.Select(c => new ClipLine($"{c.Clip.Chapter}{unit}", ClipRange(c.Clip.StartSec, c.Clip.EndSec))).ToList();
        var first = usable[0].Id!;
        var list = buttons.ToList();
        var at = list.FindIndex(b => b.Listen);
        if (at >= 0) list[at] = list[at] with { Sub = place, VideoId = first, Clips = json, ClipLines = lines };
        else list.Insert(0, new("🎧", "유튜브 듣기", place, first, $"{place} 듣기", true, json, lines));
        return list;
    }

    /// <summary>재생 구간 글: "1:05 ~ 7:40", 비었으면 "처음" · "끝까지", 둘 다 비었으면 "전체".</summary>
    public static string ClipRange(int? start, int? end)
    {
        static string Clock(int s) => $"{s / 60}:{s % 60:00}";
        if (start is null && end is null) return "전체";
        return $"{(start is { } a ? Clock(a) : "처음")} ~ {(end is { } b ? Clock(b) : "끝까지")}";
    }

    /// <summary>유튜브 영상 번호(11자)를 뽑는다: 번호만 · watch?v= · youtu.be/ · /embed/ · /shorts/. 못 찾으면 null.</summary>
    public static string? VideoId(string? value)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (IdRx().IsMatch(v)) return v;
        var m = UrlRx().Match(v);
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex IdRx();

    [GeneratedRegex(@"^https?://(?:www\.|m\.)?(?:youtube\.com/(?:watch\?(?:.*&)?v=|embed/|shorts/)|youtu\.be/)([A-Za-z0-9_-]{11})(?:[?&#/].*)?$")]
    private static partial Regex UrlRx();
}
