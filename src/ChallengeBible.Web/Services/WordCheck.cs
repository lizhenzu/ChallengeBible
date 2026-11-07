using System.Globalization;
using ChallengeBible.Infrastructure.Data;

namespace ChallengeBible.Web.Services;

/// <summary>영상 구간 한 줄: "24장" · "1:15 ~ 3:26".</summary>
public sealed record WordCheckClip(string Chapter, string Range);

/// <summary>말씀 입력 점검의 하루(R-216). 말씀이 없는 날은 목록에 없다.</summary>
public sealed record WordCheckDay(int Id, string Date, string Status, string? Ref, string? Title, string? Summary, string? Commentary,
    IReadOnlyList<WordCheckClip> Clips, IReadOnlyList<string> Missing);

/// <summary>말씀 한 줄의 입력 여부 판정(R-216, 확정 시안 word-check). 화면과 DB 를 모르는 순수 함수다.</summary>
public static class WordCheck
{
    public const string RangeItem = "성경 범위", TitleItem = "제목", StoryItem = "오늘 이야기", ClipItem = "영상 구간";
    private const int BookCount = 66;

    /// <param name="word">그날의 현재 개정(IsCurrent).</param>
    /// <param name="clips">그날 영상 구간. 말씀의 장 범위 밖 구간은 세지 않는다.</param>
    public static WordCheckDay Day(DailyWord word, IEnumerable<DailyWordClip> clips)
    {
        var rangeOk = word.BookId is >= 1 and <= BookCount
            && word.StartChapter >= 1 && word.StartVerse >= 1 && word.EndChapter >= 1 && word.EndVerse >= 1;
        var inRange = clips.Where(c => c.Chapter >= word.StartChapter && c.Chapter <= word.EndChapter).OrderBy(c => c.Seq).ToList();

        var missing = new List<string>(4);
        if (!rangeOk) missing.Add(RangeItem);
        if (string.IsNullOrWhiteSpace(word.Title)) missing.Add(TitleItem);
        if (string.IsNullOrWhiteSpace(word.Summary)) missing.Add(StoryItem);
        if (!inRange.Any(c => c.StartSec > 0 || c.EndSec > 0)) missing.Add(ClipItem);

        var unit = word.BookId == 19 ? "편" : "장";
        return new WordCheckDay(word.Id, word.ExposeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), word.Status,
            rangeOk ? BibleBooks.Range(word.BookId, word.StartChapter, word.StartVerse, word.EndChapter, word.EndVerse) : null,
            Clean(word.Title), Clean(word.Summary), Clean(word.Commentary),
            inRange.Select(c => new WordCheckClip($"{c.Chapter}{unit}", ChapterMediaService.ClipRange(c.StartSec, c.EndSec))).ToList(),
            missing);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
