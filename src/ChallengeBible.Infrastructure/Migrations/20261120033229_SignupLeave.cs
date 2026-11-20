using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SignupLeave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "BirthDay",
                schema: "app",
                table: "UserSetting",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "BirthMonth",
                schema: "app",
                table: "UserSetting",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LeftOn",
                schema: "app",
                table: "Person",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LeaveRequest",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RoleCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    RequestedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRequest", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeaveRequest_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequest_UserName",
                schema: "app",
                table: "LeaveRequest",
                column: "UserName",
                unique: true);

            // 부서 격리(D20): 탈퇴 신청도 RLS 로
            migrationBuilder.Sql("EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.LeaveRequest, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.LeaveRequest AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.LeaveRequest AFTER UPDATE;')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation DROP FILTER PREDICATE ON app.LeaveRequest, DROP BLOCK PREDICATE ON app.LeaveRequest AFTER INSERT, DROP BLOCK PREDICATE ON app.LeaveRequest AFTER UPDATE;");

            migrationBuilder.DropTable(
                name: "LeaveRequest",
                schema: "app");

            migrationBuilder.DropColumn(
                name: "BirthDay",
                schema: "app",
                table: "UserSetting");

            migrationBuilder.DropColumn(
                name: "BirthMonth",
                schema: "app",
                table: "UserSetting");

            migrationBuilder.DropColumn(
                name: "LeftOn",
                schema: "app",
                table: "Person");
        }
    }
}
