using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <summary>
    /// 통독 참여자 현황(R-159, 0041): 통독 표의 읽기 조건을 본인만(fn_Account)에서 fn_ReadingRead 로 넓힌다(물리 설계 그대로).
    /// 본인 · 시스템 연결, 지금 부서의 관리자 · 교사가 그 부서 소속원을, 부서가 공개하면 같은 부서 승인 소속원끼리 본다.
    /// 쓰기 조건(BLOCK)은 그대로 본인만이다.
    /// </summary>
    public partial class ReadingBoard : Migration
    {
        private static readonly string[] Tables = ["ReadingRound", "ChapterRead", "RoundVerseRead", "ChapterProgress"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION sec.fn_ReadingRead (@AccountId int)
                RETURNS TABLE WITH SCHEMABINDING
                AS RETURN
                    SELECT 1 AS Allowed
                    WHERE @AccountId = CAST(SESSION_CONTEXT(N'AccountId') AS int)
                       OR CAST(SESSION_CONTEXT(N'PlatformScope') AS int) = 1
                       OR (
                            EXISTS (SELECT 1 FROM app.Membership m
                                    WHERE m.TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
                                      AND m.AccountId = @AccountId
                                      AND m.Status IN ('Approved', 'Suspended'))
                        AND EXISTS (SELECT 1 FROM app.Membership a
                                    JOIN app.MembershipRole r ON r.TenantId = a.TenantId AND r.MembershipId = a.Id
                                    WHERE a.TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
                                      AND a.AccountId = CAST(SESSION_CONTEXT(N'AccountId') AS int)
                                      AND a.Status = 'Approved'
                                      AND r.Role IN ('TenantAdmin', 'Teacher'))
                       )
                       OR (
                            EXISTS (SELECT 1 FROM app.Tenant t
                                    WHERE t.Id = CAST(SESSION_CONTEXT(N'TenantId') AS int) AND t.ReadingBoardPublic = 1)
                        AND EXISTS (SELECT 1 FROM app.Membership m
                                    WHERE m.TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
                                      AND m.AccountId = @AccountId AND m.Status = 'Approved')
                        AND EXISTS (SELECT 1 FROM app.Membership s
                                    WHERE s.TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
                                      AND s.AccountId = CAST(SESSION_CONTEXT(N'AccountId') AS int) AND s.Status = 'Approved')
                       );
                """);
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation " +
                string.Join(", ", Tables.Select(t => $"ALTER FILTER PREDICATE sec.fn_ReadingRead(AccountId) ON app.{t}")) + ";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation " +
                string.Join(", ", Tables.Select(t => $"ALTER FILTER PREDICATE sec.fn_Account(AccountId) ON app.{t}")) + ";");
            migrationBuilder.Sql("DROP FUNCTION sec.fn_ReadingRead;");
        }
    }
}
