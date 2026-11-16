using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Duty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Duty",
                schema: "app",
                columns: table => new
                {
                    Key = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsAdmin = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Duty", x => x.Key);
                });

            migrationBuilder.InsertData(
                schema: "app",
                table: "Duty",
                columns: new[] { "Key", "CreatedUtc", "IsAdmin", "Name", "SortOrder" },
                values: new object[] { "Gift", new DateTime(2026, 11, 16, 0, 0, 0, 0, DateTimeKind.Utc), true, "선물 담당", 1 });

            migrationBuilder.CreateIndex(
                name: "UX_Duty_Name",
                schema: "app",
                table: "Duty",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Duty",
                schema: "app");
        }
    }
}
