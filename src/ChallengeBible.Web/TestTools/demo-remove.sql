-- demo-remove.sql : 영상 촬영용 데모 영역(demo-school)과 데모 계정을 완전히 지운다 (ADR 0143, 2026-11-28).
-- 지우는 것: 슬러그 demo-school 부서의 모든 기록 + 부서 + demo_admin · demo_user · demo_s02~s10 · demo_parent 계정(@demo.invalid).
-- 건드리지 않는 것: 다른 모든 부서 · 플랫폼 관리자 · 다른 부서 관리자 계정(R-315).
-- 실행: SSMS 에서 통째로 실행. 처음에는 @Commit = 0 (미리보기, 마지막에 되돌림) → 확인 뒤 1.
SET NOCOUNT ON;
DECLARE @Commit bit = 0;
DECLARE @T int = (SELECT Id FROM dbo.Tenant WHERE Slug = N'demo-school');
IF @T IS NULL BEGIN PRINT N'데모 영역이 없습니다.'; END
ELSE
BEGIN
  BEGIN TRANSACTION;
  -- 1) TenantId 열이 있는 모든 표에서 데모 부서 줄을 지운다. 참조 순서를 몰라도 되게, 더 지울 것이 없을 때까지 반복(참조 오류는 건너뛰고 다음 바퀴에).
  DECLARE @pass int = 0, @left int = 1;
  WHILE @pass < 12 AND @left > 0
  BEGIN
    SET @pass += 1; SET @left = 0;
    DECLARE @sql nvarchar(max), @tbl nvarchar(300);
    DECLARE c CURSOR LOCAL FAST_FORWARD FOR
      SELECT QUOTENAME(s.name) + N'.' + QUOTENAME(t.name)
      FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id JOIN sys.columns col ON col.object_id = t.object_id AND col.name = N'TenantId'
      WHERE NOT (s.name = N'dbo' AND t.name = N'Tenant');
    OPEN c; FETCH NEXT FROM c INTO @tbl;
    WHILE @@FETCH_STATUS = 0
    BEGIN
      SET @sql = N'BEGIN TRY DELETE FROM ' + @tbl + N' WHERE TenantId = @T; END TRY BEGIN CATCH IF ERROR_NUMBER() = 547 SET @l = 1; ELSE THROW; END CATCH';
      DECLARE @l int = 0;
      EXEC sp_executesql @sql, N'@T int, @l int OUTPUT', @T = @T, @l = @l OUTPUT;
      IF @l = 1 SET @left = 1;
      FETCH NEXT FROM c INTO @tbl;
    END
    CLOSE c; DEALLOCATE c;
  END
  -- 2) 데모 계정(이메일 @demo.invalid)의 계정 설정 · 역할 연결 · 계정을 지운다
  DELETE us FROM dbo.UserSetting us JOIN [Identity].[User] u ON u.UserName = us.UserName WHERE u.Email LIKE N'%@demo.invalid';
  DELETE ur FROM [Identity].[UserRoles] ur JOIN [Identity].[User] u ON u.Id = ur.UserId WHERE u.Email LIKE N'%@demo.invalid';
  DELETE FROM [Identity].[User] WHERE Email LIKE N'%@demo.invalid';
  -- 3) 부서
  DELETE FROM dbo.Tenant WHERE Id = @T;
  -- 4) 감사 기록(DemoLogin)도 지우려면 아래 주석을 푼다
  -- DELETE FROM dbo.AuditLog WHERE Action = N'DemoLogin';
  IF @Commit = 1 COMMIT TRANSACTION; ELSE BEGIN ROLLBACK TRANSACTION; PRINT N'미리보기였습니다(되돌림). @Commit = 1 로 바꿔 다시 실행하세요.'; END
END
