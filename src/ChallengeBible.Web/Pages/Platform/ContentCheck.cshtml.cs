using System.Globalization;
using System.Text.Json;
using ChallengeBible.Domain.Time;
using ChallengeBible.Web.Auth;
using ChallengeBible.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ChallengeBible.Web.Pages.Platform;

/// <summary>
/// `/platform/content/{word|quiz}/check?month=&amp;date=` 말씀 · 퀴즈 입력 점검(확정 시안 word-check · quiz-check, R-216 · R-217).
/// 서버는 한 달치 자료만 주고 달력 · 칩 · 그날 칸은 content-check.js 가 그린다.
/// </summary>
[Authorize(Policy = CurrentUser.PlatformAdminPolicy)]
public class ContentCheckModel(ContentAdminService content, QuizAiPromptService prompts, WordToolsService tools, TimeProvider time) : PageModel, ITitled
{
    /// <summary>유년부처럼 "퀴즈 출제" · "말씀 입력"(2026-11-15 사용자, 확정 시안 퀴즈출제-말씀입력). 주소는 그대로.</summary>
    public string Heading => IsQuiz ? "퀴즈 출제" : "말씀 입력";
    public string Title => $"{Heading} · 플랫폼";
    [TempData] public string? Ok { get; set; }

    [BindProperty(SupportsGet = true)] public string Kind { get; set; } = "word";
    [BindProperty(SupportsGet = true)] public string? Month { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }

    public bool IsQuiz => Kind == "quiz";
    public string Noun => IsQuiz ? "퀴즈" : "말씀";
    public string DotOrder => IsQuiz ? "배경 · 문제 · 4지선다 · 힌트" : "범위 · 제목 · 이야기 · 영상";
    public DateOnly First { get; private set; }
    /// <summary>스크립트에 넘기는 자료. 기본 직렬화라 한글과 태그 글자가 \uXXXX 로 들어가 script 안에서 안전하다.</summary>
    public string DataJson { get; private set; } = "{}";

    public string MonthText(DateOnly m) => m.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    public string Link(DateOnly m) => $"/platform/content/{Kind}/check?month={MonthText(m)}";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Kind is not ("word" or "quiz")) return NotFound();
        var today = TenantClock.ToTenantDate(time.GetUtcNow().UtcDateTime, PlatformHomeService.PlatformTimeZone);
        First = ParseMonth(Month) ?? new DateOnly(today.Year, today.Month, 1);
        var last = First.AddMonths(1).AddDays(-1);
        var initial = Date is { } d && d >= First && d <= last ? d : today >= First && today <= last ? today : First;
        var me = User.UserName()!;
        object days = IsQuiz ? await content.QuizCheckMonthAsync(me, First, ct) : await content.WordCheckMonthAsync(me, First, ct);
        // 퀴즈 출제 그날 칸의 "오늘의 말씀" 카드 · [AI 프롬프트](유년부 그대로)
        var words = IsQuiz ? (await content.WordCheckMonthAsync(me, First, ct)).Where(w => w.Title is not null || w.Ref is not null)
            .ToDictionary(w => w.Date, w => new { w.Title, w.Ref }) : null;
        var prompt = IsQuiz ? (await prompts.GetAsync(me, ct)).Text : null;
        string[] items = IsQuiz
            ? [QuizCheck.BackgroundItem, QuizCheck.QuestionItem, QuizCheck.ChoiceItem, QuizCheck.HintItem]
            : [WordCheck.RangeItem, WordCheck.TitleItem, WordCheck.StoryItem, WordCheck.ClipItem];
        DataJson = JsonSerializer.Serialize(new
        {
            Kind, Noun, Items = items, Month = MonthText(First),
            Initial = initial.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Days = days, Words = words, Prompt = prompt,
            Msg = Ok switch { "deleted" => "퀴즈를 내렸어요. 아이들 화면에는 \"오늘은 없음\"으로 보여요.", _ => null },
        }, JsonSerializerOptions.Web);
        return Page();
    }

    /// <summary>[퀴즈 삭제](유년부 그대로): 그날 공용 퀴즈를 내린다(보관 — 아이 기록이 가리키므로 지우지 않는다).</summary>
    public async Task<IActionResult> OnPostDeleteAsync(DateOnly date, CancellationToken ct)
    {
        if (Kind != "quiz") return NotFound();
        if (await tools.ArchiveQuizAsync(User.UserName()!, date, ct)) Ok = "deleted";
        return Redirect($"/platform/content/quiz/check?month={date:yyyy-MM}&date={date:yyyy-MM-dd}");
    }

    /// <summary>"2026-11" 만 받는다. 형식이 틀리거나 지나치게 먼 해는 null(이번 달).</summary>
    public static DateOnly? ParseMonth(string? month) =>
        DateOnly.TryParseExact((month ?? "") + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) && f.Year is >= 2000 and <= 2100 ? f : null;
}
