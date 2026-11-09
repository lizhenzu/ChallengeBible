using System.Globalization;
using System.Text.Json;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using ChallengeBible.Web.Api;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>
/// `/platform/quiz-input` 퀴즈 넣기(ADR 0079, 확정 시안 qput.png): 날짜 + 입력 API 와 같은 JSON. 플랫폼 관리자만.
/// API 키를 브라우저에 두지 않으려고 주소를 부르지 않고 같은 코드(<see cref="ContentApi.SaveAsync"/>)를 서버에서 부른다.
/// </summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class QuizInputModel(ContentAdminService content, TimeProvider time) : PageModel, ITitled
{
    public string Title => "퀴즈 넣기";

    [BindProperty(SupportsGet = true)] public string? Date { get; set; }
    [BindProperty] public string? Json { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ok { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public const string Sample = """
        {
          "question": "삼손이 마지막 기도에서 구한 말은 무엇인가요?",
          "background": "눈을 잃은 삼손은 다곤 신전으로 끌려 나왔습니다.",
          "format": "Choice",
          "choices": ["옥에서 건져 주소서", "눈을 보게 하소서", "이번만 강하게 하소서", "원수를 용서하소서"],
          "correctNumber": 3,
          "explanation": "삼손은 이번만 강하게 해 달라고 구했습니다(삿 16:28).",
          "publish": true
        }
        """;

    public void OnGet() => Date ??= Today();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return Fail("날짜를 골라 주세요.");

        QuizPutRequest? body;
        try { body = JsonSerializer.Deserialize<QuizPutRequest>(Json ?? "", JsonSerializerOptions.Web); }
        catch (JsonException) { return Fail("JSON 모양이 틀렸어요. 따옴표 · 쉼표 · 괄호를 확인해 주세요."); }
        if (body is null) return Fail("JSON 을 넣어 주세요.");

        var r = await ContentApi.SaveAsync(content, User.UserName()!, day, body, ct);
        if (!r.Errors.Ok) return Fail([.. r.Errors.Select(x => $"{JsonNamingPolicy.CamelCase.ConvertName(x.Key)}: {x.Value}")]);
        return RedirectToPage(new { Date, Ok = r.Created ? "new" : "changed", r.Status });
    }

    public string OkMessage => $"{Date} 퀴즈를 {(Ok == "new" ? "새로 넣었어요" : "바꿨어요")} · {(Status == ContentStatus.Published ? "공개" : "초안")}";

    private string Today() => TenantClock.ToTenantDate(time.GetUtcNow(), "Asia/Seoul").ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private PageResult Fail(params string[] errors) { Errors = errors; return Page(); }
}
