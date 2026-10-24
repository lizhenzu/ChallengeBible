using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace ChallengeBible.Web.Api;

public sealed record RangeRequest(byte Book, short StartChapter, short StartVerse, short EndChapter, short EndVerse);

/// <summary>
/// 성경 읽기 — 계정 범위 JSON API(API 계약 5-3, 0029): 절 읽음 · 범위 읽음 · 책갈피.
/// 켜기 PUT · 끄기 DELETE 만 있다(토글 없음, X-17). 로그인 + 위조 방지 토큰. 영역(slug)과 무관하다.
/// </summary>
public static class BibleApi
{
    public static void MapBibleApi(this WebApplication app)
    {
        var g = app.MapGroup("/api/me/bible").RequireAuthorization().AddEndpointFilter<AntiforgeryEndpointFilter>();

        g.MapPut("/read/{book}/{chapter}/{verse}", async (byte book, short chapter, short verse, BibleMarkService s, CancellationToken ct) =>
            await s.ReadAsync(book, chapter, verse, ct) ? Results.Ok(new { ok = true, read = true }) : NoVerse());
        g.MapDelete("/read/{book}/{chapter}/{verse}", async (byte book, short chapter, short verse, BibleMarkService s, CancellationToken ct) =>
        {
            await s.UnreadAsync(book, chapter, verse, ct);
            return Results.Ok(new { ok = true, read = false });
        });

        g.MapPut("/read-range", async (RangeRequest r, BibleMarkService s, CancellationToken ct) =>
        {
            var range = new BibleRef(r.Book, r.StartChapter, r.StartVerse, r.EndChapter, r.EndVerse);
            if (!range.IsValid) return BadRange();
            return Results.Ok(new { ok = true, added = await s.ReadRangeAsync(range, ct) });
        });
        // 범위 해제: DELETE 는 본문을 받지 않는 프록시가 있어 같은 값을 쿼리로도 받는다
        g.MapDelete("/read-range", async (byte book, short startChapter, short startVerse, short endChapter, short endVerse, BibleMarkService s, CancellationToken ct) =>
        {
            var range = new BibleRef(book, startChapter, startVerse, endChapter, endVerse);
            if (!range.IsValid) return BadRange();
            return Results.Ok(new { ok = true, removed = await s.UnreadRangeAsync(range, ct) });
        });

        g.MapPut("/bookmarks/{book}/{chapter}/{verse}", async (byte book, short chapter, short verse, BibleMarkService s, CancellationToken ct) =>
            await s.BookmarkAsync(book, chapter, verse, ct) ? Results.Ok(new { ok = true, bookmarked = true }) : NoVerse());
        g.MapDelete("/bookmarks/{book}/{chapter}/{verse}", async (byte book, short chapter, short verse, BibleMarkService s, CancellationToken ct) =>
        {
            await s.UnbookmarkAsync(book, chapter, verse, ct);
            return Results.Ok(new { ok = true, bookmarked = false });
        });
    }

    private static IResult NoVerse() => Results.NotFound(new { code = "NoVerse", message = "없는 절이에요." });
    private static IResult BadRange() => Results.BadRequest(new { code = "BadRange", message = "구절 범위를 확인해 주세요." });
}

/// <summary>위조 방지 토큰(헤더 RequestVerificationToken)만 검사한다 — 계정 범위 API 용.</summary>
public sealed class AntiforgeryEndpointFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        await antiforgery.IsRequestValidAsync(context.HttpContext)
            ? await next(context)
            : Results.BadRequest(new { code = "BadToken", message = "화면을 새로 고쳐 주세요." });
}
