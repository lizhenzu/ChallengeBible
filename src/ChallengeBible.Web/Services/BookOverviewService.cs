using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record OverviewRow(string Label, string Text, bool Chips = false);

/// <summary>본문 위 책 개요 카드 하나(R-116, ADR 0062). 글이 없는 칸은 빼고, 간략본 · 상세본 중 빈 쪽은 탭을 숨긴다.</summary>
public sealed record BookOverviewCard(byte BookId, string BookName, string Line, bool Open,
    IReadOnlyList<OverviewRow> Brief, IReadOnlyList<OverviewRow> Detail);

/// <summary>
/// 책 개요(R-116, ADR 0062): 공용 표 `app.BibleBookOverview` 를 읽는다(플랫폼 참조 데이터 — 모든 부서 같음).
/// 그 책 1장이면 펼친 채로, 다른 장이면 접힌 채로(한 줄 개요) 보인다. 책 개요 영상은 카드가 아니라 "듣기 · 영상" 줄에 보인다(<see cref="ChapterMediaService"/>, ADR 0063).
/// </summary>
public sealed class BookOverviewService(IAppDb db)
{
    public async Task<BookOverviewCard?> CardAsync(byte bookId, short chapter, CancellationToken ct = default)
    {
        await using var ctx = db.Create();
        var o = await ctx.BibleBookOverviews.AsNoTracking().FirstOrDefaultAsync(x => x.BookId == bookId, ct);
        return o is null ? null : Card(o, chapter);
    }

    public static BookOverviewCard? Card(BibleBookOverview o, short chapter)
    {
        static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        static void Add(List<OverviewRow> rows, string label, string? text, bool chips = false)
        {
            if (T(text) is { } t) rows.Add(new(label, t, chips));
        }

        var brief = new List<OverviewRow>();
        Add(brief, "저자", o.Author);
        Add(brief, "기록 시기", o.WhenWritten);
        Add(brief, "개요", o.Summary);
        Add(brief, "기록 목적", o.Purpose);

        var detail = new List<OverviewRow>();
        Add(detail, "저자", string.Join("\n", new[] { T(o.DetailAuthor), T(o.AuthorDescription) }.Where(x => x is not null)));
        var dates = (T(o.WrittenFrom), T(o.WrittenTo)) switch
        {
            ({ } f, { } t) when f != t => $"{f} ~ {t}",
            ({ } f, _) => f,
            (_, { } t) => t,
            _ => null,
        };
        if (T(o.AlternativeDate) is { } alt) dates = dates is null ? alt : $"{dates}(다른 설: {alt})";
        Add(detail, "기록 연대", dates);
        Add(detail, "장소", o.WrittenPlace);
        Add(detail, "대상", o.WrittenTarget);
        Add(detail, "시대", o.HistoricalPeriod);
        Add(detail, "역사 배경", o.HistoricalBackground);
        Add(detail, "핵심어", o.Keywords, chips: true);
        Add(detail, "내용 요약", o.ContentSummary);

        if (brief.Count == 0 && detail.Count == 0) return null;   // 글이 없는 책은 카드를 숨김
        var line = (T(o.Summary) ?? T(o.ContentSummary) ?? "").Split('\n')[0];
        return new BookOverviewCard(o.BookId, BibleBooks.NameOf(o.BookId), line, chapter == 1, brief, detail);
    }

    /// <summary>핵심어 "창조, 언약 · 족장" → 칩 하나씩.</summary>
    public static IEnumerable<string> Chips(string text) =>
        text.Split([',', '，', '·', '/', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
