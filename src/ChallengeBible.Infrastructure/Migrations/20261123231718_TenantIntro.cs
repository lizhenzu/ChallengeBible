using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TenantIntro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantIntro",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PhotoStoredName = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: true),
                    PhotoContentType = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantIntro", x => x.TenantId);
                    table.ForeignKey(
                        name: "FK_TenantIntro_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TenantIntroSeen",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    HideUntilUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    Never = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantIntroSeen", x => new { x.TenantId, x.UserName });
                    table.ForeignKey(
                        name: "FK_TenantIntroSeen_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });
            // 부서 격리(D20): 다른 부서 표와 같은 RLS 조건
            migrationBuilder.Sql("EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.TenantIntro, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.TenantIntro AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.TenantIntro AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.TenantIntroSeen, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.TenantIntroSeen AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.TenantIntroSeen AFTER UPDATE;')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation DROP FILTER PREDICATE ON app.TenantIntro, DROP BLOCK PREDICATE ON app.TenantIntro AFTER INSERT, DROP BLOCK PREDICATE ON app.TenantIntro AFTER UPDATE, DROP FILTER PREDICATE ON app.TenantIntroSeen, DROP BLOCK PREDICATE ON app.TenantIntroSeen AFTER INSERT, DROP BLOCK PREDICATE ON app.TenantIntroSeen AFTER UPDATE;");

            migrationBuilder.DropTable(
                name: "TenantIntro",
                schema: "app");

            migrationBuilder.DropTable(
                name: "TenantIntroSeen",
                schema: "app");
        }
    }
}
