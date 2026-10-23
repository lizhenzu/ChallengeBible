namespace ChallengeBible.Domain.Rules;

/// <summary>
/// 점수 · 수행 규칙 값. 코드에 값을 쓰지 않고 이 객체에서 읽는다(CLAUDE.md 원칙).
/// 1차는 플랫폼 기본 규칙 하나를 모든 영역이 쓴다.
/// </summary>
public sealed record RuleSet
{
    public required int Version { get; init; }
    public required DayOfWeek WeekStart { get; init; }
    /// <summary>인정 기간: 오늘 포함 최근 N일(R-10). 1 ~ 7.</summary>
    public required int GraceDays { get; init; }
    public required int QuizMaxAttempts { get; init; }
    public required IReadOnlyList<MissionItemRule> Items { get; init; }
    /// <summary>개인 축하 단계(R-100), 오름차순.</summary>
    public required IReadOnlyList<int> Milestones { get; init; }
    public required IReadOnlyList<FamilyMilestoneRule> FamilyMilestones { get; init; }
    public required FeatureSwitches Features { get; init; }

    /// <summary>목표 점수 = 가장 높은 축하 단계(R-82).</summary>
    public int TargetPoints => Milestones.Count == 0 ? 0 : Milestones.Max();

    public MissionItemRule Item(MissionItem item) =>
        Items.FirstOrDefault(i => i.Item == item) ?? throw new KeyNotFoundException($"규칙에 항목 {item} 이 없어요.");

    public IEnumerable<string> Validate()
    {
        if (GraceDays is < 1 or > 7) yield return "인정 기간은 1 ~ 7일이에요.";
        if (QuizMaxAttempts < 1) yield return "퀴즈 도전 횟수는 1 이상이에요.";
        foreach (var mission in Enum.GetValues<Mission>())
        {
            if (!Items.Any(i => i.Item.Mission() == mission && i.Enabled))
                yield return $"{mission} 에 켜진 항목이 하나도 없어요(ADR 0032).";
        }
        if (Items.Any(i => i.Points < 0)) yield return "점수는 0 이상이에요.";
        if (Milestones.Count == 0) yield return "축하 단계는 하나 이상 있어야 해요.";
    }

    /// <summary>플랫폼 기본값(03 문서 11절 기본값 표).</summary>
    public static RuleSet Default { get; } = new()
    {
        Version = 1,
        WeekStart = DayOfWeek.Sunday,
        GraceDays = 3,
        QuizMaxAttempts = 2,
        Items =
        [
            new(MissionItem.WordMe, true, "혼자 읽었어요", 1),
            new(MissionItem.WordParent, true, "부모님께 읽어드렸어요", 1),
            new(MissionItem.PrayerMe, true, "나를 위해 기도했어요", 1),
            new(MissionItem.PrayerOthers, true, "가족·친구를 위해 기도했어요", 1),
            new(MissionItem.QuizCorrect, true, "정답", 1),
            new(MissionItem.Recite, true, "이번 주 암송", 1),
        ],
        Milestones = [3000, 5000, 8000],
        FamilyMilestones =
        [
            new(1000, FamilyMilestoneKind.Award, "첫 1,000점! 가족 선물이 있어요", "🎁", true),
            new(2000, FamilyMilestoneKind.Applause, "2,000점 돌파, 박수!", "👏", true),
            new(3000, FamilyMilestoneKind.Award, "3,000점! 가족 선물이 있어요", "🎁", true),
            new(4000, FamilyMilestoneKind.Applause, "4,000점 돌파, 박수!", "👏", true),
            new(5000, FamilyMilestoneKind.Award, "5,000점! 최고의 가족", "🏆", true),
        ],
        Features = new(Ranking: true, FamilyTeam: true, TranslationCompare: true),
    };
}

public sealed record MissionItemRule(MissionItem Item, bool Enabled, string DisplayName, int Points);

public sealed record FamilyMilestoneRule(int Points, FamilyMilestoneKind Kind, string Message, string Emoji, bool Enabled);

public enum FamilyMilestoneKind { Award, Applause }

public sealed record FeatureSwitches(bool Ranking, bool FamilyTeam, bool TranslationCompare);
