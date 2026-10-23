using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "Tenant",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Slug = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    JoinCode = table.Column<string>(type: "char(6)", unicode: false, fixedLength: true, maxLength: 6, nullable: false),
                    TimeZoneId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ThemeColor = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    AppName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    AppShortName = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    MissionEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ReadingEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ReadingBoardPublic = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenant", x => x.Id);
                    table.CheckConstraint("CK_Tenant_JoinCode", "JoinCode NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN");
                    table.CheckConstraint("CK_Tenant_Program", "MissionEnabled = 1 OR ReadingEnabled = 1");
                    table.CheckConstraint("CK_Tenant_Slug", "LEN(Slug) >= 2 AND Slug NOT LIKE '%[^a-z0-9-]%'");
                    table.CheckConstraint("CK_Tenant_Status", "Status IN ('Active','Suspended')");
                });

            migrationBuilder.CreateTable(
                name: "Account",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoginId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PhotoPath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    IsPlatformAdmin = table.Column<bool>(type: "bit", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "bit", nullable: false),
                    FamilyTenantId = table.Column<int>(type: "int", nullable: true),
                    LastTenantId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Account", x => x.Id);
                    table.CheckConstraint("CK_Account_Status", "Status IN ('Active','Disabled')");
                    table.ForeignKey(
                        name: "FK_Account_FamilyTenant",
                        column: x => x.FamilyTenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Account_LastTenant",
                        column: x => x.LastTenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Membership",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    GroupLabel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RequestedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    DecidedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DecidedByAccountId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Membership", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.UniqueConstraint("UQ_Membership_TenantId_Id", x => new { x.TenantId, x.Id })
                        .Annotation("SqlServer:Clustered", true);
                    table.CheckConstraint("CK_Membership_Status", "Status IN ('Requested','Approved','Suspended','Released','Rejected','PromotionPending')");
                    table.ForeignKey(
                        name: "FK_Membership_Account",
                        column: x => x.AccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Membership_DecidedBy",
                        column: x => x.DecidedByAccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Membership_Tenant",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TenantSeason",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<short>(type: "smallint", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ChangedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ChangedByAccountId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSeason", x => new { x.TenantId, x.Year });
                    table.CheckConstraint("CK_TenantSeason_StartInYear", "YEAR(StartDate) = [Year]");
                    table.ForeignKey(
                        name: "FK_TenantSeason_Account",
                        column: x => x.ChangedByAccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TenantSeason_Tenant",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MembershipRole",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    MembershipId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipRole", x => new { x.TenantId, x.MembershipId, x.Role });
                    table.CheckConstraint("CK_MembershipRole_Role", "Role IN ('Participant','Guardian','Teacher','TenantAdmin')");
                    table.ForeignKey(
                        name: "FK_MembershipRole_Membership",
                        columns: x => new { x.TenantId, x.MembershipId },
                        principalSchema: "app",
                        principalTable: "Membership",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Account_FamilyTenantId",
                schema: "app",
                table: "Account",
                column: "FamilyTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Account_LastTenantId",
                schema: "app",
                table: "Account",
                column: "LastTenantId");

            migrationBuilder.CreateIndex(
                name: "UQ_Account_LoginId",
                schema: "app",
                table: "Account",
                column: "LoginId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Membership_Account",
                schema: "app",
                table: "Membership",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Membership_DecidedByAccountId",
                schema: "app",
                table: "Membership",
                column: "DecidedByAccountId");

            migrationBuilder.CreateIndex(
                name: "UQ_Membership_TenantAccount",
                schema: "app",
                table: "Membership",
                columns: new[] { "TenantId", "AccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Tenant_JoinCode",
                schema: "app",
                table: "Tenant",
                column: "JoinCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Tenant_Slug",
                schema: "app",
                table: "Tenant",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantSeason_ChangedByAccountId",
                schema: "app",
                table: "TenantSeason",
                column: "ChangedByAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MembershipRole",
                schema: "app");

            migrationBuilder.DropTable(
                name: "TenantSeason",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Membership",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Account",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Tenant",
                schema: "app");
        }
    }
}
