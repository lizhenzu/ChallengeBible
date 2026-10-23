using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PrayerDayNo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_DailyPrayer_Date",
                schema: "app",
                table: "DailyPrayer");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "ExposeDate",
                schema: "app",
                table: "DailyPrayer",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AddColumn<byte>(
                name: "DayNo",
                schema: "app",
                table: "DailyPrayer",
                type: "tinyint",
                nullable: true);

            // 0053: 기존 공용 행은 DayNo = 그 날짜의 일로 옮기고 날짜를 비운다.
            // 같은 번호의 현재 개정이 여럿이면 가장 늦은 날짜 것만 남기고 보관한다(기록이 가리키므로 지우지 않음).
            // 공용 쓰기는 시스템 범위만 통과하므로(RLS) PlatformScope 를 켜고, 스크립트 한 묶음에서도 새 열을 찾도록 EXEC 로 돌린다.
            migrationBuilder.Sql("""
                EXEC sp_set_session_context N'PlatformScope', 1;
                EXEC(N'
                UPDATE app.DailyPrayer SET DayNo = DAY(ExposeDate) WHERE TenantId IS NULL;

                WITH d AS (
                    SELECT IsCurrent, Status, ROW_NUMBER() OVER (PARTITION BY DayNo ORDER BY ExposeDate DESC) AS n
                    FROM app.DailyPrayer WHERE TenantId IS NULL AND IsCurrent = 1)
                UPDATE d SET IsCurrent = 0, Status = ''Archived'' WHERE n > 1;

                UPDATE app.DailyPrayer SET ExposeDate = NULL WHERE TenantId IS NULL;');
                """);

            migrationBuilder.CreateIndex(
                name: "UX_DailyPrayer_Date",
                schema: "app",
                table: "DailyPrayer",
                columns: new[] { "TenantId", "ExposeDate" },
                unique: true,
                filter: "[IsCurrent] = 1 AND [ExposeDate] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_DailyPrayer_Day",
                schema: "app",
                table: "DailyPrayer",
                column: "DayNo",
                unique: true,
                filter: "[IsCurrent] = 1 AND [TenantId] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DailyPrayer_Slot",
                schema: "app",
                table: "DailyPrayer",
                sql: "(TenantId IS NULL AND DayNo BETWEEN 1 AND 31 AND ExposeDate IS NULL)\nOR (TenantId IS NOT NULL AND DayNo IS NULL AND ExposeDate IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_DailyPrayer_Date",
                schema: "app",
                table: "DailyPrayer");

            migrationBuilder.DropIndex(
                name: "UX_DailyPrayer_Day",
                schema: "app",
                table: "DailyPrayer");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DailyPrayer_Slot",
                schema: "app",
                table: "DailyPrayer");

            migrationBuilder.DropColumn(
                name: "DayNo",
                schema: "app",
                table: "DailyPrayer");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "ExposeDate",
                schema: "app",
                table: "DailyPrayer",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_DailyPrayer_Date",
                schema: "app",
                table: "DailyPrayer",
                columns: new[] { "TenantId", "ExposeDate" },
                unique: true,
                filter: "[IsCurrent] = 1");
        }
    }
}
