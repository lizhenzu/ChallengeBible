using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BibleBookOverview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BibleBookOverview",
                schema: "app",
                columns: table => new
                {
                    BookId = table.Column<byte>(type: "tinyint", nullable: false),
                    Author = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WhenWritten = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Purpose = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DetailAuthor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AuthorDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WrittenFrom = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    WrittenTo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AlternativeDate = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WrittenPlace = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WrittenTarget = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HistoricalPeriod = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    HistoricalBackground = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Keywords = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ContentSummary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VideoUrl1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VideoUrl2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BibleBookOverview", x => x.BookId);
                    table.ForeignKey(
                        name: "FK_BibleBookOverview_Book",
                        column: x => x.BookId,
                        principalSchema: "app",
                        principalTable: "BibleBook",
                        principalColumn: "Id");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BibleBookOverview",
                schema: "app");
        }
    }
}
