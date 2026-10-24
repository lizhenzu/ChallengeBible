using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SearchLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SearchLog",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Term = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SearchedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchLog", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_SearchLog_Tenant",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SearchLog_Recent",
                schema: "app",
                table: "SearchLog",
                columns: new[] { "TenantId", "SearchedUtc" })
                .Annotation("SqlServer:Include", new[] { "Term" });

            migrationBuilder.CreateIndex(
                name: "UQ_SearchLog_TenantId_Id",
                schema: "app",
                table: "SearchLog",
                columns: new[] { "TenantId", "Id" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            // 부서 격리(물리 설계 3-2, R-164): 같은 부서 것만 보이고, 다른 부서 값으로 넣을 수 없다
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY sec.TenantIsolation
                    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.SearchLog,
                    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.SearchLog AFTER INSERT,
                    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.SearchLog AFTER UPDATE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY sec.TenantIsolation
                    DROP FILTER PREDICATE ON app.SearchLog,
                    DROP BLOCK PREDICATE ON app.SearchLog AFTER INSERT,
                    DROP BLOCK PREDICATE ON app.SearchLog AFTER UPDATE;
                """);
            migrationBuilder.DropTable(
                name: "SearchLog",
                schema: "app");
        }
    }
}
