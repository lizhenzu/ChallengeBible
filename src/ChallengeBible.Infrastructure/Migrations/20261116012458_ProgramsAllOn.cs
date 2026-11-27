using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProgramsAllOn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 2026-11-16 사용자: 모든 부서에서 성경말씀 · 성경통독 · 성경필사를 켠다(끄는 칸은 플랫폼 › 부서에 그대로 둔다). 데이터만 바꾼다.
            migrationBuilder.Sql("UPDATE app.Tenant SET MissionEnabled = 1, ReadingEnabled = 1, CopyEnabled = 1;");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
