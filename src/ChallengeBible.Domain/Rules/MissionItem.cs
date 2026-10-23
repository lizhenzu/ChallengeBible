namespace ChallengeBible.Domain.Rules;

public enum Mission { Word, Prayer, Quiz, Recite }

/// <summary>미션 항목. DB 코드 값은 <see cref="MissionItemCodes"/> (예: "Word.Me").</summary>
public enum MissionItem { WordMe, WordParent, PrayerMe, PrayerOthers, QuizCorrect, Recite }

public static class MissionItemCodes
{
    private static readonly Dictionary<MissionItem, string> Codes = new()
    {
        [MissionItem.WordMe] = "Word.Me",
        [MissionItem.WordParent] = "Word.Parent",
        [MissionItem.PrayerMe] = "Prayer.Me",
        [MissionItem.PrayerOthers] = "Prayer.Others",
        [MissionItem.QuizCorrect] = "Quiz.Correct",
        [MissionItem.Recite] = "Recite",
    };

    public static string Code(this MissionItem item) => Codes[item];

    public static MissionItem Parse(string code) =>
        Codes.First(kv => kv.Value == code).Key;

    public static Mission Mission(this MissionItem item) => item switch
    {
        MissionItem.WordMe or MissionItem.WordParent => Rules.Mission.Word,
        MissionItem.PrayerMe or MissionItem.PrayerOthers => Rules.Mission.Prayer,
        MissionItem.QuizCorrect => Rules.Mission.Quiz,
        _ => Rules.Mission.Recite,
    };
}
