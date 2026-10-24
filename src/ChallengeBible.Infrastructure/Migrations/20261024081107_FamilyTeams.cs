using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FamilyTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FamilyTeam",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Memo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyTeam", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.UniqueConstraint("UQ_FamilyTeam_TenantId_Id", x => new { x.TenantId, x.Id })
                        .Annotation("SqlServer:Clustered", true);
                    table.ForeignKey(
                        name: "FK_FamilyTeam_Tenant",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FamilyMember",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    FamilyTeamId = table.Column<int>(type: "int", nullable: false),
                    ParticipantId = table.Column<int>(type: "int", nullable: false),
                    Relation = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    SortOrder = table.Column<byte>(type: "tinyint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyMember", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.UniqueConstraint("UQ_FamilyMember_TenantId_Id", x => new { x.TenantId, x.Id })
                        .Annotation("SqlServer:Clustered", true);
                    table.CheckConstraint("CK_FamilyMember_Relation", "Relation IN ('Student','Mother','Father','Grandmother','Grandfather','Other')");
                    table.ForeignKey(
                        name: "FK_FamilyMember_Participant",
                        columns: x => new { x.TenantId, x.ParticipantId },
                        principalSchema: "app",
                        principalTable: "Participant",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_FamilyMember_Team",
                        columns: x => new { x.TenantId, x.FamilyTeamId },
                        principalSchema: "app",
                        principalTable: "FamilyTeam",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "FamilyMilestoneDismiss",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    FamilyTeamId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<short>(type: "smallint", nullable: false),
                    Threshold = table.Column<int>(type: "int", nullable: false),
                    DismissedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    DismissedByAccountId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyMilestoneDismiss", x => new { x.TenantId, x.FamilyTeamId, x.Year, x.Threshold });
                    table.ForeignKey(
                        name: "FK_FamilyMilestoneDismiss_Account",
                        column: x => x.DismissedByAccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FamilyMilestoneDismiss_Team",
                        columns: x => new { x.TenantId, x.FamilyTeamId },
                        principalSchema: "app",
                        principalTable: "FamilyTeam",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "FamilyMilestoneReached",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    FamilyTeamId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<short>(type: "smallint", nullable: false),
                    Threshold = table.Column<int>(type: "int", nullable: false),
                    ReachedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyMilestoneReached", x => new { x.TenantId, x.FamilyTeamId, x.Year, x.Threshold });
                    table.ForeignKey(
                        name: "FK_FamilyMilestoneReached_Team",
                        columns: x => new { x.TenantId, x.FamilyTeamId },
                        principalSchema: "app",
                        principalTable: "FamilyTeam",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "UQ_FamilyMember_TeamParticipant",
                schema: "app",
                table: "FamilyMember",
                columns: new[] { "TenantId", "FamilyTeamId", "ParticipantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_FamilyMember_OneActiveTeam",
                schema: "app",
                table: "FamilyMember",
                columns: new[] { "TenantId", "ParticipantId" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMilestoneDismiss_DismissedByAccountId",
                schema: "app",
                table: "FamilyMilestoneDismiss",
                column: "DismissedByAccountId");

            // 영역 격리(물리 설계 3-2): 가족팀 표 4개 모두 이 영역만 읽고 쓴다
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation " + string.Join(", ", Tables.Select(t =>
                $"ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.{t}, " +
                $"ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.{t} AFTER INSERT, " +
                $"ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.{t} AFTER UPDATE")) + ";");
        }

        private static readonly string[] Tables = ["FamilyTeam", "FamilyMember", "FamilyMilestoneReached", "FamilyMilestoneDismiss"];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation " + string.Join(", ", Tables.Select(t =>
                $"DROP FILTER PREDICATE ON app.{t}, DROP BLOCK PREDICATE ON app.{t} AFTER INSERT, DROP BLOCK PREDICATE ON app.{t} AFTER UPDATE")) + ";");
            migrationBuilder.DropTable(
                name: "FamilyMember",
                schema: "app");

            migrationBuilder.DropTable(
                name: "FamilyMilestoneDismiss",
                schema: "app");

            migrationBuilder.DropTable(
                name: "FamilyMilestoneReached",
                schema: "app");

            migrationBuilder.DropTable(
                name: "FamilyTeam",
                schema: "app");
        }
    }
}
