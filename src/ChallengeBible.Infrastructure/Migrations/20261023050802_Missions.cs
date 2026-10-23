using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Missions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BibleBook",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<byte>(type: "tinyint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ShortName = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Testament = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: false),
                    ChapterCount = table.Column<short>(type: "smallint", nullable: false),
                    Intro = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BibleBook", x => x.Id);
                    table.CheckConstraint("CK_BibleBook_ChapterCount", "ChapterCount >= 1");
                    table.CheckConstraint("CK_BibleBook_Id", "Id BETWEEN 1 AND 66");
                    table.CheckConstraint("CK_BibleBook_Testament", "Testament IN ('OT','NT')");
                });

            migrationBuilder.CreateTable(
                name: "DailyQuiz",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExposeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Question = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Background = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Format = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    CorrectChoiceId = table.Column<int>(type: "int", nullable: true),
                    Explanation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ReplacesId = table.Column<int>(type: "int", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedByAccountId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyQuiz", x => x.Id);
                    table.CheckConstraint("CK_DailyQuiz_Correct", "Status <> 'Published' OR Format = 'Text' OR CorrectChoiceId IS NOT NULL");
                    table.CheckConstraint("CK_DailyQuiz_Format", "Format IN ('Choice','Text')");
                    table.CheckConstraint("CK_DailyQuiz_Status", "Status IN ('Draft','Published','Archived')");
                    table.ForeignKey(
                        name: "FK_DailyQuiz_CreatedBy",
                        column: x => x.CreatedByAccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DailyQuiz_Replaces",
                        column: x => x.ReplacesId,
                        principalSchema: "app",
                        principalTable: "DailyQuiz",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DailyQuiz_Tenant",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Participant",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    MembershipId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Participant", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.UniqueConstraint("UQ_Participant_TenantId_Id", x => new { x.TenantId, x.Id })
                        .Annotation("SqlServer:Clustered", true);
                    table.ForeignKey(
                        name: "FK_Participant_Membership",
                        columns: x => new { x.TenantId, x.MembershipId },
                        principalSchema: "app",
                        principalTable: "Membership",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "DailyPrayer",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExposeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TraitName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TraitNameEn = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Paragraphs = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ReplacesId = table.Column<int>(type: "int", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedByAccountId = table.Column<int>(type: "int", nullable: false),
                    BookId = table.Column<byte>(type: "tinyint", nullable: false),
                    StartChapter = table.Column<short>(type: "smallint", nullable: false),
                    StartVerse = table.Column<short>(type: "smallint", nullable: false),
                    EndChapter = table.Column<short>(type: "smallint", nullable: false),
                    EndVerse = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyPrayer", x => x.Id);
                    table.CheckConstraint("CK_DailyPrayer_Paragraphs", "ISJSON(Paragraphs) = 1");
                    table.CheckConstraint("CK_DailyPrayer_Range", "EndChapter > StartChapter OR (EndChapter = StartChapter AND EndVerse >= StartVerse)");
                    table.CheckConstraint("CK_DailyPrayer_Status", "Status IN ('Draft','Published','Archived')");
                    table.ForeignKey(
                        name: "FK_DailyPrayer_Book",
                        column: x => x.BookId,
                        principalSchema: "app",
                        principalTable: "BibleBook",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DailyPrayer_CreatedBy",
                        column: x => x.CreatedByAccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DailyPrayer_Replaces",
                        column: x => x.ReplacesId,
                        principalSchema: "app",
                        principalTable: "DailyPrayer",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DailyPrayer_Tenant",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DailyWord",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExposeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Commentary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ReplacesId = table.Column<int>(type: "int", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedByAccountId = table.Column<int>(type: "int", nullable: false),
                    BookId = table.Column<byte>(type: "tinyint", nullable: false),
                    StartChapter = table.Column<short>(type: "smallint", nullable: false),
                    StartVerse = table.Column<short>(type: "smallint", nullable: false),
                    EndChapter = table.Column<short>(type: "smallint", nullable: false),
                    EndVerse = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyWord", x => x.Id);
                    table.CheckConstraint("CK_DailyWord_Range", "EndChapter > StartChapter OR (EndChapter = StartChapter AND EndVerse >= StartVerse)");
                    table.CheckConstraint("CK_DailyWord_Status", "Status IN ('Draft','Published','Archived')");
                    table.ForeignKey(
                        name: "FK_DailyWord_Book",
                        column: x => x.BookId,
                        principalSchema: "app",
                        principalTable: "BibleBook",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DailyWord_CreatedBy",
                        column: x => x.CreatedByAccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DailyWord_Replaces",
                        column: x => x.ReplacesId,
                        principalSchema: "app",
                        principalTable: "DailyWord",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DailyWord_Tenant",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WeeklyVerse",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LessonNo = table.Column<short>(type: "smallint", nullable: true),
                    LessonSummary = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LessonPoint = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LessonApply = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ReplacesId = table.Column<int>(type: "int", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedByAccountId = table.Column<int>(type: "int", nullable: false),
                    BookId = table.Column<byte>(type: "tinyint", nullable: false),
                    StartChapter = table.Column<short>(type: "smallint", nullable: false),
                    StartVerse = table.Column<short>(type: "smallint", nullable: false),
                    EndChapter = table.Column<short>(type: "smallint", nullable: false),
                    EndVerse = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyVerse", x => x.Id);
                    table.CheckConstraint("CK_WeeklyVerse_Period", "EndDate >= StartDate");
                    table.CheckConstraint("CK_WeeklyVerse_Range", "EndChapter > StartChapter OR (EndChapter = StartChapter AND EndVerse >= StartVerse)");
                    table.CheckConstraint("CK_WeeklyVerse_Status", "Status IN ('Draft','Published','Archived')");
                    table.ForeignKey(
                        name: "FK_WeeklyVerse_Book",
                        column: x => x.BookId,
                        principalSchema: "app",
                        principalTable: "BibleBook",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WeeklyVerse_CreatedBy",
                        column: x => x.CreatedByAccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WeeklyVerse_Replaces",
                        column: x => x.ReplacesId,
                        principalSchema: "app",
                        principalTable: "WeeklyVerse",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WeeklyVerse_Tenant",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DailyQuizAcceptedAnswer",
                schema: "app",
                columns: table => new
                {
                    DailyQuizId = table.Column<int>(type: "int", nullable: false),
                    NormalizedText = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyQuizAcceptedAnswer", x => new { x.DailyQuizId, x.NormalizedText });
                    table.ForeignKey(
                        name: "FK_DailyQuizAcceptedAnswer_Quiz",
                        column: x => x.DailyQuizId,
                        principalSchema: "app",
                        principalTable: "DailyQuiz",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DailyQuizChoice",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    DailyQuizId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<byte>(type: "tinyint", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyQuizChoice", x => x.Id);
                    table.UniqueConstraint("UQ_DailyQuizChoice_Id_Quiz", x => new { x.Id, x.DailyQuizId });
                    table.ForeignKey(
                        name: "FK_DailyQuizChoice_Quiz",
                        column: x => x.DailyQuizId,
                        principalSchema: "app",
                        principalTable: "DailyQuiz",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MilestoneReached",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    ParticipantId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<short>(type: "smallint", nullable: false),
                    Threshold = table.Column<int>(type: "int", nullable: false),
                    ReachedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MilestoneReached", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.UniqueConstraint("UQ_MilestoneReached_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_MilestoneReached_Participant",
                        columns: x => new { x.TenantId, x.ParticipantId },
                        principalSchema: "app",
                        principalTable: "Participant",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "Activity",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    ParticipantId = table.Column<int>(type: "int", nullable: false),
                    MissionItem = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    MissionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DailyWordId = table.Column<int>(type: "int", nullable: true),
                    DailyPrayerId = table.Column<int>(type: "int", nullable: true),
                    DailyQuizId = table.Column<int>(type: "int", nullable: true),
                    WeeklyVerseId = table.Column<int>(type: "int", nullable: true),
                    ReciteStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Points = table.Column<int>(type: "int", nullable: false),
                    PerformedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Activity", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.UniqueConstraint("UQ_Activity_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Activity_Content", "(MissionItem IN ('Word.Me','Word.Parent') AND DailyWordId IS NOT NULL AND DailyPrayerId IS NULL AND DailyQuizId IS NULL AND WeeklyVerseId IS NULL AND ReciteStartDate IS NULL)\nOR (MissionItem IN ('Prayer.Me','Prayer.Others') AND DailyWordId IS NULL AND DailyPrayerId IS NOT NULL AND DailyQuizId IS NULL AND WeeklyVerseId IS NULL AND ReciteStartDate IS NULL)\nOR (MissionItem = 'Quiz.Correct' AND DailyWordId IS NULL AND DailyPrayerId IS NULL AND DailyQuizId IS NOT NULL AND WeeklyVerseId IS NULL AND ReciteStartDate IS NULL)\nOR (MissionItem = 'Recite' AND DailyWordId IS NULL AND DailyPrayerId IS NULL AND DailyQuizId IS NULL AND WeeklyVerseId IS NOT NULL AND ReciteStartDate IS NOT NULL)");
                    table.CheckConstraint("CK_Activity_Item", "MissionItem IN ('Word.Me','Word.Parent','Prayer.Me','Prayer.Others','Quiz.Correct','Recite')");
                    table.CheckConstraint("CK_Activity_Points", "Points >= 0");
                    table.ForeignKey(
                        name: "FK_Activity_Participant",
                        columns: x => new { x.TenantId, x.ParticipantId },
                        principalSchema: "app",
                        principalTable: "Participant",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_Activity_Prayer",
                        column: x => x.DailyPrayerId,
                        principalSchema: "app",
                        principalTable: "DailyPrayer",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Activity_Quiz",
                        column: x => x.DailyQuizId,
                        principalSchema: "app",
                        principalTable: "DailyQuiz",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Activity_Verse",
                        column: x => x.WeeklyVerseId,
                        principalSchema: "app",
                        principalTable: "WeeklyVerse",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Activity_Word",
                        column: x => x.DailyWordId,
                        principalSchema: "app",
                        principalTable: "DailyWord",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuizAttempt",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    ParticipantId = table.Column<int>(type: "int", nullable: false),
                    MissionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AttemptNo = table.Column<byte>(type: "tinyint", nullable: false),
                    DailyQuizId = table.Column<int>(type: "int", nullable: false),
                    SelectedChoiceId = table.Column<int>(type: "int", nullable: true),
                    AnswerText = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    AttemptedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizAttempt", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.UniqueConstraint("UQ_QuizAttempt_No", x => new { x.TenantId, x.ParticipantId, x.MissionDate, x.AttemptNo })
                        .Annotation("SqlServer:Clustered", true);
                    table.UniqueConstraint("UQ_QuizAttempt_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_QuizAttempt_Answer", "SelectedChoiceId IS NOT NULL OR AnswerText IS NOT NULL");
                    table.CheckConstraint("CK_QuizAttempt_No", "AttemptNo >= 1");
                    table.ForeignKey(
                        name: "FK_QuizAttempt_Choice",
                        column: x => x.SelectedChoiceId,
                        principalSchema: "app",
                        principalTable: "DailyQuizChoice",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuizAttempt_Participant",
                        columns: x => new { x.TenantId, x.ParticipantId },
                        principalSchema: "app",
                        principalTable: "Participant",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_QuizAttempt_Quiz",
                        column: x => x.DailyQuizId,
                        principalSchema: "app",
                        principalTable: "DailyQuiz",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PointLedger",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    ParticipantId = table.Column<int>(type: "int", nullable: false),
                    MissionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MissionItem = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    ActivityId = table.Column<long>(type: "bigint", nullable: false),
                    RecordedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PointLedger", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.UniqueConstraint("UQ_PointLedger_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_PointLedger_Points", "Points > 0");
                    table.ForeignKey(
                        name: "FK_PointLedger_Activity",
                        columns: x => new { x.TenantId, x.ActivityId },
                        principalSchema: "app",
                        principalTable: "Activity",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_PointLedger_Participant",
                        columns: x => new { x.TenantId, x.ParticipantId },
                        principalSchema: "app",
                        principalTable: "Participant",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.InsertData(
                schema: "app",
                table: "BibleBook",
                columns: new[] { "Id", "ChapterCount", "Intro", "Name", "ShortName", "Testament" },
                values: new object[,]
                {
                    { (byte)1, (short)50, null, "창세기", "창", "OT" },
                    { (byte)2, (short)40, null, "출애굽기", "출", "OT" },
                    { (byte)3, (short)27, null, "레위기", "레", "OT" },
                    { (byte)4, (short)36, null, "민수기", "민", "OT" },
                    { (byte)5, (short)34, null, "신명기", "신", "OT" },
                    { (byte)6, (short)24, null, "여호수아", "수", "OT" },
                    { (byte)7, (short)21, null, "사사기", "삿", "OT" },
                    { (byte)8, (short)4, null, "룻기", "룻", "OT" },
                    { (byte)9, (short)31, null, "사무엘상", "삼상", "OT" },
                    { (byte)10, (short)24, null, "사무엘하", "삼하", "OT" },
                    { (byte)11, (short)22, null, "열왕기상", "왕상", "OT" },
                    { (byte)12, (short)25, null, "열왕기하", "왕하", "OT" },
                    { (byte)13, (short)29, null, "역대상", "대상", "OT" },
                    { (byte)14, (short)36, null, "역대하", "대하", "OT" },
                    { (byte)15, (short)10, null, "에스라", "스", "OT" },
                    { (byte)16, (short)13, null, "느헤미야", "느", "OT" },
                    { (byte)17, (short)10, null, "에스더", "에", "OT" },
                    { (byte)18, (short)42, null, "욥기", "욥", "OT" },
                    { (byte)19, (short)150, null, "시편", "시", "OT" },
                    { (byte)20, (short)31, null, "잠언", "잠", "OT" },
                    { (byte)21, (short)12, null, "전도서", "전", "OT" },
                    { (byte)22, (short)8, null, "아가", "아", "OT" },
                    { (byte)23, (short)66, null, "이사야", "사", "OT" },
                    { (byte)24, (short)52, null, "예레미야", "렘", "OT" },
                    { (byte)25, (short)5, null, "예레미야애가", "애", "OT" },
                    { (byte)26, (short)48, null, "에스겔", "겔", "OT" },
                    { (byte)27, (short)12, null, "다니엘", "단", "OT" },
                    { (byte)28, (short)14, null, "호세아", "호", "OT" },
                    { (byte)29, (short)3, null, "요엘", "욜", "OT" },
                    { (byte)30, (short)9, null, "아모스", "암", "OT" },
                    { (byte)31, (short)1, null, "오바댜", "옵", "OT" },
                    { (byte)32, (short)4, null, "요나", "욘", "OT" },
                    { (byte)33, (short)7, null, "미가", "미", "OT" },
                    { (byte)34, (short)3, null, "나훔", "나", "OT" },
                    { (byte)35, (short)3, null, "하박국", "합", "OT" },
                    { (byte)36, (short)3, null, "스바냐", "습", "OT" },
                    { (byte)37, (short)2, null, "학개", "학", "OT" },
                    { (byte)38, (short)14, null, "스가랴", "슥", "OT" },
                    { (byte)39, (short)4, null, "말라기", "말", "OT" },
                    { (byte)40, (short)28, null, "마태복음", "마", "NT" },
                    { (byte)41, (short)16, null, "마가복음", "막", "NT" },
                    { (byte)42, (short)24, null, "누가복음", "눅", "NT" },
                    { (byte)43, (short)21, null, "요한복음", "요", "NT" },
                    { (byte)44, (short)28, null, "사도행전", "행", "NT" },
                    { (byte)45, (short)16, null, "로마서", "롬", "NT" },
                    { (byte)46, (short)16, null, "고린도전서", "고전", "NT" },
                    { (byte)47, (short)13, null, "고린도후서", "고후", "NT" },
                    { (byte)48, (short)6, null, "갈라디아서", "갈", "NT" },
                    { (byte)49, (short)6, null, "에베소서", "엡", "NT" },
                    { (byte)50, (short)4, null, "빌립보서", "빌", "NT" },
                    { (byte)51, (short)4, null, "골로새서", "골", "NT" },
                    { (byte)52, (short)5, null, "데살로니가전서", "살전", "NT" },
                    { (byte)53, (short)3, null, "데살로니가후서", "살후", "NT" },
                    { (byte)54, (short)6, null, "디모데전서", "딤전", "NT" },
                    { (byte)55, (short)4, null, "디모데후서", "딤후", "NT" },
                    { (byte)56, (short)3, null, "디도서", "딛", "NT" },
                    { (byte)57, (short)1, null, "빌레몬서", "몬", "NT" },
                    { (byte)58, (short)13, null, "히브리서", "히", "NT" },
                    { (byte)59, (short)5, null, "야고보서", "약", "NT" },
                    { (byte)60, (short)5, null, "베드로전서", "벧전", "NT" },
                    { (byte)61, (short)3, null, "베드로후서", "벧후", "NT" },
                    { (byte)62, (short)5, null, "요한일서", "요일", "NT" },
                    { (byte)63, (short)1, null, "요한이서", "요이", "NT" },
                    { (byte)64, (short)1, null, "요한삼서", "요삼", "NT" },
                    { (byte)65, (short)1, null, "유다서", "유", "NT" },
                    { (byte)66, (short)22, null, "요한계시록", "계", "NT" }
                });

            migrationBuilder.CreateIndex(
                name: "CX_Activity",
                schema: "app",
                table: "Activity",
                columns: new[] { "TenantId", "ParticipantId", "MissionDate" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_Activity_DailyPrayerId",
                schema: "app",
                table: "Activity",
                column: "DailyPrayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Activity_DailyQuizId",
                schema: "app",
                table: "Activity",
                column: "DailyQuizId");

            migrationBuilder.CreateIndex(
                name: "IX_Activity_DailyWordId",
                schema: "app",
                table: "Activity",
                column: "DailyWordId");

            migrationBuilder.CreateIndex(
                name: "IX_Activity_WeeklyVerseId",
                schema: "app",
                table: "Activity",
                column: "WeeklyVerseId");

            migrationBuilder.CreateIndex(
                name: "UX_Activity_Daily",
                schema: "app",
                table: "Activity",
                columns: new[] { "TenantId", "ParticipantId", "MissionDate", "MissionItem" },
                unique: true,
                filter: "[MissionItem] <> 'Recite'");

            migrationBuilder.CreateIndex(
                name: "UX_Activity_Recite",
                schema: "app",
                table: "Activity",
                columns: new[] { "TenantId", "ParticipantId", "ReciteStartDate" },
                unique: true,
                filter: "[MissionItem] = 'Recite'");

            migrationBuilder.CreateIndex(
                name: "IX_DailyPrayer_BookId",
                schema: "app",
                table: "DailyPrayer",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyPrayer_CreatedByAccountId",
                schema: "app",
                table: "DailyPrayer",
                column: "CreatedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyPrayer_ReplacesId",
                schema: "app",
                table: "DailyPrayer",
                column: "ReplacesId");

            migrationBuilder.CreateIndex(
                name: "UX_DailyPrayer_Date",
                schema: "app",
                table: "DailyPrayer",
                columns: new[] { "TenantId", "ExposeDate" },
                unique: true,
                filter: "[IsCurrent] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_DailyQuiz_CreatedByAccountId",
                schema: "app",
                table: "DailyQuiz",
                column: "CreatedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyQuiz_ReplacesId",
                schema: "app",
                table: "DailyQuiz",
                column: "ReplacesId");

            migrationBuilder.CreateIndex(
                name: "UX_DailyQuiz_Date",
                schema: "app",
                table: "DailyQuiz",
                columns: new[] { "TenantId", "ExposeDate" },
                unique: true,
                filter: "[IsCurrent] = 1");

            migrationBuilder.CreateIndex(
                name: "UQ_DailyQuizChoice",
                schema: "app",
                table: "DailyQuizChoice",
                columns: new[] { "DailyQuizId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyWord_BookId",
                schema: "app",
                table: "DailyWord",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyWord_CreatedByAccountId",
                schema: "app",
                table: "DailyWord",
                column: "CreatedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyWord_ReplacesId",
                schema: "app",
                table: "DailyWord",
                column: "ReplacesId");

            migrationBuilder.CreateIndex(
                name: "UX_DailyWord_Date",
                schema: "app",
                table: "DailyWord",
                columns: new[] { "TenantId", "ExposeDate" },
                unique: true,
                filter: "[IsCurrent] = 1");

            migrationBuilder.CreateIndex(
                name: "UQ_MilestoneReached",
                schema: "app",
                table: "MilestoneReached",
                columns: new[] { "TenantId", "ParticipantId", "Year", "Threshold" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UQ_Participant_Membership",
                schema: "app",
                table: "Participant",
                columns: new[] { "TenantId", "MembershipId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "CX_PointLedger",
                schema: "app",
                table: "PointLedger",
                columns: new[] { "TenantId", "ParticipantId", "MissionDate" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "UQ_PointLedger_Activity",
                schema: "app",
                table: "PointLedger",
                columns: new[] { "TenantId", "ActivityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempt_DailyQuizId",
                schema: "app",
                table: "QuizAttempt",
                column: "DailyQuizId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempt_SelectedChoiceId",
                schema: "app",
                table: "QuizAttempt",
                column: "SelectedChoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyVerse_BookId",
                schema: "app",
                table: "WeeklyVerse",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyVerse_CreatedByAccountId",
                schema: "app",
                table: "WeeklyVerse",
                column: "CreatedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyVerse_ReplacesId",
                schema: "app",
                table: "WeeklyVerse",
                column: "ReplacesId");

            migrationBuilder.CreateIndex(
                name: "UX_WeeklyVerse_Start",
                schema: "app",
                table: "WeeklyVerse",
                columns: new[] { "TenantId", "StartDate" },
                unique: true,
                filter: "[IsCurrent] = 1");

            // 정답 보기는 반드시 그 퀴즈의 보기(참조 DDL) — 순환 참조라 EF 모델 밖에서 건다
            migrationBuilder.Sql("""
                ALTER TABLE app.DailyQuiz ADD CONSTRAINT FK_DailyQuiz_CorrectChoice
                    FOREIGN KEY (CorrectChoiceId, Id) REFERENCES app.DailyQuizChoice (Id, DailyQuizId);
                """);

            // 같은 소유의 현재 개정끼리 암송 기간이 겹치면 거절(R-121)
            migrationBuilder.Sql("""
                CREATE TRIGGER app.trg_WeeklyVerse_NoOverlap ON app.WeeklyVerse
                AFTER INSERT, UPDATE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1 FROM inserted i
                        JOIN app.WeeklyVerse w
                          ON w.Id <> i.Id AND w.IsCurrent = 1 AND i.IsCurrent = 1
                         AND (w.TenantId = i.TenantId OR (w.TenantId IS NULL AND i.TenantId IS NULL))
                         AND w.StartDate <= i.EndDate AND i.StartDate <= w.EndDate)
                    BEGIN
                        THROW 50001, N'암송 기간이 겹칩니다.', 1;
                    END
                END;
                """);

            // 공용 콘텐츠: 읽기 = 공용 또는 내 영역, 쓰기 = 공용은 시스템 연결만(물리 설계 3-2)
            migrationBuilder.Sql("""
                CREATE FUNCTION sec.fn_SharedRead(@TenantId int)
                RETURNS TABLE WITH SCHEMABINDING AS
                RETURN SELECT 1 AS ok
                WHERE @TenantId IS NULL
                   OR @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
                   OR CAST(SESSION_CONTEXT(N'PlatformScope') AS int) = 1;
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION sec.fn_SharedWrite(@TenantId int)
                RETURNS TABLE WITH SCHEMABINDING AS
                RETURN SELECT 1 AS ok
                WHERE CAST(SESSION_CONTEXT(N'PlatformScope') AS int) = 1
                   OR (@TenantId IS NOT NULL AND @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int));
                """);

            var shared = new[] { "DailyWord", "DailyPrayer", "DailyQuiz", "DailyQuizChoice", "DailyQuizAcceptedAnswer", "WeeklyVerse" };
            var owned = new[] { "Participant", "Activity", "QuizAttempt", "PointLedger", "MilestoneReached" };
            var adds = shared.SelectMany(t => new[]
                {
                    $"ADD FILTER PREDICATE sec.fn_SharedRead(TenantId) ON app.{t}",
                    $"ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.{t} AFTER INSERT",
                    $"ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.{t} AFTER UPDATE",
                    $"ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.{t} BEFORE DELETE",
                })
                .Concat(owned.SelectMany(t => new[]
                {
                    $"ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.{t}",
                    $"ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.{t} AFTER INSERT",
                    $"ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.{t} AFTER UPDATE",
                }));
            migrationBuilder.Sql($"ALTER SECURITY POLICY sec.TenantIsolation {string.Join(",\n    ", adds)};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            var tables = new[] { "DailyWord", "DailyPrayer", "DailyQuiz", "DailyQuizChoice", "DailyQuizAcceptedAnswer", "WeeklyVerse",
                                 "Participant", "Activity", "QuizAttempt", "PointLedger", "MilestoneReached" };
            migrationBuilder.Sql($"ALTER SECURITY POLICY sec.TenantIsolation {string.Join(", ", tables.SelectMany(t => new[]
            {
                $"DROP FILTER PREDICATE ON app.{t}", $"DROP BLOCK PREDICATE ON app.{t} AFTER INSERT", $"DROP BLOCK PREDICATE ON app.{t} AFTER UPDATE",
            }.Concat(t.StartsWith("Daily") || t == "WeeklyVerse" ? [$"DROP BLOCK PREDICATE ON app.{t} BEFORE DELETE"] : [])))};");
            migrationBuilder.Sql("DROP FUNCTION sec.fn_SharedWrite;");
            migrationBuilder.Sql("DROP FUNCTION sec.fn_SharedRead;");
            migrationBuilder.Sql("DROP TRIGGER app.trg_WeeklyVerse_NoOverlap;");
            migrationBuilder.Sql("ALTER TABLE app.DailyQuiz DROP CONSTRAINT FK_DailyQuiz_CorrectChoice;");

            migrationBuilder.DropTable(
                name: "DailyQuizAcceptedAnswer",
                schema: "app");

            migrationBuilder.DropTable(
                name: "MilestoneReached",
                schema: "app");

            migrationBuilder.DropTable(
                name: "PointLedger",
                schema: "app");

            migrationBuilder.DropTable(
                name: "QuizAttempt",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Activity",
                schema: "app");

            migrationBuilder.DropTable(
                name: "DailyQuizChoice",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Participant",
                schema: "app");

            migrationBuilder.DropTable(
                name: "DailyPrayer",
                schema: "app");

            migrationBuilder.DropTable(
                name: "WeeklyVerse",
                schema: "app");

            migrationBuilder.DropTable(
                name: "DailyWord",
                schema: "app");

            migrationBuilder.DropTable(
                name: "DailyQuiz",
                schema: "app");

            migrationBuilder.DropTable(
                name: "BibleBook",
                schema: "app");
        }
    }
}
