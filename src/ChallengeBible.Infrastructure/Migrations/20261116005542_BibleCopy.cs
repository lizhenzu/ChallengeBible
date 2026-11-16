using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BibleCopy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenant_Program",
                schema: "app",
                table: "Tenant");

            migrationBuilder.AddColumn<bool>(
                name: "CopyEnabled",
                schema: "app",
                table: "Tenant",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CopyVerse",
                schema: "app",
                columns: table => new
                {
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    BookId = table.Column<byte>(type: "tinyint", nullable: false),
                    Chapter = table.Column<short>(type: "smallint", nullable: false),
                    Verse = table.Column<short>(type: "smallint", nullable: false),
                    Translation = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    WrittenAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CopyVerse", x => new { x.UserName, x.BookId, x.Chapter, x.Verse });
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenant_Program",
                schema: "app",
                table: "Tenant",
                sql: "MissionEnabled = 1 OR ReadingEnabled = 1 OR CopyEnabled = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CopyVerse_User_WrittenAt",
                schema: "app",
                table: "CopyVerse",
                columns: new[] { "UserName", "WrittenAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CopyVerse",
                schema: "app");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenant_Program",
                schema: "app",
                table: "Tenant");

            migrationBuilder.DropColumn(
                name: "CopyEnabled",
                schema: "app",
                table: "Tenant");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenant_Program",
                schema: "app",
                table: "Tenant",
                sql: "MissionEnabled = 1 OR ReadingEnabled = 1");
        }
    }
}
