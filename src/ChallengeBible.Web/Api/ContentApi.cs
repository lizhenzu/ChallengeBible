using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Services;

namespace ChallengeBible.Web.Api;

/// <summary>퀴즈 입력 요청 본문(ADR 0079). correctNumber 는 보기 번호(1부터).</summary>
public sealed record QuizPutRequest(string? Question, string? Background, string? Format, List<string>? Choices, int? CorrectNumber,
    List<string>? AcceptedAnswers, string? Explanation, bool Publish = true);

/// <summary>말씀 입력 요청 본문(ADR 0080). book 은 성경 책 번호(1 창세기 ~ 66 요한계시록).</summary>
public sealed record WordPutRequest(string? Title, int? Book, int? StartChapter, int? StartVerse, int? EndChapter, int? EndVerse,
    string? Summary, string? Commentary, bool Publish = true);

public sealed record QuizPutResult(FieldErrors Errors, bool Created, string? Status, int? Id);

/// <summary>
/// 공용 콘텐츠 입력 API: `PUT /api/platform/quizzes/{yyyy-MM-dd}`(ADR 0079, R-125) · `PUT /api/platform/words/{yyyy-MM-dd}`(ADR 0080, R-126) 로 하루치를 넣는다.
/// 프로그램이 부르므로 쿠키 대신 `X-Api-Key`. 키가 설정되어 있지 않으면 없는 주소처럼 답한다.
/// 저장은 화면과 같은 <see cref="ContentAdminService"/> — 공개된 날을 다시 넣으면 새 개정이 되고 지난 기록은 그대로(R-124).
/// </summary>
public static class ContentApi
{
    public const string KeyHeader = "X-Api-Key";
    public const int MinKeyLength = 32, MaxChoices = 4, MaxQuestion = 1000, MaxChoice = 300, MaxAnswer = 200, MaxLongText = 4000,
        MaxWordTitle = 100, MaxWordText = 20000, MaxBook = 66, MaxChapter = 150, MaxVerse = 176;

    public static void MapContentApi(this WebApplication app)
    {
        app.MapPut("/api/platform/quizzes/{date}", (string date, HttpContext http, IConfiguration config, ContentAdminService content, ILoggerFactory logs, CancellationToken ct) =>
            PutAsync<QuizPutRequest>(date, http, config, logs, Check, (actor, day, body) => SaveAsync(content, actor, day, body, ct), ct)).AllowAnonymous();
        app.MapPut("/api/platform/words/{date}", (string date, HttpContext http, IConfiguration config, ContentAdminService content, ILoggerFactory logs, CancellationToken ct) =>
            PutAsync<WordPutRequest>(date, http, config, logs, Check, (actor, day, body) => SaveAsync(content, actor, day, body, ct), ct)).AllowAnonymous();
    }

    private static async Task<IResult> PutAsync<T>(string date, HttpContext http, IConfiguration config, ILoggerFactory logs,
        Func<T, FieldErrors> check, Func<int, DateOnly, T, Task<QuizPutResult>> save, CancellationToken ct) where T : class
    {
        var key = config["ContentApi:Key"];
        if (key is null || key.Length < MinKeyLength) return Results.NotFound();
        if (!SameKey(key, http.Request.Headers[KeyHeader].ToString()))
            return Fail(StatusCodes.Status401Unauthorized, "BadKey", "API 키가 맞지 않아요.");
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return Fail(StatusCodes.Status400BadRequest, "BadDate", "날짜는 yyyy-MM-dd 로 써 주세요.");

        T? body;
        try { body = await http.Request.ReadFromJsonAsync<T>(ct); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or BadHttpRequestException)
        { return Fail(StatusCodes.Status400BadRequest, "BadJson", "본문은 JSON 이어야 해요."); }
        if (body is null) return Fail(StatusCodes.Status400BadRequest, "BadJson", "본문은 JSON 이어야 해요.");

        var errors = check(body);
        if (!errors.Ok) return Invalid(errors);

        if (!int.TryParse(config["ContentApi:ActorAccountId"], out var actor))
            return NotReady(logs, "ContentApi:ActorAccountId 가 없어요.");
        try
        {
            var r = await save(actor, day, body);
            if (!r.Errors.Ok) return Invalid(r.Errors);
            return Results.Ok(new { date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), id = r.Id, status = r.Status, created = r.Created });
        }
        catch (UnauthorizedAccessException) { return NotReady(logs, "ContentApi:ActorAccountId 가 활성 플랫폼 관리자가 아니에요."); }
    }

    /// <summary>검사하고 저장(API · 관리 화면 "퀴즈 넣기" 공통). 플랫폼 관리자가 아니면 UnauthorizedAccessException.</summary>
    public static async Task<QuizPutResult> SaveAsync(ContentAdminService content, int actor, DateOnly day, QuizPutRequest body, CancellationToken ct = default)
    {
        var errors = Check(body);
        if (!errors.Ok) return new(errors, false, null, null);
        var choice = (body.Format ?? QuizFormat.Choice) == QuizFormat.Choice;
        var input = new ContentInput
        {
            Date = day, Question = body.Question, Background = body.Background, Format = body.Format ?? QuizFormat.Choice, Explanation = body.Explanation,
            Choices = choice ? body.Choices! : [],
            CorrectIndex = choice ? body.CorrectNumber!.Value - 1 : -1,
            AcceptedAnswers = choice ? null : string.Join("\n", body.AcceptedAnswers!),
        };
        return await StoreAsync(content, actor, ContentKind.Quiz, day, input, body.Publish, ct);
    }

    /// <summary>말씀을 검사하고 저장. 플랫폼 관리자가 아니면 UnauthorizedAccessException.</summary>
    public static async Task<QuizPutResult> SaveAsync(ContentAdminService content, int actor, DateOnly day, WordPutRequest body, CancellationToken ct = default)
    {
        var errors = Check(body);
        if (!errors.Ok) return new(errors, false, null, null);
        var input = new ContentInput
        {
            Date = day, Title = body.Title, Summary = body.Summary, Commentary = body.Commentary,
            Book = (byte)body.Book!.Value,
            StartChapter = (short)body.StartChapter!.Value, StartVerse = (short)body.StartVerse!.Value,
            EndChapter = (short)body.EndChapter!.Value, EndVerse = (short)body.EndVerse!.Value,
        };
        return await StoreAsync(content, actor, ContentKind.Word, day, input, body.Publish, ct);
    }

    private static async Task<QuizPutResult> StoreAsync(ContentAdminService content, int actor, ContentKind kind, DateOnly day, ContentInput input, bool publish, CancellationToken ct)
    {
        var created = (await content.LoadAsync(actor, kind, day, ct)).Id is null;
        var errors = await content.SaveAsync(actor, kind, input, publish, ct);
        if (!errors.Ok) return new(errors, false, null, null);
        var (_, status, id) = await content.LoadAsync(actor, kind, day, ct);
        return new(errors, created, status, id);
    }

    /// <summary>말씀: DB 칸에 들어가는 값인지 먼저 본다. 책의 장 수 · 범위 순서는 <see cref="ContentAdminService.Validate"/> 가 본다.</summary>
    public static FieldErrors Check(WordPutRequest r)
    {
        var e = new FieldErrors();
        if (string.IsNullOrWhiteSpace(r.Title)) e["title"] = "제목을 써 주세요.";
        else if (r.Title.Trim().Length > MaxWordTitle) e["title"] = $"제목은 {MaxWordTitle}자까지예요.";
        if (string.IsNullOrWhiteSpace(r.Summary)) e["summary"] = "오늘 이야기를 써 주세요.";
        else if (r.Summary.Trim().Length > MaxWordText) e["summary"] = $"오늘 이야기는 {MaxWordText}자까지예요.";
        if (r.Commentary?.Trim().Length > MaxWordText) e["commentary"] = $"해설은 {MaxWordText}자까지예요.";
        if (r.Book is not (>= 1 and <= MaxBook)) e["book"] = $"책 번호는 1 ~ {MaxBook} 이에요.";
        if (r.StartChapter is not (>= 1 and <= MaxChapter)) e["startChapter"] = "시작 장을 써 주세요.";
        if (r.StartVerse is not (>= 1 and <= MaxVerse)) e["startVerse"] = "시작 절을 써 주세요.";
        if (r.EndChapter is not (>= 1 and <= MaxChapter)) e["endChapter"] = "끝 장을 써 주세요.";
        if (r.EndVerse is not (>= 1 and <= MaxVerse)) e["endVerse"] = "끝 절을 써 주세요.";
        return e;
    }

    /// <summary>화면 검사(<see cref="ContentAdminService.Validate"/>)가 보지 않는 API 값 · DB 칸 길이를 먼저 본다.</summary>
    public static FieldErrors Check(QuizPutRequest r)
    {
        var e = new FieldErrors();
        static bool Blank(string? s) => string.IsNullOrWhiteSpace(s);
        static bool Long(string? s, int max) => s?.Trim().Length > max;

        if (Blank(r.Question)) e["question"] = "문제를 써 주세요.";
        else if (Long(r.Question, MaxQuestion)) e["question"] = $"문제는 {MaxQuestion}자까지예요.";
        if (Long(r.Background, MaxLongText)) e["background"] = $"배경은 {MaxLongText}자까지예요.";
        if (Long(r.Explanation, MaxLongText)) e["explanation"] = $"해설은 {MaxLongText}자까지예요.";

        switch (r.Format ?? QuizFormat.Choice)
        {
            case QuizFormat.Choice:
                var choices = r.Choices ?? [];
                if (choices.Count is < 2 or > MaxChoices) e["choices"] = $"보기는 2 ~ {MaxChoices}개예요.";
                else if (choices.Any(c => Blank(c))) e["choices"] = "빈 보기가 있어요.";
                else if (choices.Any(c => Long(c, MaxChoice))) e["choices"] = $"보기는 {MaxChoice}자까지예요.";
                if (r.CorrectNumber is not { } n || n < 1 || n > choices.Count) e["correctNumber"] = "정답 보기 번호(1부터)를 써 주세요.";
                break;
            case QuizFormat.Text:
                var answers = r.AcceptedAnswers ?? [];
                if (answers.Count == 0 || answers.Any(a => Blank(a))) e["acceptedAnswers"] = "맞는 답을 하나 이상 써 주세요.";
                else if (answers.Any(a => Long(a, MaxAnswer))) e["acceptedAnswers"] = $"답은 {MaxAnswer}자까지예요.";
                break;
            default:
                e["format"] = "형식은 Choice 또는 Text 예요.";
                break;
        }
        return e;
    }

    // 길이가 달라도 시간 차이가 나지 않게 해시를 비교한다
    private static bool SameKey(string expected, string given) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(given)));

    private static IResult Fail(int status, string code, string message) =>
        Results.Json(new { code, message }, statusCode: status);

    private static IResult Invalid(FieldErrors errors) =>
        Results.Json(new { code = "Invalid", message = "입력 값을 확인해 주세요.", errors = errors.ToDictionary(x => JsonNamingPolicy.CamelCase.ConvertName(x.Key), x => x.Value) }, statusCode: StatusCodes.Status400BadRequest);

    private static IResult NotReady(ILoggerFactory logs, string why)
    {
        logs.CreateLogger(nameof(ContentApi)).LogError("{Why}", why);
        return Fail(StatusCodes.Status503ServiceUnavailable, "NotReady", "서버 설정을 확인해 주세요.");
    }
}
