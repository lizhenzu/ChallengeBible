using System;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <summary>ADR 0142 — 이번 주 말씀(app.WeeklyWord) + 부서 스위치 Tenant.MissionBookEnabled(유년부만 켠다).</summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261127010000_WeeklyWordMissionBook")]
    public partial class WeeklyWordMissionBook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MissionBookEnabled",
                schema: "app",
                table: "Tenant",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "WeeklyWord",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    SundayDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyWord", x => new { x.TenantId, x.SundayDate });
                    table.ForeignKey(
                        name: "FK_WeeklyWord_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            // 유년부(이름 '잠실새내교회 유년부')만 미션북 칸을 켠다.
            migrationBuilder.Sql("UPDATE [app].[Tenant] SET [MissionBookEnabled] = 1 WHERE [Name] = N'잠실새내교회 유년부';");

            // 부서 격리(RLS): 이번 주 말씀도 TenantId 로 가둔다(D20).
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.WeeklyWord, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.WeeklyWord AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.WeeklyWord AFTER UPDATE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation DROP FILTER PREDICATE ON app.WeeklyWord, DROP BLOCK PREDICATE ON app.WeeklyWord AFTER INSERT, DROP BLOCK PREDICATE ON app.WeeklyWord AFTER UPDATE;");
            migrationBuilder.DropTable(name: "WeeklyWord", schema: "app");
            migrationBuilder.DropColumn(name: "MissionBookEnabled", schema: "app", table: "Tenant");
        }
    }
}
