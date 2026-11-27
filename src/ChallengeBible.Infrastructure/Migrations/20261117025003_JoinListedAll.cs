using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class JoinListedAll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 모든 부서를 참여 신청 목록에 보이게(2026-11-17 사용자). 임시 부서는 목록 대상이 아니라 그대로
            migrationBuilder.Sql("UPDATE app.Tenant SET JoinListed = 1 WHERE IsLobby = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 되돌리지 않는다(어느 부서가 꺼져 있었는지 알 수 없음)
        }
    }
}
