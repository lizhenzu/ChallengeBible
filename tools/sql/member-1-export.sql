-- member-1-export.sql : 특정 회원(들)을 INSERT 문으로 내보내기 (읽기만 한다, 아무것도 바꾸지 않음).  2026-11-21
-- 쓰는 때: 완전 초기화 전에 지울 회원을 따로 받아 두고, 나중에 그 줄들을 그대로 실행하면 복원된다.
-- 사용: 아래 @Names 에 아이디를 쉼표로 넣고 실행 → "Line" 열을 모두 복사해 파일(예: member-restore.sql)로 저장.
-- 주의: 결과에는 비밀번호 해시 · 연락처가 들어 있다. 절대 Git 에 올리지 않는다(파일은 내 PC 에만).
-- 내보내는 것: 계정(Identity User · UserRoles · UserClaims · UserLogins) · 부서 소속(DeptRoleUserAssoc) · 명부(Person) · 반 배정(ClassMember)
--              · 태그(MemberTag) · 설정(UserSetting) · 보호자 연결(GuardianLink) · 가입 신청(SignupApplication)
-- 내보내지 않는 것: 미션 · 퀴즈 · 출석 · 선물 · 기도 · 통독 · 필사 기록(초기화로 지워지고 복원되지 않는다)
SET NOCOUNT ON;
DECLARE @Names nvarchar(max) = N'아이디1,아이디2';   -- ← 여기에 아이디(UserName)를 쉼표로

EXEC sp_set_session_context N'PlatformScope', 1;      -- 이 연결에서만 모든 부서를 읽는다

CREATE TABLE #U (UserId nvarchar(450) COLLATE DATABASE_DEFAULT PRIMARY KEY, UserName nvarchar(256) COLLATE DATABASE_DEFAULT);
INSERT #U SELECT u.Id, u.UserName FROM [Identity].[User] u
WHERE u.UserName IN (SELECT LTRIM(RTRIM(value)) FROM STRING_SPLIT(@Names, N','));

-- 관리자 계정은 내보내지 않는다(지우지 않는 계정, R-315)
DELETE FROM #U WHERE UserId IN (SELECT ur.UserId FROM [Identity].[UserRoles] ur JOIN [Identity].[Role] r ON r.Id = ur.RoleId WHERE r.NormalizedName IN (N'ADMIN', N'SUPERADMIN'));

CREATE TABLE #P (TenantId int, PersonId int);
INSERT #P SELECT p.TenantId, p.Id FROM app.Person p WHERE p.UserId IN (SELECT UserId FROM #U);

-- (표, 걸러내는 조건) — 순서가 곧 복원 순서
CREATE TABLE #T (Seq int IDENTITY, Tbl nvarchar(200), Cond nvarchar(max));
INSERT #T (Tbl, Cond) VALUES
 (N'[Identity].[User]',            N'Id IN (SELECT UserId FROM #U)'),
 (N'[Identity].[UserRoles]',       N'UserId IN (SELECT UserId FROM #U)'),
 (N'[Identity].[UserClaims]',      N'UserId IN (SELECT UserId FROM #U)'),
 (N'[Identity].[UserLogins]',      N'UserId IN (SELECT UserId FROM #U)'),
 (N'dbo.DeptRoleUserAssoc',        N'UserId IN (SELECT UserId FROM #U)'),
 (N'app.Person',                   N'UserId IN (SELECT UserId FROM #U)'),
 (N'app.ClassMember',              N'EXISTS (SELECT 1 FROM #P x WHERE x.TenantId = t.TenantId AND x.PersonId = t.PersonId)'),
 (N'app.MemberTag',                N'UserId IN (SELECT UserId FROM #U)'),
 (N'app.UserSetting',              N'UserName IN (SELECT UserName FROM #U)'),
 (N'app.GuardianLink',             N'GuardianUserName IN (SELECT UserName FROM #U) OR StudentUserName IN (SELECT UserName FROM #U)'),
 (N'dbo.SignupApplication',        N'UserId IN (SELECT UserId FROM #U)');

CREATE TABLE #Out (Seq int IDENTITY, Line nvarchar(max));
INSERT #Out (Line) SELECT N'-- 복원용 ' + CONVERT(nvarchar(30), SYSUTCDATETIME(), 120) + N' UTC · 아이디: ' + ISNULL((SELECT STRING_AGG(UserName, N', ') FROM #U), N'(없음)');
INSERT #Out (Line) SELECT N'SET NOCOUNT ON; EXEC sp_set_session_context N''PlatformScope'', 1;';

DECLARE @tbl nvarchar(200), @cond nvarchar(max), @sql nvarchar(max), @cols nvarchar(max), @vals nvarchar(max), @oid int, @hasId bit;
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT Tbl, Cond FROM #T ORDER BY Seq;
OPEN c; FETCH NEXT FROM c INTO @tbl, @cond;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @oid = OBJECT_ID(@tbl);
    -- 열마다 "값을 글자로" 바꾸는 식: 계산 열 · rowversion 은 뺀다
    SELECT @cols = STRING_AGG(CAST(QUOTENAME(c.name) AS nvarchar(max)), N', ') WITHIN GROUP (ORDER BY c.column_id),
           @vals = STRING_AGG(CAST(
               N'ISNULL(' + CASE
                   WHEN ty.name IN (N'nvarchar', N'varchar', N'nchar', N'char', N'ntext', N'text', N'uniqueidentifier', N'xml', N'sysname')
                        THEN N'N''N'''''' + REPLACE(CONVERT(nvarchar(max), t.' + QUOTENAME(c.name) + N'), N'''''''', N'''''''''''''''') + N'''''''''
                   WHEN ty.name IN (N'datetime2', N'datetime', N'smalldatetime') THEN N'N''N'''''' + CONVERT(nvarchar(40), t.' + QUOTENAME(c.name) + N', 121) + N'''''''''
                   WHEN ty.name = N'datetimeoffset' THEN N'N''N'''''' + CONVERT(nvarchar(40), t.' + QUOTENAME(c.name) + N', 127) + N'''''''''
                   WHEN ty.name IN (N'date', N'time') THEN N'N''N'''''' + CONVERT(nvarchar(40), t.' + QUOTENAME(c.name) + N', 121) + N'''''''''
                   WHEN ty.name IN (N'varbinary', N'binary', N'image') THEN N'CONVERT(nvarchar(max), t.' + QUOTENAME(c.name) + N', 1)'
                   WHEN ty.name IN (N'float', N'real') THEN N'CONVERT(nvarchar(60), t.' + QUOTENAME(c.name) + N', 3)'
                   ELSE N'CONVERT(nvarchar(60), t.' + QUOTENAME(c.name) + N')' END
               + N', N''NULL'')' AS nvarchar(max)), N' + N'', '' + ') WITHIN GROUP (ORDER BY c.column_id)
    FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
    WHERE c.object_id = @oid AND c.is_computed = 0 AND ty.name NOT IN (N'timestamp');
    SET @hasId = CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @oid AND is_identity = 1) THEN 1 ELSE 0 END;

    -- 이 표에 해당 줄이 있을 때만 쓴다
    SET @sql = N'IF EXISTS (SELECT 1 FROM ' + @tbl + N' t WHERE ' + @cond + N') BEGIN '
        + CASE WHEN @hasId = 1 THEN N'INSERT #Out (Line) VALUES (N''SET IDENTITY_INSERT ' + @tbl + N' ON;''); ' ELSE N'' END
        + N'INSERT #Out (Line) SELECT N''INSERT INTO ' + @tbl + N' (' + REPLACE(@cols, N'''', N'''''') + N') VALUES ('' + ' + @vals + N' + N'');'' FROM ' + @tbl + N' t WHERE ' + @cond + N'; '
        + CASE WHEN @hasId = 1 THEN N'INSERT #Out (Line) VALUES (N''SET IDENTITY_INSERT ' + @tbl + N' OFF;''); ' ELSE N'' END
        + N'END';
    EXEC sys.sp_executesql @sql;
    FETCH NEXT FROM c INTO @tbl, @cond;
END
CLOSE c; DEALLOCATE c;

INSERT #Out (Line) SELECT N'EXEC sp_set_session_context N''PlatformScope'', NULL;';
SELECT Line FROM #Out ORDER BY Seq;
-- 확인용 숫자(이 줄은 복사하지 않는다)
SELECT (SELECT COUNT(*) FROM #U) AS [내보낼 회원 수], (SELECT COUNT(*) FROM #P) AS [명부 줄], (SELECT COUNT(*) FROM #Out) AS [Line 수];
EXEC sp_set_session_context N'PlatformScope', NULL;
