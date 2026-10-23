namespace ChallengeBible.Infrastructure.Data;

// 성경 참조 · 콘텐츠 · 미션 · 점수 표. 이름 · 형식은 참조 DDL(물리-설계.sql)과 맞춘다.

/// <summary>성경 책(플랫폼 참조 데이터, 66권 — 마이그레이션 시드).</summary>
public sealed class BibleBook
{
    public byte Id { get; set; }
    public required string Name { get; set; }
    public required string ShortName { get; set; }
    public required string Testament { get; set; }   // OT · NT
    public short ChapterCount { get; set; }
    public string? Intro { get; set; }
}

/// <summary>공용(TenantId NULL) 또는 영역 덮어쓰기 콘텐츠. 읽기는 공용 + 내 영역, 쓰기는 소유자만(물리 설계 3-2).</summary>
public interface ISharedContent
{
    int? TenantId { get; }
}

public static class ContentStatus
{
    public const string Draft = "Draft";
    public const string Published = "Published";
    public const string Archived = "Archived";
    public static readonly string[] All = [Draft, Published, Archived];
}

/// <summary>콘텐츠 공통: 개정은 새 행, 같은 소유 · 날짜의 현재 개정은 1건(R-121, R-124).</summary>
public abstract class ContentBase : ISharedContent
{
    public int Id { get; set; }
    public int? TenantId { get; set; }
    public int Revision { get; set; } = 1;
    public bool IsCurrent { get; set; } = true;
    public string Status { get; set; } = ContentStatus.Draft;
    public int? ReplacesId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public int CreatedByAccountId { get; set; }
}

public abstract class RangedContent : ContentBase
{
    public byte BookId { get; set; }
    public short StartChapter { get; set; }
    public short StartVerse { get; set; }
    public short EndChapter { get; set; }
    public short EndVerse { get; set; }
}

public sealed class DailyWord : RangedContent
{
    public DateOnly ExposeDate { get; set; }
    public required string Title { get; set; }
    public required string Summary { get; set; }
    public string? Commentary { get; set; }
}

/// <summary>
/// 성품기도(0047): 성품 이름 · 근거 말씀 · {이름} 자리가 있는 문단들(JSON 배열).
/// 공용은 날짜 번호(DayNo 1 ~ 31)로 매달 반복하고 날짜가 없다. 부서 덮어쓰기는 그날(ExposeDate)에만(0053).
/// </summary>
public sealed class DailyPrayer : RangedContent
{
    public const byte MaxDayNo = 31;

    public byte? DayNo { get; set; }
    public DateOnly? ExposeDate { get; set; }
    public required string TraitName { get; set; }
    public string? TraitNameEn { get; set; }
    public required string Paragraphs { get; set; }
}

public static class QuizFormat
{
    public const string Choice = "Choice";
    public const string Text = "Text";
}

public sealed class DailyQuiz : ContentBase
{
    public DateOnly ExposeDate { get; set; }
    public required string Question { get; set; }
    public string? Background { get; set; }
    public string Format { get; set; } = QuizFormat.Choice;
    public int? CorrectChoiceId { get; set; }
    public string? Explanation { get; set; }
    public List<DailyQuizChoice> Choices { get; set; } = [];
    public List<DailyQuizAcceptedAnswer> AcceptedAnswers { get; set; } = [];
}

public sealed class DailyQuizChoice : ISharedContent
{
    public int Id { get; set; }
    public int? TenantId { get; set; }
    public int DailyQuizId { get; set; }
    public byte SortOrder { get; set; }
    public required string Text { get; set; }
}

public sealed class DailyQuizAcceptedAnswer : ISharedContent
{
    public int? TenantId { get; set; }
    public int DailyQuizId { get; set; }
    public required string NormalizedText { get; set; }
}

/// <summary>
/// 암송(0052). 공용은 주 번호(WeekNo 1 ~ 55)로 매년 반복하고 날짜가 없다.
/// 부서 덮어쓰기는 실제 주(StartDate 일요일 ~ EndDate)에만 쓰여 해가 바뀌면 공용으로 돌아간다.
/// </summary>
public sealed class WeeklyVerse : RangedContent
{
    public const byte MaxWeekNo = 55;

    public byte? WeekNo { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public required string Title { get; set; }
    public short? LessonNo { get; set; }
    public string? LessonSummary { get; set; }
    public string? LessonPoint { get; set; }
    public string? LessonApply { get; set; }
}

/// <summary>참여자: 소속에 참여자 역할이 있으면 하나(영역 범위). 점수 · 수행 기록의 주인.</summary>
public sealed class Participant : ITenantOwned
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int MembershipId { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>보호 관계: 보호자 소속 → 참여자(같은 영역). 보호자는 연결된 참여자의 기록만 본다(R-132).</summary>
public sealed class GuardianLink : ITenantOwned
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int GuardianMembershipId { get; set; }
    public int ParticipantId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public int CreatedByAccountId { get; set; }
}

/// <summary>수행 기록. 하루 · 항목당 1건(R-61), 암송은 그 주에 1건(R-51) — DB 고유 인덱스로 막는다.</summary>
public sealed class Activity : ITenantOwned
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int ParticipantId { get; set; }
    public required string MissionItem { get; set; }
    public DateOnly MissionDate { get; set; }
    public int? DailyWordId { get; set; }
    public int? DailyPrayerId { get; set; }
    public int? DailyQuizId { get; set; }
    public int? WeeklyVerseId { get; set; }
    public DateOnly? ReciteStartDate { get; set; }
    public int Points { get; set; }
    public DateTime PerformedUtc { get; set; }
}

public sealed class QuizAttempt : ITenantOwned
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int ParticipantId { get; set; }
    public DateOnly MissionDate { get; set; }
    public byte AttemptNo { get; set; }
    public int DailyQuizId { get; set; }
    public int? SelectedChoiceId { get; set; }
    public string? AnswerText { get; set; }
    public bool IsCorrect { get; set; }
    public DateTime AttemptedUtc { get; set; }
}

/// <summary>점수 원장 — 추가만 한다(R-63, R-64). 당시 점수를 기록해서 규칙이 바뀌어도 그대로.</summary>
public sealed class PointLedger : ITenantOwned
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int ParticipantId { get; set; }
    public DateOnly MissionDate { get; set; }
    public required string MissionItem { get; set; }
    public int Points { get; set; }
    public long ActivityId { get; set; }
    public DateTime RecordedUtc { get; set; }
}

/// <summary>개인 축하 단계 도달(R-101) — 시즌당 단계마다 한 번.</summary>
public sealed class MilestoneReached : ITenantOwned
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int ParticipantId { get; set; }
    public short Year { get; set; }
    public int Threshold { get; set; }
    public DateTime ReachedUtc { get; set; }
}
