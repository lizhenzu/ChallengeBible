-- yuyunbu-role-check.sql : 예전 유년부 DB(SSMS)에서 실행. 읽기만 한다(아무것도 바꾸지 않음). 2026-11-21
-- 왜: 새 사이트로 옮기면 부장 · 부감 · 서기 같은 임원 역할이 빠진다고 해서, 예전 DB 어디에 그 역할이 남아 있는지 먼저 본다.
-- 결과 칸이 여러 개 나온다. 모두(표로 보기 Ctrl+D) 캡처해서 알려 주세요. 개인 정보(이름 · 전화)는 나오지 않는다.
SET NOCOUNT ON;

-- ① 지금 역할 표(DeptRoleUserAssoc)의 역할별 사람 수
SELECT N'① 지금 DeptRoleUserAssoc' AS [출처], LTRIM(RTRIM(RoleCode)) AS [역할코드], COUNT(DISTINCT UserId) AS [사람 수], COUNT(*) AS [줄 수]
FROM dbo.DeptRoleUserAssoc GROUP BY LTRIM(RTRIM(RoleCode)) ORDER BY 2;

-- ② 사람 한 명이 역할 줄을 여러 개 가진 경우(있으면 하나만 옮겨져서 나머지가 빠진다)
SELECT N'② 역할 줄이 둘 이상인 사람' AS [출처], COUNT(*) AS [사람 수] FROM (SELECT UserId FROM dbo.DeptRoleUserAssoc WHERE UserId IS NOT NULL GROUP BY UserId HAVING COUNT(*) > 1) x;

-- ③ 백업 표(역할 단일화 · 부서 정리 때 만든 것)가 있으면 거기 역할별 사람 수 — 부장 · 부감 · 서기가 거기 남아 있을 수 있다
DECLARE @t TABLE (T sysname);
INSERT @t VALUES (N'DeptRoleUserAssoc_bak_20261109r'), (N'DeptRoleUserAssoc_bak_20261109');
DECLARE @name sysname, @sql nvarchar(max);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT T FROM @t;
OPEN c; FETCH NEXT FROM c INTO @name;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF OBJECT_ID(N'dbo.' + @name) IS NULL SELECT N'③ ' + @name AS [출처], N'(없음)' AS [역할코드], 0 AS [사람 수];
    ELSE
    BEGIN
        SET @sql = N'SELECT N''③ ' + @name + N''' AS [출처], LTRIM(RTRIM(RoleCode)) AS [역할코드], COUNT(DISTINCT UserId) AS [사람 수] FROM dbo.' + QUOTENAME(@name) + N' GROUP BY LTRIM(RTRIM(RoleCode)) ORDER BY 2;';
        EXEC sys.sp_executesql @sql;
    END
    FETCH NEXT FROM c INTO @name;
END
CLOSE c; DEALLOCATE c;

-- ④ 반 배정 쪽 역할(view_All_Person_2026)의 역할별 사람 수
IF OBJECT_ID(N'dbo.view_All_Person_2026') IS NULL SELECT N'④ view_All_Person_2026' AS [출처], N'(없음)' AS [역할코드], 0 AS [사람 수];
ELSE EXEC sys.sp_executesql N'SELECT N''④ view_All_Person_2026'' AS [출처], LTRIM(RTRIM(RoleCode)) AS [역할코드], COUNT(DISTINCT userid) AS [사람 수] FROM dbo.view_All_Person_2026 GROUP BY LTRIM(RTRIM(RoleCode)) ORDER BY 2;';

-- ⑤ 임원 코드(T1004 지도 · T1005 부장 · T1006 부감 · T1008 서기 · T1009 회계 · T1040 교장)를 어느 표에든 가진 사람 수 — 지금 표에서 임원이 아닌 사람만
IF OBJECT_ID(N'dbo.DeptRoleUserAssoc_bak_20261109r') IS NOT NULL
    EXEC sys.sp_executesql N'SELECT N''⑤ 백업에는 임원인데 지금은 임원이 아닌 사람'' AS [출처], b.RoleCode AS [백업 역할], COUNT(DISTINCT b.UserId) AS [사람 수],
        MIN(ISNULL(d.RoleCode, N''(없음)'')) AS [지금 역할 예] FROM dbo.DeptRoleUserAssoc_bak_20261109r b LEFT JOIN dbo.DeptRoleUserAssoc d ON d.UserId = b.UserId
        WHERE LTRIM(RTRIM(b.RoleCode)) IN (N''T1004'', N''T1005'', N''T1006'', N''T1008'', N''T1009'', N''T1040'')
          AND ISNULL(LTRIM(RTRIM(d.RoleCode)), N'''') NOT IN (N''T1004'', N''T1005'', N''T1006'', N''T1008'', N''T1009'', N''T1040'') GROUP BY b.RoleCode ORDER BY 2;';
