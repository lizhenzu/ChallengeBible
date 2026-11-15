using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

/// <summary>
/// 퀴즈 출제 AI 프롬프트 문구(플랫폼 표, 한 줄). 유년부 QuizAiPrompt 를 그대로 옮김(2026-11-15 사용자 "있는 그대로").
/// {{BibleRange}} · {{ExposeDate}} 자리에 그날 구절 · 날짜를 넣어 복사한다. 줄이 없으면 기본 문구.
/// </summary>
public sealed class QuizAiPrompt
{
    public int Id { get; set; }
    public required string PromptText { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public string? UpdatedByUserName { get; set; }
}

internal sealed class QuizAiPromptConfig : IEntityTypeConfiguration<QuizAiPrompt>
{
    public void Configure(EntityTypeBuilder<QuizAiPrompt> e)
    {
        e.ToTable("QuizAiPrompt");
        e.HasKey(x => x.Id);
        e.Property(x => x.UpdatedUtc).HasPrecision(3);
        e.Property(x => x.UpdatedByUserName).HasMaxLength(256);
    }
}
