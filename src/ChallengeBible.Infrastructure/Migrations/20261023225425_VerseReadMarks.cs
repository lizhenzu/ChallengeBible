using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VerseReadMarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bookmark",
                schema: "app",
                columns: table => new
                {
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    BookId = table.Column<byte>(type: "tinyint", nullable: false),
                    Chapter = table.Column<short>(type: "smallint", nullable: false),
                    Verse = table.Column<short>(type: "smallint", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookmark", x => new { x.AccountId, x.BookId, x.Chapter, x.Verse });
                    table.ForeignKey(
                        name: "FK_Bookmark_Account",
                        column: x => x.AccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Bookmark_Book",
                        column: x => x.BookId,
                        principalSchema: "app",
                        principalTable: "BibleBook",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "VerseReadMark",
                schema: "app",
                columns: table => new
                {
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    BookId = table.Column<byte>(type: "tinyint", nullable: false),
                    Chapter = table.Column<short>(type: "smallint", nullable: false),
                    Verse = table.Column<short>(type: "smallint", nullable: false),
                    ReadUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerseReadMark", x => new { x.AccountId, x.BookId, x.Chapter, x.Verse });
                    table.ForeignKey(
                        name: "FK_VerseReadMark_Account",
                        column: x => x.AccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VerseReadMark_Book",
                        column: x => x.BookId,
                        principalSchema: "app",
                        principalTable: "BibleBook",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookmark_BookId",
                schema: "app",
                table: "Bookmark",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_VerseReadMark_BookId",
                schema: "app",
                table: "VerseReadMark",
                column: "BookId");

            // 계정 범위 격리(0029, 물리 설계 3-2): 로그인 계정 = 소유 계정, 또는 시스템 연결(관리자 초기화)
            migrationBuilder.Sql("""
                CREATE FUNCTION sec.fn_Account(@AccountId int)
                RETURNS TABLE WITH SCHEMABINDING AS
                RETURN SELECT 1 AS ok
                WHERE @AccountId = CAST(SESSION_CONTEXT(N'AccountId') AS int)
                   OR CAST(SESSION_CONTEXT(N'PlatformScope') AS int) = 1;
                """);
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY sec.TenantIsolation
                    ADD FILTER PREDICATE sec.fn_Account(AccountId) ON app.VerseReadMark,
                    ADD BLOCK PREDICATE sec.fn_Account(AccountId) ON app.VerseReadMark AFTER INSERT,
                    ADD BLOCK PREDICATE sec.fn_Account(AccountId) ON app.VerseReadMark AFTER UPDATE,
                    ADD FILTER PREDICATE sec.fn_Account(AccountId) ON app.Bookmark,
                    ADD BLOCK PREDICATE sec.fn_Account(AccountId) ON app.Bookmark AFTER INSERT,
                    ADD BLOCK PREDICATE sec.fn_Account(AccountId) ON app.Bookmark AFTER UPDATE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY sec.TenantIsolation
                    DROP FILTER PREDICATE ON app.VerseReadMark,
                    DROP BLOCK PREDICATE ON app.VerseReadMark AFTER INSERT,
                    DROP BLOCK PREDICATE ON app.VerseReadMark AFTER UPDATE,
                    DROP FILTER PREDICATE ON app.Bookmark,
                    DROP BLOCK PREDICATE ON app.Bookmark AFTER INSERT,
                    DROP BLOCK PREDICATE ON app.Bookmark AFTER UPDATE;
                """);
            migrationBuilder.Sql("DROP FUNCTION sec.fn_Account;");

            migrationBuilder.DropTable(
                name: "Bookmark",
                schema: "app");

            migrationBuilder.DropTable(
                name: "VerseReadMark",
                schema: "app");
        }
    }
}
