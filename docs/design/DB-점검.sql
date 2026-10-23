-- =====================================================================
-- 새 DB 점검 (물리 설계 6절 "남은 확인") — 빈 DB 에서 한 번 실행
-- 만든 시험 개체는 끝에서 모두 지운다. 업무 데이터는 건드리지 않는다.
-- 결과 표 하나에 항목마다 OK / 실패 가 나온다.
-- =====================================================================
SET NOCOUNT ON;
DECLARE @r TABLE (No int, Item nvarchar(100), Result nvarchar(400));

-- 1. 버전 (2019 = 15, 2022 = 16)
INSERT @r SELECT 1, N'SQL Server 버전', CAST(SERVERPROPERTY('ProductMajorVersion') AS nvarchar(10)) + N' (' + CAST(SERVERPROPERTY('Edition') AS nvarchar(100)) + N')';

-- 2. DB 정렬 — 이름에 _CI_ 가 있으면 아이디 대소문자 구분 안 함(원하는 값)
INSERT @r SELECT 2, N'DB 정렬(collation)', CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(100));

-- 3. 호환성 수준 (130 이상이면 RLS 가능)
INSERT @r SELECT 3, N'호환성 수준', CAST(compatibility_level AS nvarchar(10)) FROM sys.databases WHERE name = DB_NAME();

-- 4. 세션 값(읽기 전용) — 영역 격리의 바탕
BEGIN TRY
    EXEC sp_set_session_context @key = N'ChkTenant', @value = 7, @read_only = 1;
    INSERT @r SELECT 4, N'sp_set_session_context', N'OK (값 ' + CAST(SESSION_CONTEXT(N'ChkTenant') AS nvarchar(10)) + N')';
END TRY
BEGIN CATCH
    INSERT @r SELECT 4, N'sp_set_session_context', N'실패: ' + ERROR_MESSAGE();
END CATCH;

-- 5. 스키마 · 함수 · 보안 정책(RLS) 만들기 권한
BEGIN TRY
    EXEC (N'CREATE SCHEMA chk;');
    EXEC (N'CREATE TABLE chk.T (TenantId int NOT NULL, V int NOT NULL);');
    EXEC (N'CREATE FUNCTION chk.fn_T(@TenantId int) RETURNS TABLE WITH SCHEMABINDING AS
            RETURN SELECT 1 AS ok WHERE @TenantId = CAST(SESSION_CONTEXT(N''ChkTenant'') AS int);');
    EXEC (N'CREATE SECURITY POLICY chk.P ADD FILTER PREDICATE chk.fn_T(TenantId) ON chk.T WITH (STATE = ON);');
    EXEC (N'INSERT chk.T VALUES (7, 1), (8, 2);');
    DECLARE @seen int;
    EXEC sp_executesql N'SELECT @n = COUNT(*) FROM chk.T;', N'@n int OUTPUT', @n = @seen OUTPUT;
    INSERT @r SELECT 5, N'RLS(보안 정책) 만들기 · 걸러 보기',
        CASE WHEN @seen = 1 THEN N'OK (2행 중 내 영역 1행만 보임)' ELSE N'이상: ' + CAST(@seen AS nvarchar(10)) + N'행 보임' END;
END TRY
BEGIN CATCH
    INSERT @r SELECT 5, N'RLS(보안 정책) 만들기', N'실패: ' + ERROR_MESSAGE();
END CATCH;

-- 6. 트리거 만들기 권한 (암송 기간 겹침 검사에 씀)
BEGIN TRY
    EXEC (N'CREATE TRIGGER chk.trg_T ON chk.T AFTER INSERT AS BEGIN SET NOCOUNT ON; END;');
    INSERT @r SELECT 6, N'트리거 만들기', N'OK';
END TRY
BEGIN CATCH
    INSERT @r SELECT 6, N'트리거 만들기', N'실패: ' + ERROR_MESSAGE();
END CATCH;

-- 정리
BEGIN TRY
    IF OBJECT_ID(N'chk.P', N'SP') IS NOT NULL EXEC (N'DROP SECURITY POLICY chk.P;');
    IF OBJECT_ID(N'chk.trg_T', N'TR') IS NOT NULL EXEC (N'DROP TRIGGER chk.trg_T;');
    IF OBJECT_ID(N'chk.T', N'U') IS NOT NULL EXEC (N'DROP TABLE chk.T;');
    IF OBJECT_ID(N'chk.fn_T', N'IF') IS NOT NULL EXEC (N'DROP FUNCTION chk.fn_T;');
    IF SCHEMA_ID(N'chk') IS NOT NULL EXEC (N'DROP SCHEMA chk;');
    INSERT @r SELECT 7, N'시험 개체 지우기', N'OK';
END TRY
BEGIN CATCH
    INSERT @r SELECT 7, N'시험 개체 지우기', N'실패: ' + ERROR_MESSAGE();
END CATCH;

SELECT No, Item, Result FROM @r ORDER BY No;
