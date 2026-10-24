using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class JoinListed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "JoinListed",
                schema: "app",
                table: "Tenant",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JoinListed",
                schema: "app",
                table: "Tenant");
        }
    }
}
