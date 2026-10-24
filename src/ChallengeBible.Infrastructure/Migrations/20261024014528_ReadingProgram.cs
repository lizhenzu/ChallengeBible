using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReadingProgram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BibleChapter",
                schema: "app",
                columns: table => new
                {
                    BookId = table.Column<byte>(type: "tinyint", nullable: false),
                    Chapter = table.Column<short>(type: "smallint", nullable: false),
                    VerseCount = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BibleChapter", x => new { x.BookId, x.Chapter });
                    table.CheckConstraint("CK_BibleChapter_Verses", "VerseCount >= 1");
                    table.ForeignKey(
                        name: "FK_BibleChapter_Book",
                        column: x => x.BookId,
                        principalSchema: "app",
                        principalTable: "BibleBook",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReadingRound",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    RoundNo = table.Column<int>(type: "int", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ClosedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingRound", x => x.Id);
                    table.UniqueConstraint("UQ_ReadingRound", x => new { x.AccountId, x.RoundNo });
                    table.CheckConstraint("CK_ReadingRound_End", "CompletedUtc IS NULL OR ClosedUtc IS NULL");
                    table.CheckConstraint("CK_ReadingRound_No", "RoundNo >= 1");
                    table.ForeignKey(
                        name: "FK_ReadingRound_Account",
                        column: x => x.AccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ChapterProgress",
                schema: "app",
                columns: table => new
                {
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    RoundNo = table.Column<int>(type: "int", nullable: false),
                    BookId = table.Column<byte>(type: "tinyint", nullable: false),
                    Chapter = table.Column<short>(type: "smallint", nullable: false),
                    LastViewedVerse = table.Column<short>(type: "smallint", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChapterProgress", x => new { x.AccountId, x.RoundNo, x.BookId, x.Chapter });
                    table.CheckConstraint("CK_ChapterProgress_Verse", "LastViewedVerse >= 1");
                    table.ForeignKey(
                        name: "FK_ChapterProgress_Chapter",
                        columns: x => new { x.BookId, x.Chapter },
                        principalSchema: "app",
                        principalTable: "BibleChapter",
                        principalColumns: new[] { "BookId", "Chapter" });
                    table.ForeignKey(
                        name: "FK_ChapterProgress_Round",
                        columns: x => new { x.AccountId, x.RoundNo },
                        principalSchema: "app",
                        principalTable: "ReadingRound",
                        principalColumns: new[] { "AccountId", "RoundNo" });
                });

            migrationBuilder.CreateTable(
                name: "ChapterRead",
                schema: "app",
                columns: table => new
                {
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    RoundNo = table.Column<int>(type: "int", nullable: false),
                    BookId = table.Column<byte>(type: "tinyint", nullable: false),
                    Chapter = table.Column<short>(type: "smallint", nullable: false),
                    ReadUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChapterRead", x => new { x.AccountId, x.RoundNo, x.BookId, x.Chapter });
                    table.CheckConstraint("CK_ChapterRead_Chapter", "Chapter >= 1");
                    table.ForeignKey(
                        name: "FK_ChapterRead_Book",
                        column: x => x.BookId,
                        principalSchema: "app",
                        principalTable: "BibleBook",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ChapterRead_Round",
                        columns: x => new { x.AccountId, x.RoundNo },
                        principalSchema: "app",
                        principalTable: "ReadingRound",
                        principalColumns: new[] { "AccountId", "RoundNo" });
                });

            migrationBuilder.CreateTable(
                name: "RoundVerseRead",
                schema: "app",
                columns: table => new
                {
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    RoundNo = table.Column<int>(type: "int", nullable: false),
                    BookId = table.Column<byte>(type: "tinyint", nullable: false),
                    Chapter = table.Column<short>(type: "smallint", nullable: false),
                    Verse = table.Column<short>(type: "smallint", nullable: false),
                    ReadUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoundVerseRead", x => new { x.AccountId, x.RoundNo, x.BookId, x.Chapter, x.Verse });
                    table.CheckConstraint("CK_RoundVerseRead_Verse", "Verse >= 1");
                    table.ForeignKey(
                        name: "FK_RoundVerseRead_Chapter",
                        columns: x => new { x.BookId, x.Chapter },
                        principalSchema: "app",
                        principalTable: "BibleChapter",
                        principalColumns: new[] { "BookId", "Chapter" });
                    table.ForeignKey(
                        name: "FK_RoundVerseRead_Round",
                        columns: x => new { x.AccountId, x.RoundNo },
                        principalSchema: "app",
                        principalTable: "ReadingRound",
                        principalColumns: new[] { "AccountId", "RoundNo" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChapterProgress_BookId_Chapter",
                schema: "app",
                table: "ChapterProgress",
                columns: new[] { "BookId", "Chapter" });

            migrationBuilder.CreateIndex(
                name: "IX_ChapterProgress_Recent",
                schema: "app",
                table: "ChapterProgress",
                columns: new[] { "AccountId", "UpdatedUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ChapterRead_BookId",
                schema: "app",
                table: "ChapterRead",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_ChapterRead_Day",
                schema: "app",
                table: "ChapterRead",
                columns: new[] { "AccountId", "ReadUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ReadingRound_Open",
                schema: "app",
                table: "ReadingRound",
                column: "AccountId",
                unique: true,
                filter: "[CompletedUtc] IS NULL AND [ClosedUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RoundVerseRead_BookId_Chapter",
                schema: "app",
                table: "RoundVerseRead",
                columns: new[] { "BookId", "Chapter" });

            // 장마다 절 수: 이미 적재한 개역개정(번역 1)에서 센다(0041). 본문이 아직 없으면 성경 적재 도구가 채운다.
            migrationBuilder.Sql("""
                INSERT INTO app.BibleChapter (BookId, Chapter, VerseCount)
                SELECT BookId, Chapter, COUNT(*) FROM app.BibleVerse WHERE TranslationId = 1 GROUP BY BookId, Chapter;
                """);

            // 계정 범위 격리(0029 · 0040): 이번에는 본인 것만 읽고 쓴다(sec.fn_Account).
            // 참여자 현황(R-159)을 만들 때 읽기 조건을 sec.fn_ReadingRead 로 넓힌다(ADR 0057).
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY sec.TenantIsolation
                    ADD FILTER PREDICATE sec.fn_Account(AccountId) ON app.ReadingRound,
                    ADD BLOCK PREDICATE sec.fn_Account(AccountId) ON app.ReadingRound AFTER INSERT,
                    ADD BLOCK PREDICATE sec.fn_Account(AccountId) ON app.ReadingRound AFTER UPDATE,
                    ADD FILTER PREDICATE sec.fn_Account(AccountId) ON app.ChapterRead,
                    ADD BLOCK PREDICATE sec.fn_Account(AccountId) ON app.ChapterRead AFTER INSERT,
                    ADD BLOCK PREDICATE sec.fn_Account(AccountId) ON app.ChapterRead AFTER UPDATE,
                    ADD FILTER PREDICATE sec.fn_Account(AccountId) ON app.RoundVerseRead,
                    ADD BLOCK PREDICATE sec.fn_Account(AccountId) ON app.RoundVerseRead AFTER INSERT,
                    ADD BLOCK PREDICATE sec.fn_Account(AccountId) ON app.RoundVerseRead AFTER UPDATE,
                    ADD FILTER PREDICATE sec.fn_Account(AccountId) ON app.ChapterProgress,
                    ADD BLOCK PREDICATE sec.fn_Account(AccountId) ON app.ChapterProgress AFTER INSERT,
                    ADD BLOCK PREDICATE sec.fn_Account(AccountId) ON app.ChapterProgress AFTER UPDATE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY sec.TenantIsolation
                    DROP FILTER PREDICATE ON app.ReadingRound, DROP BLOCK PREDICATE ON app.ReadingRound AFTER INSERT, DROP BLOCK PREDICATE ON app.ReadingRound AFTER UPDATE,
                    DROP FILTER PREDICATE ON app.ChapterRead, DROP BLOCK PREDICATE ON app.ChapterRead AFTER INSERT, DROP BLOCK PREDICATE ON app.ChapterRead AFTER UPDATE,
                    DROP FILTER PREDICATE ON app.RoundVerseRead, DROP BLOCK PREDICATE ON app.RoundVerseRead AFTER INSERT, DROP BLOCK PREDICATE ON app.RoundVerseRead AFTER UPDATE,
                    DROP FILTER PREDICATE ON app.ChapterProgress, DROP BLOCK PREDICATE ON app.ChapterProgress AFTER INSERT, DROP BLOCK PREDICATE ON app.ChapterProgress AFTER UPDATE;
                """);

            migrationBuilder.DropTable(
                name: "ChapterProgress",
                schema: "app");

            migrationBuilder.DropTable(
                name: "ChapterRead",
                schema: "app");

            migrationBuilder.DropTable(
                name: "RoundVerseRead",
                schema: "app");

            migrationBuilder.DropTable(
                name: "BibleChapter",
                schema: "app");

            migrationBuilder.DropTable(
                name: "ReadingRound",
                schema: "app");
        }
    }
}
