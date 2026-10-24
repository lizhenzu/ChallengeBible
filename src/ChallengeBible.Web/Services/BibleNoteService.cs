using ChallengeBible.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ChallengeBible.Web.Services;

/// <summary>해설 한 건(`dbo.eBibleSummary` 한 행): 책 + 시작 장:절 ~ 끝 장:절 범위 하나를 설명한다(R-118, ADR 0056).</summary>
public sealed record BibleNote(int Id, string Book, string? KeyWord, string? Summary, int StartChapter, int StartVerse, int EndChapter, int EndVerse)
{
    /// <summary>"민수기 35:1 ~ 35:8", 한 절이면 "시편 23:1".</summary>
    public string Range => StartChapter == EndChapter && StartVerse == EndVerse
        ? $"{Book} {StartChapter}:{StartVerse}"
        : $"{Book} {StartChapter}:{StartVerse} ~ {EndChapter}:{EndVerse}";

    /// <summary>비었으면(공백뿐 포함) null — 그 줄을 그리지 않는다.</summary>
    public string? Title => string.IsNullOrWhiteSpace(KeyWord) ? null : KeyWord.Trim();
    public string? Body => string.IsNullOrWhiteSpace(Summary) ? null : Summary.Trim();
}

/// <summary>
/// 성경 읽기 화면의 해설 카드(R-118, ADR 0056). 같은 DB 의 기존 표 `dbo.eBibleSummary` 를 **읽기만** 한다.
/// 플랫폼 참조 데이터라 TenantId 가 없다. 접속은 `ConnectionStrings:BibleNote`(읽기 전용 계정)가 있으면 그것을, 없으면 App 을 쓴다.
/// </summary>
public sealed class BibleNoteService(string connectionString, ILogger<BibleNoteService> log)
{
    /// <summary>해설 표가 앱(개역개정)과 다르게 적은 책 이름. 조회할 때 앱 이름과 함께 찾는다(ADR 0056).</summary>
    private static readonly Dictionary<string, string> SourceSpelling = new()
    {
        ["예레미야애가"] = "예레미야 애가",
        ["요한일서"] = "요한1서",
        ["요한이서"] = "요한2서",
        ["요한삼서"] = "요한3서",
    };

    /// <summary>책 하나를 해설 표에서 찾을 이름들: 앱 이름(앞뒤 공백 제거)과, 있으면 해설 표의 다른 표기.</summary>
    public static IReadOnlyList<string> SourceNames(string book)
    {
        var name = book.Trim();
        return SourceSpelling.TryGetValue(name, out var alt) ? [name, alt] : [name];
    }

    /// <summary>
    /// 범위 r 과 겹치는 해설을 한 번의 조회로 가져온다(시작 장:절 순). 조회가 실패하면 로그만 남기고 빈 목록 —
    /// 해설이 없어도 본문은 그대로 보여 준다.
    /// </summary>
    public async Task<IReadOnlyList<BibleNote>> ForRangeAsync(BibleRef r, CancellationToken ct = default)
    {
        if (!r.IsValid) return [];
        var book = r.Book.Trim();
        var names = SourceNames(book);
        try
        {
            await using var conn = new SqlConnection(connectionString);
            await using var cmd = new SqlCommand("""
                SELECT Id, BibleText, BibleKeyWord, BibleSummary, StartChapter, StartVerse, EndChapter, EndVerse
                FROM dbo.eBibleSummary
                WHERE BibleText IN (@book, @alt) AND StartChapter <= @ec AND EndChapter >= @sc
                ORDER BY StartChapter, StartVerse, Id
                """, conn) { CommandTimeout = 10 };
            cmd.Parameters.Add("@book", SqlDbType.NVarChar, 100).Value = names[0];
            cmd.Parameters.Add("@alt", SqlDbType.NVarChar, 100).Value = names[^1];   // 다른 표기가 없으면 같은 이름
            cmd.Parameters.Add("@sc", SqlDbType.Int).Value = (int)r.StartChapter;
            cmd.Parameters.Add("@ec", SqlDbType.Int).Value = (int)r.EndChapter;
            await conn.OpenAsync(ct);
            await using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult, ct);
            var list = new List<BibleNote>();
            while (await rd.ReadAsync(ct))
                list.Add(new(rd.GetInt32(0), book, rd.IsDBNull(2) ? null : rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetString(3),
                    rd.GetInt32(4), rd.GetInt32(5), rd.GetInt32(6), rd.GetInt32(7)));
            return list;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "해설 조회 실패: {Book} {Sc}~{Ec}장 — 해설 없이 본문만 보여 준다", book, r.StartChapter, r.EndChapter);
            return [];
        }
    }

    /// <summary>
    /// 해설마다 어느 절 위에 그릴지 정해 묶는다. 화면 범위의 첫 절(장 하나를 열면 1절)보다 앞에서 시작한 해설은 첫 절 위,
    /// 그 외는 시작 절 위. 범위와 겹치지 않거나 그 절이 본문 목록에 없으면(데이터 불일치) 건너뛴다.
    /// </summary>
    public static IReadOnlyDictionary<(short Chapter, short Verse), IReadOnlyList<BibleNote>> Place(
        IReadOnlyList<BibleNote> notes, BibleRef r, IReadOnlyList<VerseLine> lines)
    {
        var have = lines.Select(l => (l.Chapter, l.Verse)).ToHashSet();
        static bool Before(int c1, int v1, int c2, int v2) => c1 < c2 || (c1 == c2 && v1 < v2);
        return notes
            .Where(n => !Before(n.EndChapter, n.EndVerse, r.StartChapter, r.StartVerse) && !Before(r.EndChapter, r.EndVerse, n.StartChapter, n.StartVerse))
            .Select(n => (Note: n, At: Before(n.StartChapter, n.StartVerse, r.StartChapter, r.StartVerse)
                ? (r.StartChapter, r.StartVerse)
                : ((short)n.StartChapter, (short)n.StartVerse)))
            .Where(x => have.Contains(x.At))
            .GroupBy(x => x.At)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<BibleNote>)g.Select(x => x.Note)
                .OrderBy(n => n.StartChapter).ThenBy(n => n.StartVerse).ThenBy(n => n.Id).ToList());
    }
}
