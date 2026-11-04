using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WordClips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyWordClip",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WordDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Seq = table.Column<byte>(type: "tinyint", nullable: false),
                    Chapter = table.Column<short>(type: "smallint", nullable: false),
                    YoutubeVideoId = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    StartSec = table.Column<int>(type: "int", nullable: true),
                    EndSec = table.Column<int>(type: "int", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyWordClip", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UQ_DailyWordClip_Slot",
                schema: "app",
                table: "DailyWordClip",
                columns: new[] { "WordDate", "Seq" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyWordClip",
                schema: "app");
        }
    }
}
