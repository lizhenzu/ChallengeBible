using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlatformRuleSet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformRuleSetVersion",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    Values = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ChangedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ChangedByAccountId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformRuleSetVersion", x => x.Id);
                    table.CheckConstraint("CK_PlatformRuleSetVersion_Values", "ISJSON([Values]) = 1");
                    table.ForeignKey(
                        name: "FK_PlatformRuleSetVersion_Account",
                        column: x => x.ChangedByAccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformRuleSetVersion_ChangedByAccountId",
                schema: "app",
                table: "PlatformRuleSetVersion",
                column: "ChangedByAccountId");

            migrationBuilder.CreateIndex(
                name: "UQ_PlatformRuleSetVersion_No",
                schema: "app",
                table: "PlatformRuleSetVersion",
                column: "VersionNo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformRuleSetVersion",
                schema: "app");
        }
    }
}
