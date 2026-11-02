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
    /// <summary>성장 단계(R-190, ADR 0076): 씨앗 → 새싹 → 나무 → 숲. 각 단계가 끝나는 점수, 오름차순. 모든 부서 같음.
    /// 예전에 저장한 규칙에 없으면 이 기본값을 쓴다.</summary>
    public IReadOnlyList<GrowthStage> GrowthStages { get; init; } = DefaultGrowth;

    public static readonly IReadOnlyList<GrowthStage> DefaultGrowth =
        [new("씨앗", "🌰", 400), new("새싹", "🌱", 900), new("나무", "🌿", 1300), new("숲", "🌲", 1700)];

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
        if (GrowthStages.Count == 0 || GrowthStages.Zip(GrowthStages.Skip(1)).Any(p => p.First.Until >= p.Second.Until) || GrowthStages[0].Until <= 0)
            yield return "성장 단계는 하나 이상, 끝 점수가 0보다 크고 차례로 커야 해요.";
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
            new(1000, FamilyMilestoneKind.Award, "첫 1,000점! 가족이 함께 해냈어요", "🎉", true),
            new(2000, FamilyMilestoneKind.Applause, "2,000점 돌파, 박수!", "👏", true),
            new(3000, FamilyMilestoneKind.Award, "3,000점! 가족이 함께 해냈어요", "🎉", true),
            new(4000, FamilyMilestoneKind.Applause, "4,000점 돌파, 박수!", "👏", true),
            new(5000, FamilyMilestoneKind.Award, "5,000점! 최고의 가족", "🏆", true),
        ],
        Features = new(Ranking: true, FamilyTeam: true, TranslationCompare: true),
    };
}

public sealed record MissionItemRule(MissionItem Item, bool Enabled, string DisplayName, int Points);

public sealed record FamilyMilestoneRule(int Points, FamilyMilestoneKind Kind, string Message, string Emoji, bool Enabled);

public enum FamilyMilestoneKind { Award, Applause }

/// <summary>성장 단계 하나: 이름 · 그림 · 이 단계가 끝나는 점수(다음 단계 시작).</summary>
public sealed record GrowthStage(string Name, string Emoji, int Until);

public sealed record FeatureSwitches(bool Ranking, bool FamilyTeam, bool TranslationCompare);
