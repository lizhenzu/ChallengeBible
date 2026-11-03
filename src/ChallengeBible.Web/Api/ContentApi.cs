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

/// <summary>
/// 공용 콘텐츠 입력 API(ADR 0079, R-125): `PUT /api/platform/quizzes/{yyyy-MM-dd}` 로 하루치 퀴즈를 넣는다.
/// 프로그램이 부르므로 쿠키 대신 `X-Api-Key`. 키가 설정되어 있지 않으면 없는 주소처럼 답한다.
/// 저장은 화면과 같은 <see cref="ContentAdminService"/> — 공개된 날을 다시 넣으면 새 개정이 되고 지난 기록은 그대로(R-124).
/// </summary>
public static class ContentApi
{
    public const string KeyHeader = "X-Api-Key";
    public const int MinKeyLength = 32, MaxChoices = 4, MaxQuestion = 1000, MaxChoice = 300, MaxAnswer = 200, MaxLongText = 4000;

    public static void MapContentApi(this WebApplication app) =>
        app.MapPut("/api/platform/quizzes/{date}", PutQuizAsync).AllowAnonymous();

    private static async Task<IResult> PutQuizAsync(string date, HttpContext http, IConfiguration config, ContentAdminService content, ILoggerFactory logs, CancellationToken ct)
    {
        var key = config["ContentApi:Key"];
        if (key is null || key.Length < MinKeyLength) return Results.NotFound();
        if (!SameKey(key, http.Request.Headers[KeyHeader].ToString()))
            return Fail(StatusCodes.Status401Unauthorized, "BadKey", "API 키가 맞지 않아요.");
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return Fail(StatusCodes.Status400BadRequest, "BadDate", "날짜는 yyyy-MM-dd 로 써 주세요.");

        QuizPutRequest? body;
        try { body = await http.Request.ReadFromJsonAsync<QuizPutRequest>(ct); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or BadHttpRequestException)
        { return Fail(StatusCodes.Status400BadRequest, "BadJson", "본문은 JSON 이어야 해요."); }
        if (body is null) return Fail(StatusCodes.Status400BadRequest, "BadJson", "본문은 JSON 이어야 해요.");

        var errors = Check(body);
        if (!errors.Ok) return Invalid(errors);

        if (!int.TryParse(config["ContentApi:ActorAccountId"], out var actor))
            return NotReady(logs, "ContentApi:ActorAccountId 가 없어요.");
        var choice = (body.Format ?? QuizFormat.Choice) == QuizFormat.Choice;
        var input = new ContentInput
        {
            Date = day, Question = body.Question, Background = body.Background, Format = body.Format ?? QuizFormat.Choice, Explanation = body.Explanation,
            Choices = choice ? body.Choices! : [],
            CorrectIndex = choice ? body.CorrectNumber!.Value - 1 : -1,
            AcceptedAnswers = choice ? null : string.Join("\n", body.AcceptedAnswers!),
        };
        try
        {
            var created = (await content.LoadAsync(actor, ContentKind.Quiz, day, ct)).Id is null;
            errors = await content.SaveAsync(actor, ContentKind.Quiz, input, body.Publish, ct);
            if (!errors.Ok) return Invalid(errors);
            var (_, status, id) = await content.LoadAsync(actor, ContentKind.Quiz, day, ct);
            return Results.Ok(new { date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), id, status, created });
        }
        catch (UnauthorizedAccessException) { return NotReady(logs, "ContentApi:ActorAccountId 가 활성 플랫폼 관리자가 아니에요."); }
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
