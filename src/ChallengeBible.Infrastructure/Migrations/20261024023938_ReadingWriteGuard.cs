using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <summary>
    /// 참여자 현황으로 남의 통독 기록이 "보이게" 되면서(fn_ReadingRead) 남의 기록을 고치거나 지울 수도 있게 됐다.
    /// 읽기 조건이 본인만이던 때는 FILTER 가 막아 주던 것 — 이제 BLOCK BEFORE UPDATE · BEFORE DELETE 를 본인만(fn_Account)으로 따로 둔다.
    /// </summary>
    public partial class ReadingWriteGuard : Migration
    {
        private static readonly string[] Tables = ["ReadingRound", "ChapterRead", "RoundVerseRead", "ChapterProgress"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation " + string.Join(", ", Tables.SelectMany(t => new[]
            {
                $"ADD BLOCK PREDICATE sec.fn_Account(AccountId) ON app.{t} BEFORE UPDATE",
                $"ADD BLOCK PREDICATE sec.fn_Account(AccountId) ON app.{t} BEFORE DELETE",
            })) + ";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation " + string.Join(", ", Tables.SelectMany(t => new[]
            {
                $"DROP BLOCK PREDICATE ON app.{t} BEFORE UPDATE",
                $"DROP BLOCK PREDICATE ON app.{t} BEFORE DELETE",
            })) + ";");
        }
    }
}
