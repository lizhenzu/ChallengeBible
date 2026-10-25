using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OnFamily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Family",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedByAccountId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Family", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Family_CreatedBy",
                        column: x => x.CreatedByAccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FamilyLink",
                schema: "app",
                columns: table => new
                {
                    FamilyId = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    Relation = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    SortOrder = table.Column<byte>(type: "tinyint", nullable: false),
                    AddedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    AddedByAccountId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyLink", x => new { x.FamilyId, x.AccountId });
                    table.CheckConstraint("CK_FamilyLink_Relation", "Relation IN ('Student','Mother','Father','Grandmother','Grandfather','Other')");
                    table.ForeignKey(
                        name: "FK_FamilyLink_Account",
                        column: x => x.AccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FamilyLink_AddedBy",
                        column: x => x.AddedByAccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FamilyLink_Family",
                        column: x => x.FamilyId,
                        principalSchema: "app",
                        principalTable: "Family",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FamilyRequest",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequesterAccountId = table.Column<int>(type: "int", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    RequestedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    DecidedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DecidedByAccountId = table.Column<int>(type: "int", nullable: true),
                    FamilyId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyRequest", x => x.Id);
                    table.CheckConstraint("CK_FamilyRequest_Status", "Status IN ('Requested','Approved','Rejected')");
                    table.ForeignKey(
                        name: "FK_FamilyRequest_DecidedBy",
                        column: x => x.DecidedByAccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FamilyRequest_Family",
                        column: x => x.FamilyId,
                        principalSchema: "app",
                        principalTable: "Family",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FamilyRequest_Requester",
                        column: x => x.RequesterAccountId,
                        principalSchema: "app",
                        principalTable: "Account",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Family_CreatedByAccountId",
                schema: "app",
                table: "Family",
                column: "CreatedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyLink_AddedByAccountId",
                schema: "app",
                table: "FamilyLink",
                column: "AddedByAccountId");

            migrationBuilder.CreateIndex(
                name: "UQ_FamilyLink_Account",
                schema: "app",
                table: "FamilyLink",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyRequest_DecidedByAccountId",
                schema: "app",
                table: "FamilyRequest",
                column: "DecidedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyRequest_FamilyId",
                schema: "app",
                table: "FamilyRequest",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyRequest_Status",
                schema: "app",
                table: "FamilyRequest",
                columns: new[] { "Status", "RequestedUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_FamilyRequest_Open",
                schema: "app",
                table: "FamilyRequest",
                column: "RequesterAccountId",
                unique: true,
                filter: "[Status] = 'Requested'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FamilyLink",
                schema: "app");

            migrationBuilder.DropTable(
                name: "FamilyRequest",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Family",
                schema: "app");
        }
    }
}
