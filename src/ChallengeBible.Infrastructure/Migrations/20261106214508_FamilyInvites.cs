using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FamilyInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FamilyInvite",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    FamilyTeamId = table.Column<int>(type: "int", nullable: false),
                    ParticipantId = table.Column<int>(type: "int", nullable: false),
                    InvitedByParticipantId = table.Column<int>(type: "int", nullable: false),
                    Relation = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyInvite", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.UniqueConstraint("UQ_FamilyInvite_TenantId_Id", x => new { x.TenantId, x.Id })
                        .Annotation("SqlServer:Clustered", true);
                    table.CheckConstraint("CK_FamilyInvite_Relation", "Relation IN ('Student','Mother','Father','Grandmother','Grandfather','Other')");
                    table.ForeignKey(
                        name: "FK_FamilyInvite_InvitedBy",
                        columns: x => new { x.TenantId, x.InvitedByParticipantId },
                        principalSchema: "app",
                        principalTable: "Participant",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_FamilyInvite_Participant",
                        columns: x => new { x.TenantId, x.ParticipantId },
                        principalSchema: "app",
                        principalTable: "Participant",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_FamilyInvite_Team",
                        columns: x => new { x.TenantId, x.FamilyTeamId },
                        principalSchema: "app",
                        principalTable: "FamilyTeam",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyInvite_InvitedBy",
                schema: "app",
                table: "FamilyInvite",
                columns: new[] { "TenantId", "InvitedByParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyInvite_Participant",
                schema: "app",
                table: "FamilyInvite",
                columns: new[] { "TenantId", "ParticipantId" });

            migrationBuilder.CreateIndex(
                name: "UX_FamilyInvite_TeamParticipant",
                schema: "app",
                table: "FamilyInvite",
                columns: new[] { "TenantId", "FamilyTeamId", "ParticipantId" },
                unique: true);

            // RLS: 같은 영역만 읽고 쓴다(ADR 0084)
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyInvite, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyInvite AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyInvite AFTER UPDATE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation DROP FILTER PREDICATE ON app.FamilyInvite, DROP BLOCK PREDICATE ON app.FamilyInvite AFTER INSERT, DROP BLOCK PREDICATE ON app.FamilyInvite AFTER UPDATE;");

            migrationBuilder.DropTable(
                name: "FamilyInvite",
                schema: "app");
        }
    }
}
