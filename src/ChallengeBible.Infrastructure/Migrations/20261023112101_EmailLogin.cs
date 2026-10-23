using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <summary>
    /// 로그인 키를 아이디(LoginId)에서 이메일(Email)로 바꾼다(ADR 0050).
    /// 열을 지우지 않고 이름만 바꿔 기존 계정을 보존한다. 형식이 이메일이 아닌 옛 아이디는 그대로 남으므로
    /// 계정이 이미 있으면 이메일로 직접 고쳐야 로그인할 수 있다.
    /// </summary>
    public partial class EmailLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "UQ_Account_LoginId", schema: "app", table: "Account");

            migrationBuilder.RenameColumn(name: "LoginId", schema: "app", table: "Account", newName: "Email");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                schema: "app",
                table: "Account",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.CreateIndex(name: "UQ_Account_Email", schema: "app", table: "Account", column: "Email", unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "UQ_Account_Email", schema: "app", table: "Account");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                schema: "app",
                table: "Account",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(254)",
                oldMaxLength: 254);

            migrationBuilder.RenameColumn(name: "Email", schema: "app", table: "Account", newName: "LoginId");

            migrationBuilder.CreateIndex(name: "UQ_Account_LoginId", schema: "app", table: "Account", column: "LoginId", unique: true);
        }
    }
}
