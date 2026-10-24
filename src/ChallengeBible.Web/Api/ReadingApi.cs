using ChallengeBible.Web.Services;

namespace ChallengeBible.Web.Api;

public sealed record PositionRequest(short Verse);

/// <summary>
/// 도전!!!성경통독 — 계정 범위 JSON API(API 계약 5-3b, 0040 · 0042). 켜기 PUT · 끄기 DELETE(X-17), 로그인 + 위조 방지 토큰.
/// 응답 `{ ok, chapterDone, progress, roundCompleted, newRoundNo }`.
/// </summary>
public static class ReadingApi
{
    public static void MapReadingApi(this WebApplication app)
    {
        var g = app.MapGroup("/api/me/reading").RequireAuthorization().AddEndpointFilter<AntiforgeryEndpointFilter>();

        g.MapPut("/chapters/{book}/{chapter}", async (byte book, short chapter, ReadingService s, CancellationToken ct) => Reply(await s.CompleteChapterAsync(book, chapter, ct)));
        g.MapDelete("/chapters/{book}/{chapter}", async (byte book, short chapter, ReadingService s, CancellationToken ct) => Reply(await s.UndoChapterAsync(book, chapter, ct)));

        g.MapPut("/verses/{book}/{chapter}/{verse}", async (byte book, short chapter, short verse, ReadingService s, CancellationToken ct) =>
            Reply(await s.SetVersesAsync(book, chapter, verse, verse, true, ct)));
        g.MapDelete("/verses/{book}/{chapter}/{verse}", async (byte book, short chapter, short verse, ReadingService s, CancellationToken ct) =>
            Reply(await s.SetVersesAsync(book, chapter, verse, verse, false, ct)));
        // 이 페이지 모두 읽음 · 취소(보이는 절 범위, R-160)
        g.MapPut("/verses/{book}/{chapter}", async (byte book, short chapter, short from, short to, ReadingService s, CancellationToken ct) =>
            Reply(await s.SetVersesAsync(book, chapter, from, to, true, ct)));
        g.MapDelete("/verses/{book}/{chapter}", async (byte book, short chapter, short from, short to, ReadingService s, CancellationToken ct) =>
            Reply(await s.SetVersesAsync(book, chapter, from, to, false, ct)));

        g.MapPut("/position/{book}/{chapter}", async (byte book, short chapter, PositionRequest r, ReadingService s, CancellationToken ct) =>
            await s.SavePositionAsync(book, chapter, r.Verse, ct) ? Results.Ok(new { ok = true }) : NotFound());

        g.MapPost("/rounds", async (ReadingService s, CancellationToken ct) => Results.Ok(new { ok = true, roundNo = await s.StartNewRoundAsync(ct) }));
    }

    private static IResult Reply(ReadingResult r) => r.Ok
        ? Results.Ok(new { ok = true, chapterDone = r.ChapterDone, progress = r.Progress, roundCompleted = r.RoundCompleted, newRoundNo = r.NewRoundNo })
        : NotFound(r.Error);

    private static IResult NotFound(string? message = null) => Results.NotFound(new { code = "NoVerse", message = message ?? "없는 장 · 절이에요." });
}
