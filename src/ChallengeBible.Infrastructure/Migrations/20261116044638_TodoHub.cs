using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TodoHub : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "TodoSnoozedUntil",
                schema: "app",
                table: "UserSetting",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MemberTag",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Tag = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberTag", x => new { x.TenantId, x.UserId, x.Tag });
                    table.ForeignKey(
                        name: "FK_MemberTag_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TodoRule",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    Kind = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Roles = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TodoRule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TodoRule_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "UX_TodoRule_Tenant_Kind",
                schema: "app",
                table: "TodoRule",
                columns: new[] { "TenantId", "Kind" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
            // 보안 조건(RLS, D20): 업무 꼬리표는 부서 범위, 할 일 규칙은 공통(null) + 부서(ADR 0100)
            migrationBuilder.Sql("EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.MemberTag, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.MemberTag AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.MemberTag AFTER UPDATE, ADD FILTER PREDICATE sec.fn_SharedRead(TenantId) ON app.TodoRule, ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.TodoRule AFTER INSERT, ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.TodoRule AFTER UPDATE, ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.TodoRule BEFORE DELETE;')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation DROP FILTER PREDICATE ON app.MemberTag, DROP BLOCK PREDICATE ON app.MemberTag AFTER INSERT, DROP BLOCK PREDICATE ON app.MemberTag AFTER UPDATE, DROP FILTER PREDICATE ON app.TodoRule, DROP BLOCK PREDICATE ON app.TodoRule AFTER INSERT, DROP BLOCK PREDICATE ON app.TodoRule AFTER UPDATE, DROP BLOCK PREDICATE ON app.TodoRule BEFORE DELETE;");
            migrationBuilder.DropTable(
                name: "MemberTag",
                schema: "app");

            migrationBuilder.DropTable(
                name: "TodoRule",
                schema: "app");

            migrationBuilder.DropColumn(
                name: "TodoSnoozedUntil",
                schema: "app",
                table: "UserSetting");
        }
    }
}
