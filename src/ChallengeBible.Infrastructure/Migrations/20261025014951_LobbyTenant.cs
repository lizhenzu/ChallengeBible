using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LobbyTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLobby",
                schema: "app",
                table: "Tenant",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "UX_Tenant_Lobby",
                schema: "app",
                table: "Tenant",
                column: "IsLobby",
                unique: true,
                filter: "[IsLobby] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Tenant_Lobby",
                schema: "app",
                table: "Tenant");

            migrationBuilder.DropColumn(
                name: "IsLobby",
                schema: "app",
                table: "Tenant");
        }
    }
}
