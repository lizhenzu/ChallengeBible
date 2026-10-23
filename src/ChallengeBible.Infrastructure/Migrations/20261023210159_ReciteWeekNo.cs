using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReciteWeekNo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_WeeklyVerse_Start",
                schema: "app",
                table: "WeeklyVerse");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WeeklyVerse_Period",
                schema: "app",
                table: "WeeklyVerse");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "StartDate",
                schema: "app",
                table: "WeeklyVerse",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "EndDate",
                schema: "app",
                table: "WeeklyVerse",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AddColumn<byte>(
                name: "WeekNo",
                schema: "app",
                table: "WeeklyVerse",
                type: "tinyint",
                nullable: true);

            // 0052: 기존 공용 행은 시작일로 주 번호를 계산해 옮기고 날짜를 비운다.
            // 같은 주 번호의 현재 개정이 여럿이면 가장 늦은 해 것만 남기고 보관한다(기록이 가리키므로 지우지 않음).
            // 공용 쓰기는 시스템 범위만 통과하므로(RLS) 이 연결에 PlatformScope 를 켠다.
            // 스크립트(migrations script)로 한 묶음에 돌려도 새 열 WeekNo 를 찾도록 EXEC 로 따로 컴파일한다.
            migrationBuilder.Sql("""
                EXEC sp_set_session_context N'PlatformScope', 1;
                EXEC(N'
                UPDATE v SET WeekNo = DATEDIFF(DAY, f.FirstSunday, v.StartDate) / 7 + 1
                FROM app.WeeklyVerse v
                CROSS APPLY (SELECT DATEFROMPARTS(YEAR(v.StartDate), 1, 1) AS Jan1) j
                CROSS APPLY (SELECT DATEADD(DAY, (7 - DATEDIFF(DAY, ''19000107'', j.Jan1) % 7) % 7, j.Jan1) AS FirstSunday) f
                WHERE v.TenantId IS NULL;

                WITH d AS (
                    SELECT IsCurrent, Status, ROW_NUMBER() OVER (PARTITION BY WeekNo ORDER BY StartDate DESC) AS n
                    FROM app.WeeklyVerse WHERE TenantId IS NULL AND IsCurrent = 1)
                UPDATE d SET IsCurrent = 0, Status = ''Archived'' WHERE n > 1;

                UPDATE app.WeeklyVerse SET StartDate = NULL, EndDate = NULL WHERE TenantId IS NULL;');
                """);

            migrationBuilder.CreateIndex(
                name: "UX_WeeklyVerse_Start",
                schema: "app",
                table: "WeeklyVerse",
                columns: new[] { "TenantId", "StartDate" },
                unique: true,
                filter: "[IsCurrent] = 1 AND [StartDate] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_WeeklyVerse_Week",
                schema: "app",
                table: "WeeklyVerse",
                column: "WeekNo",
                unique: true,
                filter: "[IsCurrent] = 1 AND [TenantId] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WeeklyVerse_Slot",
                schema: "app",
                table: "WeeklyVerse",
                sql: "(TenantId IS NULL AND WeekNo BETWEEN 1 AND 55 AND StartDate IS NULL AND EndDate IS NULL)\nOR (TenantId IS NOT NULL AND WeekNo IS NULL AND DATEDIFF(DAY, '19000107', StartDate) % 7 = 0 AND EndDate = DATEADD(DAY, 6, StartDate))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_WeeklyVerse_Start",
                schema: "app",
                table: "WeeklyVerse");

            migrationBuilder.DropIndex(
                name: "UX_WeeklyVerse_Week",
                schema: "app",
                table: "WeeklyVerse");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WeeklyVerse_Slot",
                schema: "app",
                table: "WeeklyVerse");

            migrationBuilder.DropColumn(
                name: "WeekNo",
                schema: "app",
                table: "WeeklyVerse");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "StartDate",
                schema: "app",
                table: "WeeklyVerse",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "EndDate",
                schema: "app",
                table: "WeeklyVerse",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_WeeklyVerse_Start",
                schema: "app",
                table: "WeeklyVerse",
                columns: new[] { "TenantId", "StartDate" },
                unique: true,
                filter: "[IsCurrent] = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WeeklyVerse_Period",
                schema: "app",
                table: "WeeklyVerse",
                sql: "EndDate >= StartDate");
        }
    }
}
