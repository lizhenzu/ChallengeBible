using ChallengeBible.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChallengeBible.Infrastructure.Data;

internal sealed class BibleBookConfig : IEntityTypeConfiguration<BibleBook>
{
    public void Configure(EntityTypeBuilder<BibleBook> e)
    {
        e.ToTable("BibleBook", t =>
        {
            t.HasCheckConstraint("CK_BibleBook_Id", "Id BETWEEN 1 AND 66");
            t.HasCheckConstraint("CK_BibleBook_Testament", "Testament IN ('OT','NT')");
            t.HasCheckConstraint("CK_BibleBook_ChapterCount", "ChapterCount >= 1");
        });
        e.HasKey(x => x.Id).HasName("PK_BibleBook");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Name).HasMaxLength(30);
        e.Property(x => x.ShortName).HasMaxLength(10);
        e.Property(x => x.Testament).HasMaxLength(2).IsFixedLength().IsUnicode(false);
        e.HasData(BibleBooks.All());
    }
}

internal static class ContentConfig
{
    public static void Base<T>(EntityTypeBuilder<T> e, string table) where T : ContentBase
    {
        e.HasKey(x => x.Id).HasName($"PK_{table}");
        e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
        e.Property(x => x.CreatedUtc).HasColumnType("datetime2(3)");
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).HasConstraintName($"FK_{table}_Tenant").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<T>().WithMany().HasForeignKey(x => x.ReplacesId).HasConstraintName($"FK_{table}_Replaces").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.CreatedByAccountId).HasConstraintName($"FK_{table}_CreatedBy").OnDelete(DeleteBehavior.NoAction);
    }

    public static void Range<T>(EntityTypeBuilder<T> e, string table) where T : RangedContent =>
        e.HasOne<BibleBook>().WithMany().HasForeignKey(x => x.BookId).HasConstraintName($"FK_{table}_Book").OnDelete(DeleteBehavior.NoAction);

    public static string StatusCheck => $"Status IN ({string.Join(",", ContentStatus.All.Select(s => $"'{s}'"))})";
    public const string RangeCheck = "EndChapter > StartChapter OR (EndChapter = StartChapter AND EndVerse >= StartVerse)";
}

internal sealed class DailyWordConfig : IEntityTypeConfiguration<DailyWord>
{
    public void Configure(EntityTypeBuilder<DailyWord> e)
    {
        e.ToTable("DailyWord", t =>
        {
            t.HasCheckConstraint("CK_DailyWord_Status", ContentConfig.StatusCheck);
            t.HasCheckConstraint("CK_DailyWord_Range", ContentConfig.RangeCheck);
        });
        ContentConfig.Base(e, "DailyWord");
        ContentConfig.Range(e, "DailyWord");
        e.Property(x => x.Title).HasMaxLength(100);
        e.HasIndex(x => new { x.TenantId, x.ExposeDate }).IsUnique().HasFilter("[IsCurrent] = 1").HasDatabaseName("UX_DailyWord_Date");
    }
}

internal sealed class DailyPrayerConfig : IEntityTypeConfiguration<DailyPrayer>
{
    public void Configure(EntityTypeBuilder<DailyPrayer> e)
    {
        e.ToTable("DailyPrayer", t =>
        {
            t.HasCheckConstraint("CK_DailyPrayer_Status", ContentConfig.StatusCheck);
            t.HasCheckConstraint("CK_DailyPrayer_Range", ContentConfig.RangeCheck);
            t.HasCheckConstraint("CK_DailyPrayer_Paragraphs", "ISJSON(Paragraphs) = 1");
            // 0053: 공용은 날짜 번호만, 부서는 날짜만
            t.HasCheckConstraint("CK_DailyPrayer_Slot", $"""
                (TenantId IS NULL AND DayNo BETWEEN 1 AND {DailyPrayer.MaxDayNo} AND ExposeDate IS NULL)
                OR (TenantId IS NOT NULL AND DayNo IS NULL AND ExposeDate IS NOT NULL)
                """.ReplaceLineEndings("\n"));
        });
        ContentConfig.Base(e, "DailyPrayer");
        ContentConfig.Range(e, "DailyPrayer");
        e.Property(x => x.TraitName).HasMaxLength(30);
        e.Property(x => x.TraitNameEn).HasMaxLength(40);
        e.HasIndex(x => x.DayNo).IsUnique().HasFilter("[IsCurrent] = 1 AND [TenantId] IS NULL").HasDatabaseName("UX_DailyPrayer_Day");
        e.HasIndex(x => new { x.TenantId, x.ExposeDate }).IsUnique().HasFilter("[IsCurrent] = 1 AND [ExposeDate] IS NOT NULL").HasDatabaseName("UX_DailyPrayer_Date");
    }
}

internal sealed class DailyQuizConfig : IEntityTypeConfiguration<DailyQuiz>
{
    public void Configure(EntityTypeBuilder<DailyQuiz> e)
    {
        e.ToTable("DailyQuiz", t =>
        {
            t.HasCheckConstraint("CK_DailyQuiz_Status", ContentConfig.StatusCheck);
            t.HasCheckConstraint("CK_DailyQuiz_Format", "Format IN ('Choice','Text')");
            t.HasCheckConstraint("CK_DailyQuiz_Correct", "Status <> 'Published' OR Format = 'Text' OR CorrectChoiceId IS NOT NULL");
        });
        ContentConfig.Base(e, "DailyQuiz");
        e.Property(x => x.Question).HasMaxLength(1000);
        e.Property(x => x.Format).HasMaxLength(10).IsUnicode(false);
        e.HasIndex(x => new { x.TenantId, x.ExposeDate }).IsUnique().HasFilter("[IsCurrent] = 1").HasDatabaseName("UX_DailyQuiz_Date");
        e.HasMany(x => x.Choices).WithOne().HasForeignKey(c => c.DailyQuizId).HasConstraintName("FK_DailyQuizChoice_Quiz").OnDelete(DeleteBehavior.NoAction);
        e.HasMany(x => x.AcceptedAnswers).WithOne().HasForeignKey(c => c.DailyQuizId).HasConstraintName("FK_DailyQuizAcceptedAnswer_Quiz").OnDelete(DeleteBehavior.NoAction);
        // 정답 보기는 그 퀴즈의 보기(복합 FK). 순환 참조라 마이그레이션 SQL 로 따로 건다.
    }
}

internal sealed class DailyQuizChoiceConfig : IEntityTypeConfiguration<DailyQuizChoice>
{
    public void Configure(EntityTypeBuilder<DailyQuizChoice> e)
    {
        e.ToTable("DailyQuizChoice");
        e.HasKey(x => x.Id).HasName("PK_DailyQuizChoice");
        e.Property(x => x.Text).HasMaxLength(300);
        e.HasIndex(x => new { x.DailyQuizId, x.SortOrder }).IsUnique().HasDatabaseName("UQ_DailyQuizChoice");
        e.HasAlternateKey(x => new { x.Id, x.DailyQuizId }).HasName("UQ_DailyQuizChoice_Id_Quiz");
    }
}

internal sealed class DailyQuizAcceptedAnswerConfig : IEntityTypeConfiguration<DailyQuizAcceptedAnswer>
{
    public void Configure(EntityTypeBuilder<DailyQuizAcceptedAnswer> e)
    {
        e.ToTable("DailyQuizAcceptedAnswer");
        e.HasKey(x => new { x.DailyQuizId, x.NormalizedText }).HasName("PK_DailyQuizAcceptedAnswer");
        e.Property(x => x.NormalizedText).HasMaxLength(200);
    }
}

internal sealed class WeeklyVerseConfig : IEntityTypeConfiguration<WeeklyVerse>
{
    public void Configure(EntityTypeBuilder<WeeklyVerse> e)
    {
        e.ToTable("WeeklyVerse", t =>
        {
            t.HasCheckConstraint("CK_WeeklyVerse_Status", ContentConfig.StatusCheck);
            t.HasCheckConstraint("CK_WeeklyVerse_Range", ContentConfig.RangeCheck);
            // 0052: 공용은 주 번호만, 부서는 일요일(1900-01-07 기준, DATEFIRST 무관) 시작 7일만
            t.HasCheckConstraint("CK_WeeklyVerse_Slot", $"""
                (TenantId IS NULL AND WeekNo BETWEEN 1 AND {WeeklyVerse.MaxWeekNo} AND StartDate IS NULL AND EndDate IS NULL)
                OR (TenantId IS NOT NULL AND WeekNo IS NULL AND DATEDIFF(DAY, '19000107', StartDate) % 7 = 0 AND EndDate = DATEADD(DAY, 6, StartDate))
                """.ReplaceLineEndings("\n"));
            t.HasTrigger("trg_WeeklyVerse_NoOverlap");
        });
        ContentConfig.Base(e, "WeeklyVerse");
        ContentConfig.Range(e, "WeeklyVerse");
        e.Property(x => x.Title).HasMaxLength(200);
        e.Property(x => x.LessonSummary).HasMaxLength(200);
        e.HasIndex(x => x.WeekNo).IsUnique().HasFilter("[IsCurrent] = 1 AND [TenantId] IS NULL").HasDatabaseName("UX_WeeklyVerse_Week");
        e.HasIndex(x => new { x.TenantId, x.StartDate }).IsUnique().HasFilter("[IsCurrent] = 1 AND [StartDate] IS NOT NULL").HasDatabaseName("UX_WeeklyVerse_Start");
    }
}

internal sealed class ParticipantConfig : IEntityTypeConfiguration<Participant>
{
    public void Configure(EntityTypeBuilder<Participant> e)
    {
        e.ToTable("Participant");
        e.HasKey(x => x.Id).HasName("PK_Participant").IsClustered(false);
        e.HasAlternateKey(x => new { x.TenantId, x.Id }).HasName("UQ_Participant_TenantId_Id").IsClustered();
        e.HasIndex(x => new { x.TenantId, x.MembershipId }).IsUnique().HasDatabaseName("UQ_Participant_Membership");
        e.HasOne<Membership>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.MembershipId }).HasPrincipalKey(m => new { m.TenantId, m.Id })
            .HasConstraintName("FK_Participant_Membership").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class GuardianLinkConfig : IEntityTypeConfiguration<GuardianLink>
{
    public void Configure(EntityTypeBuilder<GuardianLink> e)
    {
        e.ToTable("GuardianLink");
        e.HasKey(x => x.Id).HasName("PK_GuardianLink").IsClustered(false);
        e.HasAlternateKey(x => new { x.TenantId, x.Id }).HasName("UQ_GuardianLink_TenantId_Id").IsClustered();
        e.HasIndex(x => new { x.TenantId, x.GuardianMembershipId, x.ParticipantId }).IsUnique().HasDatabaseName("UQ_GuardianLink_Pair");
        e.HasIndex(x => new { x.TenantId, x.ParticipantId }).HasDatabaseName("IX_GuardianLink_Participant");
        e.Property(x => x.CreatedUtc).HasColumnType("datetime2(3)");
        e.HasOne<Membership>().WithMany().HasForeignKey(x => new { x.TenantId, x.GuardianMembershipId }).HasPrincipalKey(m => new { m.TenantId, m.Id })
            .HasConstraintName("FK_GuardianLink_Guardian").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Participant>().WithMany().HasForeignKey(x => new { x.TenantId, x.ParticipantId }).HasPrincipalKey(p => new { p.TenantId, p.Id })
            .HasConstraintName("FK_GuardianLink_Participant").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.CreatedByAccountId).HasConstraintName("FK_GuardianLink_CreatedBy").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class ActivityConfig : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> e)
    {
        var items = string.Join(",", Enum.GetValues<MissionItem>().Select(i => $"'{i.Code()}'"));
        e.ToTable("Activity", t =>
        {
            t.HasCheckConstraint("CK_Activity_Item", $"MissionItem IN ({items})");
            t.HasCheckConstraint("CK_Activity_Points", "Points >= 0");
            t.HasCheckConstraint("CK_Activity_Content", """
                (MissionItem IN ('Word.Me','Word.Parent') AND DailyWordId IS NOT NULL AND DailyPrayerId IS NULL AND DailyQuizId IS NULL AND WeeklyVerseId IS NULL AND ReciteStartDate IS NULL)
                OR (MissionItem IN ('Prayer.Me','Prayer.Others') AND DailyWordId IS NULL AND DailyPrayerId IS NOT NULL AND DailyQuizId IS NULL AND WeeklyVerseId IS NULL AND ReciteStartDate IS NULL)
                OR (MissionItem = 'Quiz.Correct' AND DailyWordId IS NULL AND DailyPrayerId IS NULL AND DailyQuizId IS NOT NULL AND WeeklyVerseId IS NULL AND ReciteStartDate IS NULL)
                OR (MissionItem = 'Recite' AND DailyWordId IS NULL AND DailyPrayerId IS NULL AND DailyQuizId IS NULL AND WeeklyVerseId IS NOT NULL AND ReciteStartDate IS NOT NULL)
                """.ReplaceLineEndings("\n"));   // 체크아웃 줄바꿈(CRLF)과 무관하게 모델이 같도록
        });
        e.HasKey(x => x.Id).HasName("PK_Activity").IsClustered(false);
        e.HasAlternateKey(x => new { x.TenantId, x.Id }).HasName("UQ_Activity_TenantId_Id");
        e.HasIndex(x => new { x.TenantId, x.ParticipantId, x.MissionDate }).HasDatabaseName("CX_Activity").IsClustered();
        e.HasIndex(x => new { x.TenantId, x.ParticipantId, x.MissionDate, x.MissionItem }).IsUnique()
            .HasFilter("[MissionItem] <> 'Recite'").HasDatabaseName("UX_Activity_Daily");   // R-61
        e.HasIndex(x => new { x.TenantId, x.ParticipantId, x.ReciteStartDate }).IsUnique()
            .HasFilter("[MissionItem] = 'Recite'").HasDatabaseName("UX_Activity_Recite");   // R-51
        e.Property(x => x.MissionItem).HasMaxLength(20).IsUnicode(false);
        e.Property(x => x.PerformedUtc).HasColumnType("datetime2(3)");
        e.HasOne<Participant>().WithMany().HasForeignKey(x => new { x.TenantId, x.ParticipantId }).HasPrincipalKey(p => new { p.TenantId, p.Id })
            .HasConstraintName("FK_Activity_Participant").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<DailyWord>().WithMany().HasForeignKey(x => x.DailyWordId).HasConstraintName("FK_Activity_Word").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<DailyPrayer>().WithMany().HasForeignKey(x => x.DailyPrayerId).HasConstraintName("FK_Activity_Prayer").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<DailyQuiz>().WithMany().HasForeignKey(x => x.DailyQuizId).HasConstraintName("FK_Activity_Quiz").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<WeeklyVerse>().WithMany().HasForeignKey(x => x.WeeklyVerseId).HasConstraintName("FK_Activity_Verse").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class QuizAttemptConfig : IEntityTypeConfiguration<QuizAttempt>
{
    public void Configure(EntityTypeBuilder<QuizAttempt> e)
    {
        e.ToTable("QuizAttempt", t =>
        {
            t.HasCheckConstraint("CK_QuizAttempt_No", "AttemptNo >= 1");
            t.HasCheckConstraint("CK_QuizAttempt_Answer", "SelectedChoiceId IS NOT NULL OR AnswerText IS NOT NULL");
        });
        e.HasKey(x => x.Id).HasName("PK_QuizAttempt").IsClustered(false);
        e.HasAlternateKey(x => new { x.TenantId, x.ParticipantId, x.MissionDate, x.AttemptNo }).HasName("UQ_QuizAttempt_No").IsClustered();
        e.HasAlternateKey(x => new { x.TenantId, x.Id }).HasName("UQ_QuizAttempt_TenantId_Id");
        e.Property(x => x.AnswerText).HasMaxLength(200);
        e.Property(x => x.AttemptedUtc).HasColumnType("datetime2(3)");
        e.HasOne<Participant>().WithMany().HasForeignKey(x => new { x.TenantId, x.ParticipantId }).HasPrincipalKey(p => new { p.TenantId, p.Id })
            .HasConstraintName("FK_QuizAttempt_Participant").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<DailyQuiz>().WithMany().HasForeignKey(x => x.DailyQuizId).HasConstraintName("FK_QuizAttempt_Quiz").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<DailyQuizChoice>().WithMany().HasForeignKey(x => x.SelectedChoiceId).HasConstraintName("FK_QuizAttempt_Choice").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class PointLedgerConfig : IEntityTypeConfiguration<PointLedger>
{
    public void Configure(EntityTypeBuilder<PointLedger> e)
    {
        e.ToTable("PointLedger", t => t.HasCheckConstraint("CK_PointLedger_Points", "Points > 0"));
        e.HasKey(x => x.Id).HasName("PK_PointLedger").IsClustered(false);
        e.HasAlternateKey(x => new { x.TenantId, x.Id }).HasName("UQ_PointLedger_TenantId_Id");
        e.HasIndex(x => new { x.TenantId, x.ActivityId }).IsUnique().HasDatabaseName("UQ_PointLedger_Activity");
        e.HasIndex(x => new { x.TenantId, x.ParticipantId, x.MissionDate }).HasDatabaseName("CX_PointLedger").IsClustered();
        e.Property(x => x.MissionItem).HasMaxLength(20).IsUnicode(false);
        e.Property(x => x.RecordedUtc).HasColumnType("datetime2(3)");
        e.HasOne<Participant>().WithMany().HasForeignKey(x => new { x.TenantId, x.ParticipantId }).HasPrincipalKey(p => new { p.TenantId, p.Id })
            .HasConstraintName("FK_PointLedger_Participant").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Activity>().WithMany().HasForeignKey(x => new { x.TenantId, x.ActivityId }).HasPrincipalKey(a => new { a.TenantId, a.Id })
            .HasConstraintName("FK_PointLedger_Activity").OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class MilestoneReachedConfig : IEntityTypeConfiguration<MilestoneReached>
{
    public void Configure(EntityTypeBuilder<MilestoneReached> e)
    {
        e.ToTable("MilestoneReached");
        e.HasKey(x => x.Id).HasName("PK_MilestoneReached").IsClustered(false);
        e.HasAlternateKey(x => new { x.TenantId, x.Id }).HasName("UQ_MilestoneReached_TenantId_Id");
        e.HasIndex(x => new { x.TenantId, x.ParticipantId, x.Year, x.Threshold }).IsUnique().IsClustered().HasDatabaseName("UQ_MilestoneReached");
        e.Property(x => x.ReachedUtc).HasColumnType("datetime2(3)");
        e.HasOne<Participant>().WithMany().HasForeignKey(x => new { x.TenantId, x.ParticipantId }).HasPrincipalKey(p => new { p.TenantId, p.Id })
            .HasConstraintName("FK_MilestoneReached_Participant").OnDelete(DeleteBehavior.NoAction);
    }
}
