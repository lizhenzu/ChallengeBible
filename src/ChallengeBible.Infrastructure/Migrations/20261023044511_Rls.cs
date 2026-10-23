using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <summary>
    /// 영역 격리 RLS(물리 설계 3-2). 읽기는 FILTER, 쓰기는 BLOCK(AFTER INSERT · AFTER UPDATE).
    /// 세션 값은 SessionContextInterceptor 가 연결마다 읽기 전용으로 넣는다.
    /// 표가 늘면 이 정책에 표를 더하는 마이그레이션을 새로 만든다.
    /// </summary>
    public partial class Rls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SCHEMA sec;");

            // 세션 TenantId 와 같거나 시스템 연결(PlatformScope)이면 통과
            migrationBuilder.Sql("""
                CREATE FUNCTION sec.fn_Tenant(@TenantId int)
                RETURNS TABLE WITH SCHEMABINDING AS
                RETURN SELECT 1 AS ok
                WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
                   OR CAST(SESSION_CONTEXT(N'PlatformScope') AS int) = 1;
                """);

            // 위 조건 또는 내 계정의 소속(R-135 "내 소속 영역 목록")
            migrationBuilder.Sql("""
                CREATE FUNCTION sec.fn_Membership(@TenantId int, @AccountId int)
                RETURNS TABLE WITH SCHEMABINDING AS
                RETURN SELECT 1 AS ok
                WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
                   OR @AccountId = CAST(SESSION_CONTEXT(N'AccountId') AS int)
                   OR CAST(SESSION_CONTEXT(N'PlatformScope') AS int) = 1;
                """);

            migrationBuilder.Sql("""
                CREATE SECURITY POLICY sec.TenantIsolation
                    ADD FILTER PREDICATE sec.fn_Membership(TenantId, AccountId) ON app.Membership,
                    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.Membership AFTER INSERT,
                    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.Membership AFTER UPDATE,
                    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.MembershipRole,
                    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.MembershipRole AFTER INSERT,
                    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.MembershipRole AFTER UPDATE,
                    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.TenantSeason,
                    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.TenantSeason AFTER INSERT,
                    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.TenantSeason AFTER UPDATE
                WITH (STATE = ON);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SECURITY POLICY sec.TenantIsolation;");
            migrationBuilder.Sql("DROP FUNCTION sec.fn_Membership;");
            migrationBuilder.Sql("DROP FUNCTION sec.fn_Tenant;");
            migrationBuilder.Sql("DROP SCHEMA sec;");
        }
    }
}
