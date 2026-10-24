using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Promotion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLog",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ActorAccountId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLog_Actor",
                        column: x => x.ActorAccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PromotionRequest",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    SourceTenantId = table.Column<int>(type: "int", nullable: false),
                    TargetTenantId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    RequestedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RequestedByAccountId = table.Column<int>(type: "int", nullable: false),
                    DecidedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DecidedByAccountId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionRequest", x => x.Id);
                    table.CheckConstraint("CK_PromotionRequest_Status", "Status IN ('Pending','Approved','Rejected','Cancelled')");
                    table.CheckConstraint("CK_PromotionRequest_Tenants", "SourceTenantId <> TargetTenantId");
                    table.ForeignKey(
                        name: "FK_PromotionRequest_Account",
                        column: x => x.AccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PromotionRequest_DecidedBy",
                        column: x => x.DecidedByAccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PromotionRequest_RequestedBy",
                        column: x => x.RequestedByAccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PromotionRequest_Source",
                        column: x => x.SourceTenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PromotionRequest_Target",
                        column: x => x.TargetTenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_ActorAccountId",
                schema: "app",
                table: "AuditLog",
                column: "ActorAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_Occurred",
                schema: "app",
                table: "AuditLog",
                column: "OccurredUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionRequest_DecidedByAccountId",
                schema: "app",
                table: "PromotionRequest",
                column: "DecidedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionRequest_RequestedByAccountId",
                schema: "app",
                table: "PromotionRequest",
                column: "RequestedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionRequest_Source",
                schema: "app",
                table: "PromotionRequest",
                columns: new[] { "SourceTenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionRequest_Target",
                schema: "app",
                table: "PromotionRequest",
                columns: new[] { "TargetTenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UQ_PromotionRequest_Pending",
                schema: "app",
                table: "PromotionRequest",
                columns: new[] { "AccountId", "SourceTenantId" },
                unique: true,
                filter: "[Status] = 'Pending'");

            // 진급 요청 격리(0030, 물리 설계 3-2): 보낸 영역 또는 받는 영역, 또는 시스템 연결만. AuditLog 는 플랫폼 표라 RLS 없음
            migrationBuilder.Sql("""
                CREATE FUNCTION sec.fn_Promotion(@SourceTenantId int, @TargetTenantId int)
                RETURNS TABLE WITH SCHEMABINDING AS
                RETURN SELECT 1 AS ok
                WHERE @SourceTenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
                   OR @TargetTenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
                   OR CAST(SESSION_CONTEXT(N'PlatformScope') AS int) = 1;
                """);
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY sec.TenantIsolation
                    ADD FILTER PREDICATE sec.fn_Promotion(SourceTenantId, TargetTenantId) ON app.PromotionRequest,
                    ADD BLOCK PREDICATE sec.fn_Promotion(SourceTenantId, TargetTenantId) ON app.PromotionRequest AFTER INSERT,
                    ADD BLOCK PREDICATE sec.fn_Promotion(SourceTenantId, TargetTenantId) ON app.PromotionRequest AFTER UPDATE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY sec.TenantIsolation
                    DROP FILTER PREDICATE ON app.PromotionRequest,
                    DROP BLOCK PREDICATE ON app.PromotionRequest AFTER INSERT,
                    DROP BLOCK PREDICATE ON app.PromotionRequest AFTER UPDATE;
                """);
            migrationBuilder.Sql("DROP FUNCTION sec.fn_Promotion;");
            migrationBuilder.DropTable(
                name: "AuditLog",
                schema: "app");

            migrationBuilder.DropTable(
                name: "PromotionRequest",
                schema: "app");
        }
    }
}
