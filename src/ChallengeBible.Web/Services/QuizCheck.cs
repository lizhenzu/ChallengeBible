using System.Globalization;
using ChallengeBible.Infrastructure.Data;

namespace ChallengeBible.Web.Services;

/// <summary>보기 한 줄.</summary>
public sealed record QuizCheckChoice(string Text, bool Correct);

/// <summary>퀴즈 입력 점검의 하루(R-217). 퀴즈가 없는 날은 목록에 없다.</summary>
public sealed record QuizCheckDay(int Id, string Date, string Status, string Format, string? Background, string? Question,
    IReadOnlyList<QuizCheckChoice> Choices, IReadOnlyList<string> Answers, string? Hint, IReadOnlyList<string> Missing);

/// <summary>퀴즈 한 줄의 입력 여부 판정(R-217, 확정 시안 quiz-check). 화면과 DB 를 모르는 순수 함수다.</summary>
public static class QuizCheck
{
    public const string BackgroundItem = "배경", QuestionItem = "문제", ChoiceItem = "4지선다", HintItem = "힌트";
    public const int ChoiceCount = 4;

    public static QuizCheckDay Day(DailyQuiz quiz)
    {
        var choices = quiz.Choices.OrderBy(c => c.SortOrder).ToList();
        var answers = quiz.AcceptedAnswers.Select(a => a.NormalizedText).Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        var answered = quiz.Format == QuizFormat.Text
            ? answers.Count > 0
            : choices.Count == ChoiceCount && choices.All(c => !string.IsNullOrWhiteSpace(c.Text)) && choices.Any(c => c.Id == quiz.CorrectChoiceId);

        var missing = new List<string>(4);
        if (string.IsNullOrWhiteSpace(quiz.Background)) missing.Add(BackgroundItem);
        if (string.IsNullOrWhiteSpace(quiz.Question)) missing.Add(QuestionItem);
        if (!answered) missing.Add(ChoiceItem);
        if (string.IsNullOrWhiteSpace(quiz.Explanation)) missing.Add(HintItem);

        return new QuizCheckDay(quiz.Id, quiz.ExposeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), quiz.Status, quiz.Format,
            Clean(quiz.Background), Clean(quiz.Question),
            choices.Select(c => new QuizCheckChoice(c.Text, c.Id == quiz.CorrectChoiceId)).ToList(), answers,
            Clean(quiz.Explanation), missing);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
