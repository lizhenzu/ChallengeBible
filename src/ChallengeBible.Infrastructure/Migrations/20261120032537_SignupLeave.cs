using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SignupLeave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "BirthDay",
                schema: "app",
                table: "UserSetting",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "BirthMonth",
                schema: "app",
                table: "UserSetting",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LeftOn",
                schema: "app",
                table: "Person",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BirthDay",
                schema: "app",
                table: "UserSetting");

            migrationBuilder.DropColumn(
                name: "BirthMonth",
                schema: "app",
                table: "UserSetting");

            migrationBuilder.DropColumn(
                name: "LeftOn",
                schema: "app",
                table: "Person");
        }
    }
}
