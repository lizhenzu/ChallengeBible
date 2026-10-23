-- =====================================================================
-- 새 DB 점검 (물리 설계 6절) — 아무것도 만들거나 바꾸지 않는 SELECT 한 줄
-- SmarterASP SQL Studio 웹 화면은 EXEC · sp_… 실행을 막으므로 권한만 묻는다.
-- 기대값: Ver 15 이상, Coll 에 _CI_, Compat 130 이상, 나머지 1
-- 2026-10-23 결과: 16, SQL_Latin1_General_CP1_CI_AS, 160, 1, 1, 1, 1
-- =====================================================================
SELECT
  SERVERPROPERTY('ProductMajorVersion') AS Ver,
  DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS Coll,
  (SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()) AS Compat,
  IS_ROLEMEMBER('db_owner') AS IsOwner,
  HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER ANY SECURITY POLICY') AS CanRls,
  HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE FUNCTION') AS CanFunc,
  HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE SCHEMA') AS CanSchema;
