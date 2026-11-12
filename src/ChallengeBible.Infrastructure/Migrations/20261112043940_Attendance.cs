using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Attendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NewcomerEvangelist",
                schema: "app",
                table: "Person",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "NewcomerFirstVisit",
                schema: "app",
                table: "Person",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NewcomerVoluntary",
                schema: "app",
                table: "Person",
                type: "bit",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AttendanceCheck",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    Sunday = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceCheck", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AttendanceCheck_Person_TenantId_PersonId",
                        columns: x => new { x.TenantId, x.PersonId },
                        principalSchema: "app",
                        principalTable: "Person",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCheck_TenantId_PersonId_Sunday",
                schema: "app",
                table: "AttendanceCheck",
                columns: new[] { "TenantId", "PersonId", "Sunday" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCheck_TenantId_Sunday",
                schema: "app",
                table: "AttendanceCheck",
                columns: new[] { "TenantId", "Sunday" });

            // EXEC: keep the batch from checking the table before it exists (real DB 33268)
            migrationBuilder.Sql("EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.AttendanceCheck, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.AttendanceCheck AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.AttendanceCheck AFTER UPDATE;')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation DROP FILTER PREDICATE ON app.AttendanceCheck, DROP BLOCK PREDICATE ON app.AttendanceCheck AFTER INSERT, DROP BLOCK PREDICATE ON app.AttendanceCheck AFTER UPDATE;");

            migrationBuilder.DropTable(
                name: "AttendanceCheck",
                schema: "app");

            migrationBuilder.DropColumn(
                name: "NewcomerEvangelist",
                schema: "app",
                table: "Person");

            migrationBuilder.DropColumn(
                name: "NewcomerFirstVisit",
                schema: "app",
                table: "Person");

            migrationBuilder.DropColumn(
                name: "NewcomerVoluntary",
                schema: "app",
                table: "Person");
        }
    }
}
