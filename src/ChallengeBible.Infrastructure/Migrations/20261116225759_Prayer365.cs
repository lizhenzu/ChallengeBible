using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Prayer365 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Prayer365",
                schema: "app",
                columns: table => new
                {
                    MonthDay = table.Column<string>(type: "char(4)", unicode: false, fixedLength: true, maxLength: 4, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    VerseRef = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    VerseText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Fixes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSample = table.Column<bool>(type: "bit", nullable: false),
                    IsReviewed = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prayer365", x => x.MonthDay);
                });

            migrationBuilder.CreateTable(
                name: "Prayer365Log",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PrayDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MonthDay = table.Column<string>(type: "char(4)", unicode: false, fixedLength: true, maxLength: 4, nullable: false),
                    PrayedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prayer365Log", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Prayer365Log_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Prayer365View",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    ViewDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MonthDay = table.Column<string>(type: "char(4)", unicode: false, fixedLength: true, maxLength: 4, nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prayer365View", x => new { x.TenantId, x.ViewDate, x.MonthDay });
                    table.ForeignKey(
                        name: "FK_Prayer365View_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Prayer365Log_TenantId_PrayDate",
                schema: "app",
                table: "Prayer365Log",
                columns: new[] { "TenantId", "PrayDate" });

            migrationBuilder.CreateIndex(
                name: "UX_Prayer365Log_User_Day",
                schema: "app",
                table: "Prayer365Log",
                columns: new[] { "UserName", "PrayDate" },
                unique: true);

            // 부서 격리(D20): 기도 기록 · 조회 수는 부서 표(RLS). 기도문(Prayer365)은 플랫폼 공용 — 정책 없음(Duty 처럼)
            migrationBuilder.Sql("EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.Prayer365Log, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Prayer365Log AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Prayer365Log AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.Prayer365View, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Prayer365View AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Prayer365View AFTER UPDATE;')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation DROP FILTER PREDICATE ON app.Prayer365Log, DROP BLOCK PREDICATE ON app.Prayer365Log AFTER INSERT, DROP BLOCK PREDICATE ON app.Prayer365Log AFTER UPDATE, DROP FILTER PREDICATE ON app.Prayer365View, DROP BLOCK PREDICATE ON app.Prayer365View AFTER INSERT, DROP BLOCK PREDICATE ON app.Prayer365View AFTER UPDATE;");

            migrationBuilder.DropTable(
                name: "Prayer365",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Prayer365Log",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Prayer365View",
                schema: "app");
        }
    }
}
