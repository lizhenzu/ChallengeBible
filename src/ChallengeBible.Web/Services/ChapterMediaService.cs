using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>"듣기 · 영상" 줄의 버튼 하나. 재생 창에는 유튜브 영상 번호(11자)만 넘긴다.</summary>
public sealed record MediaButton(string Icon, string Title, string Sub, string VideoId, string PlayerTitle, bool Listen);

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
