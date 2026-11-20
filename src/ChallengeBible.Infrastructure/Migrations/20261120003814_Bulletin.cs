using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Bulletin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bulletin",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bulletin", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Bulletin_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BulletinFile",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BulletinId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StoredName = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    ContentType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulletinFile", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_BulletinFile_Bulletin_TenantId_BulletinId",
                        columns: x => new { x.TenantId, x.BulletinId },
                        principalSchema: "app",
                        principalTable: "Bulletin",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bulletin_TenantId_Date",
                schema: "app",
                table: "Bulletin",
                columns: new[] { "TenantId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_BulletinFile_TenantId_BulletinId",
                schema: "app",
                table: "BulletinFile",
                columns: new[] { "TenantId", "BulletinId" });
            // 부서 격리(RLS, ADR 0117): 다른 부서 주보 · 파일은 읽지도 쓰지도 못한다
            migrationBuilder.Sql("EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.Bulletin, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Bulletin AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Bulletin AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.BulletinFile, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.BulletinFile AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.BulletinFile AFTER UPDATE;')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation DROP FILTER PREDICATE ON app.Bulletin, DROP BLOCK PREDICATE ON app.Bulletin AFTER INSERT, DROP BLOCK PREDICATE ON app.Bulletin AFTER UPDATE, DROP FILTER PREDICATE ON app.BulletinFile, DROP BLOCK PREDICATE ON app.BulletinFile AFTER INSERT, DROP BLOCK PREDICATE ON app.BulletinFile AFTER UPDATE;");
            migrationBuilder.DropTable(
                name: "BulletinFile",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Bulletin",
                schema: "app");
        }
    }
}
