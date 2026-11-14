using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GiftNumbers5000 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 새 상품 번호는 5000번부터(2026-11-14 사용자): 유년부 상품(1000번대)은 옮길 때 원래 번호로 들어가 겹치지 않는다.
            // 이미 5000 이상이면 그대로. 한 번도 넣지 않은 표면 다음 번호 = 다시 정한 값, 넣은 적 있는 표면 = 다시 정한 값 + 1.
            migrationBuilder.Sql(@"
DECLARE @cur numeric(38, 0) = IDENT_CURRENT('app.GiftItem');
DECLARE @used bit = CASE WHEN EXISTS (SELECT 1 FROM sys.identity_columns WHERE object_id = OBJECT_ID('app.GiftItem') AND last_value IS NOT NULL) THEN 1 ELSE 0 END;
IF @cur < 5000 AND (SELECT ISNULL(MAX(Id), 0) FROM app.GiftItem) < 5000
BEGIN
    IF @used = 1 DBCC CHECKIDENT ('app.GiftItem', RESEED, 4999) WITH NO_INFOMSGS;
    ELSE DBCC CHECKIDENT ('app.GiftItem', RESEED, 5000) WITH NO_INFOMSGS;
END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 되돌리기는 번호 줄을 건드리지 않는다(이미 5000번대로 만든 상품이 있을 수 있다).
        }
    }
}
