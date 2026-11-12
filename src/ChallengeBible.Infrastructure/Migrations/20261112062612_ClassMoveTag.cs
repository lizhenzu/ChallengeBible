using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClassMoveTag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PrevClassName",
                schema: "app",
                table: "Person",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrevDeptName",
                schema: "app",
                table: "Person",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrevTeacherName",
                schema: "app",
                table: "Person",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrevTeacherPhone",
                schema: "app",
                table: "Person",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrevClassName",
                schema: "app",
                table: "Person");

            migrationBuilder.DropColumn(
                name: "PrevDeptName",
                schema: "app",
                table: "Person");

            migrationBuilder.DropColumn(
                name: "PrevTeacherName",
                schema: "app",
                table: "Person");

            migrationBuilder.DropColumn(
                name: "PrevTeacherPhone",
                schema: "app",
                table: "Person");
        }
    }
}
