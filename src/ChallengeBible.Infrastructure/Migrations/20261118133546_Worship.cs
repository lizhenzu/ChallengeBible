using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Worship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorshipSchedule",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DayOfWeek = table.Column<byte>(type: "tinyint", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time(0)", nullable: false),
                    Place = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BulletinUrl = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    BulletinDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorshipSchedule", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_WorshipSchedule_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorshipSchedule_TenantId_SortOrder",
                schema: "app",
                table: "WorshipSchedule",
                columns: new[] { "TenantId", "SortOrder" });
            // 부서 격리(D20): 다른 부서 표와 같은 RLS 조건
            migrationBuilder.Sql("EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.WorshipSchedule, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.WorshipSchedule AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.WorshipSchedule AFTER UPDATE;')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation DROP FILTER PREDICATE ON app.WorshipSchedule, DROP BLOCK PREDICATE ON app.WorshipSchedule AFTER INSERT, DROP BLOCK PREDICATE ON app.WorshipSchedule AFTER UPDATE;");

            migrationBuilder.DropTable(
                name: "WorshipSchedule",
                schema: "app");
        }
    }
}
