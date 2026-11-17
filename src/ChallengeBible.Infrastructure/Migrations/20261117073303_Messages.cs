using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Messages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Message",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    SenderUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SenderTenantId = table.Column<int>(type: "int", nullable: true),
                    SenderLabel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RecipientUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SentUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ReplyToId = table.Column<long>(type: "bigint", nullable: true),
                    ReadUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    SenderDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RecipientDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CanceledUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Message", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MessageReceipt",
                schema: "app",
                columns: table => new
                {
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ReadUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    DeletedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageReceipt", x => new { x.MessageId, x.UserName });
                    table.ForeignKey(
                        name: "FK_MessageReceipt_Message_MessageId",
                        column: x => x.MessageId,
                        principalSchema: "app",
                        principalTable: "Message",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MessageTarget",
                schema: "app",
                columns: table => new
                {
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageTarget", x => new { x.MessageId, x.TenantId });
                    table.ForeignKey(
                        name: "FK_MessageTarget_Message_MessageId",
                        column: x => x.MessageId,
                        principalSchema: "app",
                        principalTable: "Message",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MessageTarget_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Message_Kind_SentUtc",
                schema: "app",
                table: "Message",
                columns: new[] { "Kind", "SentUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Message_RecipientUserName_SentUtc",
                schema: "app",
                table: "Message",
                columns: new[] { "RecipientUserName", "SentUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Message_SenderUserName_SentUtc",
                schema: "app",
                table: "Message",
                columns: new[] { "SenderUserName", "SentUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageReceipt_UserName",
                schema: "app",
                table: "MessageReceipt",
                column: "UserName");

            migrationBuilder.CreateIndex(
                name: "IX_MessageTarget_TenantId",
                schema: "app",
                table: "MessageTarget",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessageReceipt",
                schema: "app");

            migrationBuilder.DropTable(
                name: "MessageTarget",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Message",
                schema: "app");
        }
    }
}
