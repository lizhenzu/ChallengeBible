using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GuardianLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GuardianLink",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    GuardianMembershipId = table.Column<int>(type: "int", nullable: false),
                    ParticipantId = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedByAccountId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuardianLink", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.UniqueConstraint("UQ_GuardianLink_TenantId_Id", x => new { x.TenantId, x.Id })
                        .Annotation("SqlServer:Clustered", true);
                    table.ForeignKey(
                        name: "FK_GuardianLink_CreatedBy",
                        column: x => x.CreatedByAccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GuardianLink_Guardian",
                        columns: x => new { x.TenantId, x.GuardianMembershipId },
                        principalSchema: "app",
                        principalTable: "Membership",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_GuardianLink_Participant",
                        columns: x => new { x.TenantId, x.ParticipantId },
                        principalSchema: "app",
                        principalTable: "Participant",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_GuardianLink_CreatedByAccountId",
                schema: "app",
                table: "GuardianLink",
                column: "CreatedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GuardianLink_Participant",
                schema: "app",
                table: "GuardianLink",
                columns: new[] { "TenantId", "ParticipantId" });

            migrationBuilder.CreateIndex(
                name: "UQ_GuardianLink_Pair",
                schema: "app",
                table: "GuardianLink",
                columns: new[] { "TenantId", "GuardianMembershipId", "ParticipantId" },
                unique: true);

            // 영역 격리(물리 설계 3-2)
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY sec.TenantIsolation
                    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.GuardianLink,
                    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GuardianLink AFTER INSERT,
                    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GuardianLink AFTER UPDATE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY sec.TenantIsolation
                    DROP FILTER PREDICATE ON app.GuardianLink,
                    DROP BLOCK PREDICATE ON app.GuardianLink AFTER INSERT,
                    DROP BLOCK PREDICATE ON app.GuardianLink AFTER UPDATE;
                """);
            migrationBuilder.DropTable(
                name: "GuardianLink",
                schema: "app");
        }
    }
}
