using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public sealed record QuizAiPromptInfo(string Text, bool IsDefault, DateTime? UpdatedUtc, string? UpdatedBy);

/// <summary>
/// 퀴즈 출제 AI 프롬프트(유년부 QuizAiPromptHelper · ManageQuizAiPrompt 를 그대로 옮김, 2026-11-15 사용자 "있는 그대로").
/// 플랫폼 관리자가 화면에서 고친다(사용자 질문 1 → 1). 기본 문구는 유년부 그대로 — 문구를 다듬는 것은 사람이 한다(사용자).
/// </summary>
public sealed class QuizAiPromptService(ISystemDb systemDb, TimeProvider time)
{
    public const string BibleRangeToken = "{{BibleRange}}";
    public const string ExposeDateToken = "{{ExposeDate}}";

    /// <summary>줄바꿈을 LF 로 맞추고 앞뒤 공백을 지운다(입력 칸은 CRLF 로 보낸다).</summary>
    public static string Normalize(string? template) => (template ?? "").Replace("\r\n", "\n").Trim();

    /// <summary>구절과 날짜를 채운다.</summary>
    public static string Build(string template, string bibleRange, string exposeDate) =>
        Normalize(template).Replace(BibleRangeToken, bibleRange).Replace(ExposeDateToken, exposeDate);

    /// <summary>문구에 빠진 자리 표시.</summary>
    public static List<string> MissingTokens(string? template)
    {
        var missing = new List<string>();
        if (template is null || !template.Contains(BibleRangeToken)) missing.Add(BibleRangeToken);
        if (template is null || !template.Contains(ExposeDateToken)) missing.Add(ExposeDateToken);
        return missing;
    }

    public async Task<QuizAiPromptInfo> GetAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        var row = await ctx.QuizAiPrompts.AsNoTracking().OrderBy(p => p.Id).FirstOrDefaultAsync(ct);
        return row is null || string.IsNullOrWhiteSpace(row.PromptText)
            ? new QuizAiPromptInfo(Normalize(DefaultTemplate), true, null, null)
            : new QuizAiPromptInfo(Normalize(row.PromptText), false, row.UpdatedUtc, row.UpdatedByUserName);
    }

    /// <summary>저장. 두 자리 표시가 모두 있어야 한다. 빈 칸이면 기본 문구로 되돌린다.</summary>
    public async Task<List<string>> SaveAsync(string actor, string? text, CancellationToken ct = default)
    {
        var t = Normalize(text);
        if (t.Length > 0 && MissingTokens(t) is { Count: > 0 } missing) return missing;
        await using var ctx = systemDb.Create(actor);
        if (!await AccountService.IsActivePlatformAdminAsync(ctx, actor, ct)) throw new UnauthorizedAccessException("플랫폼 관리자만 할 수 있어요.");
        var row = await ctx.QuizAiPrompts.OrderBy(p => p.Id).FirstOrDefaultAsync(ct);
        if (t.Length == 0) { if (row is not null) ctx.QuizAiPrompts.Remove(row); }
        else if (row is null) ctx.QuizAiPrompts.Add(new QuizAiPrompt { PromptText = t, UpdatedUtc = time.GetUtcNow().UtcDateTime, UpdatedByUserName = actor });
        else { row.PromptText = t; row.UpdatedUtc = time.GetUtcNow().UtcDateTime; row.UpdatedByUserName = actor; }
        await ctx.SaveChangesAsync(ct);
        return [];
    }

    /// <summary>유년부 기본 문구 그대로(Helpers/QuizAiPromptHelper.DefaultTemplate).</summary>
    public const string DefaultTemplate = """
너는 성경 교육 전문가이자 유년부 문제 출제자다.
아래 입력을 바탕으로 문제 5개와 각 문제의 SQL문(있으면 UPDATE, 없으면 INSERT)을 만들어라.

## 입력
- 성경 범위: {{BibleRange}}
- 출제 일자: {{ExposeDate}}

## 고정 조건
- 역본: 개역개정
- 대상: 유년부(초1~3)
- 용도: 공과 복습
- 문항 수: 5문항, 4지선다
- 5문항 모두 같은 날짜(출제 일자)를 ExposeDate로 쓴다

## 진행 순서
1. 본문 분석을 짧은 표로 출력한다: 장르, 흐름 구분(절 범위), 반복어·핵심어(절 번호), 하나님의 성품, 인물의 감정 변화, 암송 구절 후보 2~3개, 중심 교훈 한 문장
2. 본문 특성에 맞는 문제 유형을 골라 5문항에 배분하고, 배분표를 한 줄로 보여 준다
   - 후보: 반복어 찾기 / 간구·행동 내용 고르기(본문에 없는 것 찾기 포함) / 하나님의 성품 / 약속과 복 / 사건 순서 / 비유 표현 이해 / 빈칸 암송(1문항) / 상황 적용(1문항)
   - 문항 순서는 본문 흐름을 따른다(상황 적용은 마지막)
3. 확인 질문 없이 바로 출제한다

## 출제 규칙
- 모든 정답은 해당 본문에 근거하고 절 번호를 붙인다. 본문에 없는 추측은 쓰지 않는다
- 개역개정의 실제 표현을 쓴다. 기억에 의존해서 표현을 지어내지 않는다
- 오답 보기는 그럴듯하게 만든다(다른 성경 내용, 비슷한 표현). 단 정답은 하나만 되게 한다
- 정답 번호는 1~4번에 고르게 나눈다(같은 번호는 최대 2번)
- 유년부 수준에 맞게 쉬운 어휘와 짧은 문장을 쓰고, 성경 구절은 길게 인용하지 않는다
- 5문항의 질문(QuestionQ)은 서로 다른 문장이어야 한다

## 문제 출력 형식 (문제마다)
**N번 (유형)** — ExposeDate: YYYY-MM-DD
- **배경:** 3~4문장. 본문 상황, 인물의 마음, 문제와 관련된 내용을 아이 눈높이로 설명
- **문제:** 질문 (근거 구절 표시)
- ① ② ③ ④ 보기
- **힌트:** 정답을 직접 말하지 않고 떠올리게 하는 한 문장

이어서 바로 그 문제의 SQL을 출력한다:

```sql
DECLARE @ExposeDate    date          = 'YYYY-MM-DD';
DECLARE @UserName      nvarchar(100) = N'basic';
DECLARE @QuestionQ     nvarchar(max) = N'질문 (절 번호는 넣지 않음)';
DECLARE @Background    nvarchar(max) = N'배경 3~4문장';
DECLARE @Q1            nvarchar(500) = N'보기1';
DECLARE @Q2            nvarchar(500) = N'보기2';
DECLARE @Q3            nvarchar(500) = N'보기3';
DECLARE @Q4            nvarchar(500) = N'보기4';
DECLARE @CorrectAnswer nvarchar(10)  = N'정답번호(1~4)';
DECLARE @AnswerGuide   nvarchar(max) = N'힌트 문장. [책] [장]장 [절]절을 읽어 보세요.';

IF EXISTS (SELECT 1 FROM [dbo].[QuestionData]
           WHERE ExposeDate = @ExposeDate AND QuestionType = 'o' AND QuestionQ = @QuestionQ)
BEGIN
    UPDATE [dbo].[QuestionData]
       SET QuestionBackground = @Background,
           Question1     = @Q1,
           Question2     = @Q2,
           Question3     = @Q3,
           Question4     = @Q4,
           AnswerQ       = N'',
           CorrectAnswer = @CorrectAnswer,
           AnswerGuide   = @AnswerGuide,
           [UserName]    = @UserName
     WHERE ExposeDate = @ExposeDate AND QuestionType = 'o' AND QuestionQ = @QuestionQ;
END
ELSE
BEGIN
    INSERT INTO [dbo].[QuestionData]
        (ExposeDate, QuestionType, QuestionBackground, QuestionQ, Question1, Question2, Question3, Question4, AnswerQ, CorrectAnswer, AnswerGuide, [UserName])
    VALUES
        (@ExposeDate, 'o', @Background, @QuestionQ, @Q1, @Q2, @Q3, @Q4, N'', @CorrectAnswer, @AnswerGuide, @UserName);
END
GO
```

SQL 규칙:
- 문자열은 모두 N'' 유니코드로 쓴다. 문자열 안의 작은따옴표(')는 ''로 바꾼다
- QuestionType은 'o', AnswerQ는 빈 문자열로 둔다
- 기존 데이터 판단 기준: ExposeDate + QuestionType + QuestionQ가 같으면 UPDATE, 없으면 INSERT
- 5개 블록 모두 @ExposeDate는 같은 출제 일자로 쓴다
- 5개 블록을 GO로 나눠서 한 번에 실행할 수 있게 한다

## 마지막에 출력
- 정답표: 1-① 2-③ … (출제 일자 한 번 표시)
- 오늘의 한 줄 교훈
- 가정에서 나눌 질문 1개
""";
}
