using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Gift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GiftCreditRate",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Credits = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftCreditRate", x => new { x.TenantId, x.Credits });
                    table.CheckConstraint("CK_GiftCreditRate_Values", "[Credits] > 0 AND [Credits] % 1000 = 0 AND [Amount] >= 0");
                    table.ForeignKey(
                        name: "FK_GiftCreditRate_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GiftItem",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1000, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    SharedByTenantId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Price = table.Column<int>(type: "int", nullable: false),
                    CoupangUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    ReviewReason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    HasImage = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftItem", x => x.Id);
                    table.CheckConstraint("CK_GiftItem_Price", "[Price] >= 0");
                    table.CheckConstraint("CK_GiftItem_Status", "[Status] IN ('Active','Review','Retired')");
                    table.ForeignKey(
                        name: "FK_GiftItem_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GiftReadingAmount",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    RoundNo = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftReadingAmount", x => new { x.TenantId, x.RoundNo });
                    table.CheckConstraint("CK_GiftReadingAmount_Values", "[RoundNo] >= 1 AND [Amount] >= 0");
                    table.ForeignKey(
                        name: "FK_GiftReadingAmount_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GiftSetting",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    PersonalCap = table.Column<int>(type: "int", nullable: true),
                    NewcomerMin = table.Column<int>(type: "int", nullable: true),
                    NewcomerMax = table.Column<int>(type: "int", nullable: true),
                    InviteMin = table.Column<int>(type: "int", nullable: true),
                    InviteMax = table.Column<int>(type: "int", nullable: true),
                    BirthdayMin = table.Column<int>(type: "int", nullable: true),
                    BirthdayMax = table.Column<int>(type: "int", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftSetting", x => x.TenantId);
                    table.ForeignKey(
                        name: "FK_GiftSetting_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GiftYearClose",
                schema: "app",
                columns: table => new
                {
                    Year = table.Column<short>(type: "smallint", nullable: false),
                    ClosedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ClosedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftYearClose", x => x.Year);
                });

            migrationBuilder.CreateTable(
                name: "TeamAwardArchive",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<short>(type: "smallint", nullable: false),
                    AwardId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AwardKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Threshold = table.Column<int>(type: "int", nullable: false),
                    AwardedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamAwardArchive", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_TeamAwardArchive_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Gift",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Year = table.Column<short>(type: "smallint", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PersonId = table.Column<int>(type: "int", nullable: true),
                    FamilyTeamId = table.Column<int>(type: "int", nullable: true),
                    SourceKey = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    State = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    OpenFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    OpenUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    MinAmount = table.Column<int>(type: "int", nullable: false),
                    MaxAmount = table.Column<int>(type: "int", nullable: false),
                    GiftItemId = table.Column<int>(type: "int", nullable: true),
                    ItemName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ItemPrice = table.Column<int>(type: "int", nullable: true),
                    ItemHasImage = table.Column<bool>(type: "bit", nullable: true),
                    CreditsUsed = table.Column<int>(type: "int", nullable: true),
                    PointsUsed = table.Column<int>(type: "int", nullable: true),
                    PointsBurned = table.Column<int>(type: "int", nullable: true),
                    LinkToken = table.Column<string>(type: "char(32)", unicode: false, fixedLength: true, maxLength: 32, nullable: true),
                    PickedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    PurchasedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    DeliveredUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Gift", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Gift_Amount", "[MinAmount] >= 0 AND [MaxAmount] >= [MinAmount]");
                    table.CheckConstraint("CK_Gift_Kind", "[Kind] IN ('Personal','Newcomer','Invite','Birthday','Reading','Family','Friend')");
                    table.CheckConstraint("CK_Gift_State", "[State] IN ('Open','Picked','Purchased','Delivered','Cancelled','Skipped','Expired','YearClosed')");
                    table.CheckConstraint("CK_Gift_Who", "[UserName] IS NOT NULL OR [PersonId] IS NOT NULL OR [FamilyTeamId] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_Gift_GiftItem_GiftItemId",
                        column: x => x.GiftItemId,
                        principalSchema: "app",
                        principalTable: "GiftItem",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Gift_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GiftItemHidden",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    GiftItemId = table.Column<int>(type: "int", nullable: false),
                    HiddenUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    HiddenByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftItemHidden", x => new { x.TenantId, x.GiftItemId });
                    table.ForeignKey(
                        name: "FK_GiftItemHidden_GiftItem_GiftItemId",
                        column: x => x.GiftItemId,
                        principalSchema: "app",
                        principalTable: "GiftItem",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GiftItemHidden_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GiftLog",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GiftId = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    ActorUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ItemPrice = table.Column<int>(type: "int", nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftLog", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_GiftLog_Gift_TenantId_GiftId",
                        columns: x => new { x.TenantId, x.GiftId },
                        principalSchema: "app",
                        principalTable: "Gift",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "GiftPointUse",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Year = table.Column<short>(type: "smallint", nullable: false),
                    GiftId = table.Column<long>(type: "bigint", nullable: false),
                    PointsUsed = table.Column<int>(type: "int", nullable: false),
                    PointsBurned = table.Column<int>(type: "int", nullable: false),
                    FirstBonus = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftPointUse", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_GiftPointUse_Points", "[PointsUsed] >= 0 AND [PointsBurned] >= 0");
                    table.ForeignKey(
                        name: "FK_GiftPointUse_Gift_TenantId_GiftId",
                        columns: x => new { x.TenantId, x.GiftId },
                        principalSchema: "app",
                        principalTable: "Gift",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Gift_GiftItemId",
                schema: "app",
                table: "Gift",
                column: "GiftItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Gift_TenantId_UserName",
                schema: "app",
                table: "Gift",
                columns: new[] { "TenantId", "UserName" });

            migrationBuilder.CreateIndex(
                name: "IX_Gift_TenantId_Year_State",
                schema: "app",
                table: "Gift",
                columns: new[] { "TenantId", "Year", "State" });

            migrationBuilder.CreateIndex(
                name: "UX_Gift_LinkToken",
                schema: "app",
                table: "Gift",
                column: "LinkToken",
                unique: true,
                filter: "[LinkToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Gift_Source",
                schema: "app",
                table: "Gift",
                columns: new[] { "TenantId", "Kind", "UserName", "PersonId", "FamilyTeamId", "SourceKey" },
                unique: true,
                filter: "[UserName] IS NOT NULL AND [PersonId] IS NOT NULL AND [FamilyTeamId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GiftItem_Status_Price",
                schema: "app",
                table: "GiftItem",
                columns: new[] { "Status", "Price" });

            migrationBuilder.CreateIndex(
                name: "IX_GiftItem_TenantId_Status",
                schema: "app",
                table: "GiftItem",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_GiftItemHidden_GiftItemId",
                schema: "app",
                table: "GiftItemHidden",
                column: "GiftItemId");

            migrationBuilder.CreateIndex(
                name: "IX_GiftLog_TenantId_GiftId_AtUtc",
                schema: "app",
                table: "GiftLog",
                columns: new[] { "TenantId", "GiftId", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GiftPointUse_TenantId_GiftId",
                schema: "app",
                table: "GiftPointUse",
                columns: new[] { "TenantId", "GiftId" });

            migrationBuilder.CreateIndex(
                name: "IX_GiftPointUse_TenantId_UserName_Year",
                schema: "app",
                table: "GiftPointUse",
                columns: new[] { "TenantId", "UserName", "Year" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamAwardArchive_TenantId_Year",
                schema: "app",
                table: "TeamAwardArchive",
                columns: new[] { "TenantId", "Year" });

            // 선물(ADR 0094) 부서 격리: 상품은 공통 · 부서(콘텐츠와 같다), 나머지는 부서. EXEC: 표가 생기기 전에 묶음을 검사하지 않게(실DB 33268)
            migrationBuilder.Sql("EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_SharedRead(TenantId) ON app.GiftItem, ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.GiftItem AFTER INSERT, ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.GiftItem AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.GiftItemHidden, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftItemHidden AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftItemHidden AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.GiftSetting, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftSetting AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftSetting AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.GiftCreditRate, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftCreditRate AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftCreditRate AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.GiftReadingAmount, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftReadingAmount AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftReadingAmount AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.Gift, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Gift AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Gift AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.GiftLog, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftLog AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftLog AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.GiftPointUse, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftPointUse AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftPointUse AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.TeamAwardArchive, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.TeamAwardArchive AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.TeamAwardArchive AFTER UPDATE;')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation DROP FILTER PREDICATE ON app.GiftItem, DROP BLOCK PREDICATE ON app.GiftItem AFTER INSERT, DROP BLOCK PREDICATE ON app.GiftItem AFTER UPDATE, DROP FILTER PREDICATE ON app.GiftItemHidden, DROP BLOCK PREDICATE ON app.GiftItemHidden AFTER INSERT, DROP BLOCK PREDICATE ON app.GiftItemHidden AFTER UPDATE, DROP FILTER PREDICATE ON app.GiftSetting, DROP BLOCK PREDICATE ON app.GiftSetting AFTER INSERT, DROP BLOCK PREDICATE ON app.GiftSetting AFTER UPDATE, DROP FILTER PREDICATE ON app.GiftCreditRate, DROP BLOCK PREDICATE ON app.GiftCreditRate AFTER INSERT, DROP BLOCK PREDICATE ON app.GiftCreditRate AFTER UPDATE, DROP FILTER PREDICATE ON app.GiftReadingAmount, DROP BLOCK PREDICATE ON app.GiftReadingAmount AFTER INSERT, DROP BLOCK PREDICATE ON app.GiftReadingAmount AFTER UPDATE, DROP FILTER PREDICATE ON app.Gift, DROP BLOCK PREDICATE ON app.Gift AFTER INSERT, DROP BLOCK PREDICATE ON app.Gift AFTER UPDATE, DROP FILTER PREDICATE ON app.GiftLog, DROP BLOCK PREDICATE ON app.GiftLog AFTER INSERT, DROP BLOCK PREDICATE ON app.GiftLog AFTER UPDATE, DROP FILTER PREDICATE ON app.GiftPointUse, DROP BLOCK PREDICATE ON app.GiftPointUse AFTER INSERT, DROP BLOCK PREDICATE ON app.GiftPointUse AFTER UPDATE, DROP FILTER PREDICATE ON app.TeamAwardArchive, DROP BLOCK PREDICATE ON app.TeamAwardArchive AFTER INSERT, DROP BLOCK PREDICATE ON app.TeamAwardArchive AFTER UPDATE;");
            migrationBuilder.DropTable(
                name: "GiftCreditRate",
                schema: "app");

            migrationBuilder.DropTable(
                name: "GiftItemHidden",
                schema: "app");

            migrationBuilder.DropTable(
                name: "GiftLog",
                schema: "app");

            migrationBuilder.DropTable(
                name: "GiftPointUse",
                schema: "app");

            migrationBuilder.DropTable(
                name: "GiftReadingAmount",
                schema: "app");

            migrationBuilder.DropTable(
                name: "GiftSetting",
                schema: "app");

            migrationBuilder.DropTable(
                name: "GiftYearClose",
                schema: "app");

            migrationBuilder.DropTable(
                name: "TeamAwardArchive",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Gift",
                schema: "app");

            migrationBuilder.DropTable(
                name: "GiftItem",
                schema: "app");
        }
    }
}
