using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChapterMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChapterMedia",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookId = table.Column<byte>(type: "tinyint", nullable: false),
                    Chapter = table.Column<short>(type: "smallint", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    SortOrder = table.Column<byte>(type: "tinyint", nullable: false),
                    YoutubeVideoId = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DurationSec = table.Column<int>(type: "int", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChapterMedia", x => x.Id);
                    table.CheckConstraint("CK_ChapterMedia_Kind", "Kind IN ('Listen','Overview')");
                    table.ForeignKey(
                        name: "FK_ChapterMedia_Book",
                        column: x => x.BookId,
                        principalSchema: "app",
                        principalTable: "BibleBook",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "UQ_ChapterMedia_Slot",
                schema: "app",
                table: "ChapterMedia",
                columns: new[] { "BookId", "Chapter", "Kind", "SortOrder" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChapterMedia",
                schema: "app");
        }
    }
}
