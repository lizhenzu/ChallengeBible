using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <summary>
    /// 유년부와 공유 표로 옮기기(ADR 0090). 표 교체 · 데이터 변환 · RLS 교체가 EF 연산으로 나타나지 않아
    /// 실DB 는 tools/sql/migrate-shared-tables.sql 로 옮긴다. 여기서는 모델 스냅샷만 앞으로 간다.
    /// </summary>
    public partial class SharedTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) { }

        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("옛 표로 되돌릴 수 없어요(ADR 0090).");
    }
}
