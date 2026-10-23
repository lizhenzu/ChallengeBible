using System.Globalization;
using System.Text;

namespace ChallengeBible.Domain.Missions;

public enum QuizState { CanTry, Solved, OutOfAttempts }

public static class QuizPolicy
{
    /// <summary>R-41: 정답을 맞혔거나 시도 횟수를 다 쓰면 더 도전할 수 없다.</summary>
    public static QuizState State(int attemptsUsed, bool solved, int maxAttempts) =>
        solved ? QuizState.Solved : attemptsUsed >= maxAttempts ? QuizState.OutOfAttempts : QuizState.CanTry;

    /// <summary>R-43: 주관식 답 비교용 정규화 — 앞뒤 · 가운데 공백 제거, 대소문자 무시, 유니코드 정규화.</summary>
    public static string Normalize(string answer)
    {
        var s = answer.Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch)) continue;
            sb.Append(char.ToLower(ch, CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    public static bool IsCorrectText(string answer, IEnumerable<string> accepted) =>
        accepted.Any(a => Normalize(a) == Normalize(answer));
}
