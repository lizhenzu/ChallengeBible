-- yuyunbu-export.sql : 예전 유년부 DB(SSMS)에서 실행 → 나온 결과(s 칸)를 모두 복사해 새 사이트 [SQL 실행]에 붙여 넣는다 (ADR 0120)
-- 이 파일은 tools/sql/yuyunbu/build.py 로 만든다. 고칠 때는 import-body.sql · build.py 를 고친다.
-- 결과 보기: [결과를 표로](Ctrl+D) → 결과 칸 왼쪽 위를 눌러 모두 고르기 → 복사(Ctrl+C). 머리 줄(s)은 복사되지 않는다.
-- 예전 DB 는 읽기만 한다(아무것도 바꾸지 않음).
SET NOCOUNT ON;
DECLARE @L TABLE (n int IDENTITY(1,1) PRIMARY KEY, s nvarchar(max));
DECLARE @sa TABLE (Id nvarchar(450) PRIMARY KEY);
INSERT @sa SELECT DISTINCT ur.UserId FROM [Identity].[UserRoles] ur JOIN [Identity].[Role] r ON r.Id = ur.RoleId WHERE r.NormalizedName = N'SUPERADMIN';
DECLARE @cand TABLE (PersonId int NULL, UserId nvarchar(450) NULL, RoleCode nvarchar(50) NOT NULL, Src tinyint NOT NULL);
INSERT @cand (PersonId, UserId, RoleCode, Src)
SELECT pm.PersonId, d.UserId, LTRIM(RTRIM(d.RoleCode)), 1
FROM dbo.DeptRoleUserAssoc d LEFT JOIN [Identity].[User] u ON u.Id = d.UserId LEFT JOIN dbo.PersonMaster_2026 pm ON pm.UserName = u.UserName
WHERE d.UserId IS NOT NULL AND NULLIF(LTRIM(RTRIM(d.RoleCode)), N'') IS NOT NULL;
INSERT @cand (PersonId, UserId, RoleCode, Src)
SELECT ap.PersonId, COALESCE(NULLIF(LTRIM(RTRIM(ap.userid)), N''), u.Id), LTRIM(RTRIM(ap.RoleCode)), 2
FROM dbo.view_All_Person_2026 ap LEFT JOIN dbo.PersonMaster_2026 pm ON pm.PersonId = ap.PersonId LEFT JOIN [Identity].[User] u ON u.UserName = pm.UserName
WHERE NULLIF(LTRIM(RTRIM(ap.RoleCode)), N'') IS NOT NULL;
DECLARE @role TABLE (UserId nvarchar(450) PRIMARY KEY, RoleCode nvarchar(50));
INSERT @role SELECT UserId, RoleCode FROM (SELECT UserId, RoleCode, ROW_NUMBER() OVER (PARTITION BY UserId ORDER BY CASE WHEN RoleCode IN (N'T1004', N'T1005', N'T1006', N'T1008', N'T1009', N'T1040') THEN 1 WHEN RoleCode IN (N'T1010', N'T1013', N'T1015', N'T1016', N'T1017', N'T1018', N'T1019') THEN 2 WHEN RoleCode IN (N'T1002', N'T1003') THEN 3 WHEN RoleCode = N'T1020' THEN 4 WHEN RoleCode = N'T1001' THEN 5 WHEN RoleCode = N'T1099' THEN 7 ELSE 6 END, Src) k FROM @cand WHERE UserId IS NOT NULL) x WHERE k = 1;
DECLARE @users TABLE (Id nvarchar(450) PRIMARY KEY, UserName nvarchar(256));
INSERT @users SELECT u.Id, u.UserName FROM [Identity].[User] u WHERE u.Id NOT IN (SELECT Id FROM @sa) AND NULLIF(LTRIM(RTRIM(u.UserName)), N'') IS NOT NULL;
DECLARE @teach TABLE (Pid int PRIMARY KEY);
INSERT @teach SELECT DISTINCT Pid FROM (SELECT TeacherPersonId Pid FROM dbo.ClassTeacherMap_2026 WHERE IsActive = 1 UNION SELECT SubTeacherPersonId FROM dbo.ClassTeacherMap_2026 WHERE IsActive = 1 AND SubTeacherPersonId IS NOT NULL) t;
INSERT @cand (PersonId, UserId, RoleCode, Src) SELECT Pid, NULL, N'T1002', 3 FROM @teach;
INSERT @cand (PersonId, UserId, RoleCode, Src) SELECT DISTINCT PersonID, NULL, N'T1001', 4 FROM dbo.ClassInfo_2026 WHERE PersonID IS NOT NULL;
DECLARE @prole TABLE (PersonId int PRIMARY KEY, RoleCode nvarchar(50));
INSERT @prole SELECT PersonId, RoleCode FROM (SELECT PersonId, RoleCode, ROW_NUMBER() OVER (PARTITION BY PersonId ORDER BY CASE WHEN RoleCode IN (N'T1004', N'T1005', N'T1006', N'T1008', N'T1009', N'T1040') THEN 1 WHEN RoleCode IN (N'T1010', N'T1013', N'T1015', N'T1016', N'T1017', N'T1018', N'T1019') THEN 2 WHEN RoleCode IN (N'T1002', N'T1003') THEN 3 WHEN RoleCode = N'T1020' THEN 4 WHEN RoleCode = N'T1001' THEN 5 WHEN RoleCode = N'T1099' THEN 7 ELSE 6 END, Src) k FROM @cand WHERE PersonId IS NOT NULL) x WHERE k = 1;
DECLARE @people TABLE (Pid int PRIMARY KEY, Name nvarchar(50), UserName nvarchar(256), RoleCode nvarchar(50), BM tinyint, BD tinyint, Ph1 nvarchar(20), Ph2 nvarchar(20));
INSERT @people
SELECT p.PersonId, LEFT(LTRIM(RTRIM(p.fullName)), 50), NULLIF(LTRIM(RTRIM(p.UserName)), N''),
       COALESCE(r.RoleCode, pr.RoleCode, N'T1099'),
       CASE WHEN p.bMonth BETWEEN 1 AND 12 AND p.bDay BETWEEN 1 AND 31 THEN p.bMonth END, CASE WHEN p.bMonth BETWEEN 1 AND 12 AND p.bDay BETWEEN 1 AND 31 THEN p.bDay END,
       LEFT(CASE WHEN x.p1 NOT LIKE N'%[^0-9]%' AND LEN(x.p1) = 11 THEN STUFF(STUFF(x.p1, 8, 0, N'-'), 4, 0, N'-') ELSE x.p1 END, 20),
       LEFT(CASE WHEN x.p2 NOT LIKE N'%[^0-9]%' AND LEN(x.p2) = 11 THEN STUFF(STUFF(x.p2, 8, 0, N'-'), 4, 0, N'-') ELSE x.p2 END, 20)
FROM dbo.PersonMaster_2026 p
CROSS APPLY (SELECT NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100), p.PhoneNumber))), N'') p1, NULLIF(LTRIM(RTRIM(p.PhoneNumber2)), N'') p2) x
LEFT JOIN [Identity].[User] au ON au.UserName = p.UserName
LEFT JOIN @role r ON r.UserId = au.Id
LEFT JOIN @prole pr ON pr.PersonId = p.PersonId
WHERE NULLIF(LTRIM(RTRIM(p.fullName)), N'') IS NOT NULL
  AND (au.Id IS NULL OR au.Id NOT IN (SELECT Id FROM @sa));
DECLARE @cls TABLE (Y int, B int, Name nvarchar(50), PRIMARY KEY (Y, B));
INSERT @cls SELECT ClassYear, ClassBan, MAX(LEFT(Name, 50)) FROM (
    SELECT ClassYear, ClassBan, COALESCE(NULLIF(LTRIM(RTRIM(ClassName)), N''), CONCAT(ClassYear, N'학년 ', ClassBan, N'반')) Name FROM dbo.ClassMaster WHERE IsActive = 1
    UNION ALL SELECT DISTINCT c.ClassYear, c.ClassBan, CONCAT(c.ClassYear, N'학년 ', c.ClassBan, N'반') FROM dbo.ClassInfo_2026 c
      WHERE c.ClassYear IS NOT NULL AND c.ClassBan IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.ClassMaster m WHERE m.IsActive = 1 AND m.ClassYear = c.ClassYear AND m.ClassBan = c.ClassBan)
) z GROUP BY ClassYear, ClassBan;
INSERT @L(s) VALUES (N'-- 유년부 이관 넣기 SQL(예전 DB 에서 ' + CONVERT(nvarchar(30), SYSUTCDATETIME(), 126) + N' UTC 에 뽑음). 새 사이트 [SQL 실행]에서 먼저 [시험 실행], 결과를 보고 [저장].');
INSERT @L(s) VALUES (N'DECLARE @now datetime2(3) = ''' + CONVERT(nvarchar(30), SYSUTCDATETIME(), 126) + N''';');
INSERT @L(s) VALUES (N'DECLARE @U TABLE (Id nvarchar(450) PRIMARY KEY, UserName nvarchar(256), Email nvarchar(256), EmailConfirmed bit, PasswordHash nvarchar(max), SecurityStamp nvarchar(max), PhoneNumber nvarchar(max), FirstName nvarchar(max), LastName nvarchar(max), UsernameChangeLimit int, InsertDT datetime, RoleCode nvarchar(50));');
INSERT @L(s) VALUES (N'DECLARE @P TABLE (OldPid int PRIMARY KEY, Name nvarchar(50), UserName nvarchar(256), RoleCode nvarchar(50), BirthMonth tinyint, BirthDay tinyint, Phone1 nvarchar(20), Phone2 nvarchar(20));');
INSERT @L(s) VALUES (N'DECLARE @C TABLE (ClassYear int, ClassBan int, Name nvarchar(50), PRIMARY KEY (ClassYear, ClassBan));');
INSERT @L(s) VALUES (N'DECLARE @M TABLE (OldPid int, ClassYear int, ClassBan int, IsTeacher bit);');
INSERT @L(s) SELECT N'INSERT @U VALUES (' + ISNULL(N'N''' + REPLACE(CONVERT(nvarchar(max), u.Id), N'''', N'''''') + N'''', N'NULL') + N', ' + ISNULL(N'N''' + REPLACE(CONVERT(nvarchar(max), u.UserName), N'''', N'''''') + N'''', N'NULL') + N', ' + ISNULL(N'N''' + REPLACE(CONVERT(nvarchar(max), u.Email), N'''', N'''''') + N'''', N'NULL') + N', ' + ISNULL(CONVERT(nvarchar(20), u.EmailConfirmed), N'NULL') + N', ' + ISNULL(N'N''' + REPLACE(CONVERT(nvarchar(max), u.PasswordHash), N'''', N'''''') + N'''', N'NULL') + N', ' + ISNULL(N'N''' + REPLACE(CONVERT(nvarchar(max), u.SecurityStamp), N'''', N'''''') + N'''', N'NULL') + N', ' + ISNULL(N'N''' + REPLACE(CONVERT(nvarchar(max), u.PhoneNumber), N'''', N'''''') + N'''', N'NULL') + N', ' + ISNULL(N'N''' + REPLACE(CONVERT(nvarchar(max), u.FirstName), N'''', N'''''') + N'''', N'NULL') + N', ' + ISNULL(N'N''' + REPLACE(CONVERT(nvarchar(max), u.LastName), N'''', N'''''') + N'''', N'NULL') + N', ' + ISNULL(CONVERT(nvarchar(20), u.UsernameChangeLimit), N'NULL') + N', ' + ISNULL(N'''' + CONVERT(nvarchar(30), u.insertDT, 126) + N'''', N'NULL') + N', ' + ISNULL(N'N''' + REPLACE(CONVERT(nvarchar(max), r.RoleCode), N'''', N'''''') + N'''', N'NULL') + N');' FROM [Identity].[User] u JOIN @users k ON k.Id = u.Id LEFT JOIN @role r ON r.UserId = u.Id ORDER BY u.UserName;
INSERT @L(s) SELECT N'INSERT @P VALUES (' + ISNULL(CONVERT(nvarchar(20), p.Pid), N'NULL') + N', ' + ISNULL(N'N''' + REPLACE(CONVERT(nvarchar(max), p.Name), N'''', N'''''') + N'''', N'NULL') + N', ' + ISNULL(N'N''' + REPLACE(CONVERT(nvarchar(max), p.UserName), N'''', N'''''') + N'''', N'NULL') + N', ' + ISNULL(N'N''' + REPLACE(CONVERT(nvarchar(max), p.RoleCode), N'''', N'''''') + N'''', N'NULL') + N', ' + ISNULL(CONVERT(nvarchar(20), p.BM), N'NULL') + N', ' + ISNULL(CONVERT(nvarchar(20), p.BD), N'NULL') + N', ' + ISNULL(N'N''' + REPLACE(CONVERT(nvarchar(max), p.Ph1), N'''', N'''''') + N'''', N'NULL') + N', ' + ISNULL(N'N''' + REPLACE(CONVERT(nvarchar(max), p.Ph2), N'''', N'''''') + N'''', N'NULL') + N');' FROM @people p ORDER BY p.Pid;
INSERT @L(s) SELECT N'INSERT @C VALUES (' + ISNULL(CONVERT(nvarchar(20), c.Y), N'NULL') + N', ' + ISNULL(CONVERT(nvarchar(20), c.B), N'NULL') + N', ' + ISNULL(N'N''' + REPLACE(CONVERT(nvarchar(max), c.Name), N'''', N'''''') + N'''', N'NULL') + N');' FROM @cls c ORDER BY c.Y, c.B;
INSERT @L(s) SELECT N'INSERT @M VALUES (' + ISNULL(CONVERT(nvarchar(20), m.Pid), N'NULL') + N', ' + ISNULL(CONVERT(nvarchar(20), m.Y), N'NULL') + N', ' + ISNULL(CONVERT(nvarchar(20), m.B), N'NULL') + N', ' + ISNULL(CONVERT(nvarchar(20), m.T), N'NULL') + N');' FROM (
    SELECT c.PersonID Pid, c.ClassYear Y, c.ClassBan B, 0 T FROM dbo.ClassInfo_2026 c WHERE c.PersonID IS NOT NULL AND c.ClassYear IS NOT NULL AND c.ClassBan IS NOT NULL
    UNION SELECT t.TeacherPersonId, t.ClassYear, t.ClassBan, 1 FROM dbo.ClassTeacherMap_2026 t WHERE t.IsActive = 1
    UNION SELECT t.SubTeacherPersonId, t.ClassYear, t.ClassBan, 1 FROM dbo.ClassTeacherMap_2026 t WHERE t.IsActive = 1 AND t.SubTeacherPersonId IS NOT NULL
) m WHERE m.Pid IN (SELECT Pid FROM @people) ORDER BY m.T DESC, m.Pid;
INSERT @L(s) VALUES (N'-- ===== 넣기(새 사이트 SQL 화면) — 위의 @U @P @C @M 자료를 유년부(jamsil-kids)에 넣는다 ====='),
    (N'-- 정한 것(2026-11-20 사용자): 계정 + 역할 + 명부 + 반 · 같은 이메일/아이디가 있으면 예전 것으로 덮어씀 · 권한은 옮기지 않음(Basic)'),
    (N'-- SuperAdmin 은 가져오지 않음 · 관리자(Admin · SuperAdmin) 계정은 절대 바꾸지 않음(R-315) · 유년부 계정 없는 명부 · 반 배정은 비우고 채움'),
    (N'SET NOCOUNT ON;'),
    (N'DECLARE @tid int = (SELECT Id FROM app.Tenant WHERE Slug = N''jamsil-kids'');'),
    (N'IF @tid IS NULL THROW 50001, N''유년부(jamsil-kids) 부서를 찾을 수 없어요.'', 1;'),
    (N'DECLARE @lobby int = (SELECT TOP 1 Id FROM app.Tenant WHERE IsLobby = 1);'),
    (N'DECLARE @basic nvarchar(450) = (SELECT TOP 1 Id FROM [Identity].[Role] WHERE NormalizedName = N''BASIC'');'),
    (N'DECLARE @admins TABLE (Id nvarchar(450) PRIMARY KEY);'),
    (N'INSERT @admins SELECT DISTINCT ur.UserId FROM [Identity].[UserRoles] ur JOIN [Identity].[Role] r ON r.Id = ur.RoleId WHERE r.NormalizedName IN (N''ADMIN'', N''SUPERADMIN'');'),
    (N' '),
    (N'-- 1. 계정 짝(예전 Id → 새 Id): 같은 이메일, 없으면 같은 아이디. 관리자면 건드리지 않는다'),
    (N'DECLARE @UM TABLE (OldId nvarchar(450) PRIMARY KEY, NewId nvarchar(450) NULL, Kind nvarchar(10) NULL);'),
    (N'INSERT @UM (OldId, NewId)'),
    (N'SELECT u.Id, (SELECT TOP 1 x.Id FROM [Identity].[User] x'),
    (N'              WHERE (u.Email IS NOT NULL AND x.NormalizedEmail = UPPER(u.Email)) OR x.NormalizedUserName = UPPER(u.UserName) OR x.Id = u.Id'),
    (N'              ORDER BY CASE WHEN u.Email IS NOT NULL AND x.NormalizedEmail = UPPER(u.Email) THEN 0 WHEN x.NormalizedUserName = UPPER(u.UserName) THEN 1 ELSE 2 END)'),
    (N'FROM @U u;'),
    (N'UPDATE @UM SET Kind = CASE WHEN NewId IS NULL THEN N''new'' WHEN NewId IN (SELECT Id FROM @admins) THEN N''admin'' ELSE N''update'' END;'),
    (N'UPDATE @UM SET NewId = OldId WHERE Kind = N''new'';'),
    (N' '),
    (N'UPDATE x SET PasswordHash = u.PasswordHash, SecurityStamp = ISNULL(u.SecurityStamp, CONVERT(nvarchar(36), NEWID())), ConcurrencyStamp = CONVERT(nvarchar(36), NEWID()),'),
    (N'             FirstName = u.FirstName, LastName = u.LastName, PhoneNumber = u.PhoneNumber'),
    (N'FROM [Identity].[User] x JOIN @UM um ON um.NewId = x.Id AND um.Kind = N''update'' JOIN @U u ON u.Id = um.OldId;'),
    (N' '),
    (N'INSERT [Identity].[User] (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber,'),
    (N'                          PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount, FirstName, LastName, ProfilePicture, UsernameChangeLimit, insertDT)'),
    (N'SELECT u.Id, u.UserName, UPPER(u.UserName), u.Email, UPPER(u.Email), u.EmailConfirmed, u.PasswordHash, ISNULL(u.SecurityStamp, CONVERT(nvarchar(36), NEWID())), CONVERT(nvarchar(36), NEWID()), u.PhoneNumber,'),
    (N'       0, 0, NULL, 1, 0, u.FirstName, u.LastName, NULL, u.UsernameChangeLimit, ISNULL(u.InsertDT, @now)'),
    (N'FROM @U u JOIN @UM um ON um.OldId = u.Id AND um.Kind = N''new'';'),
    (N'INSERT [Identity].[UserRoles] (UserId, RoleId) SELECT um.NewId, @basic FROM @UM um WHERE um.Kind = N''new'' AND @basic IS NOT NULL;'),
    (N' '),
    (N'-- 2. 소속: 예전 역할로 유년부에. 관리자 · 다른 정식 부서 소속은 그대로 두고, 역할이 없던 새 계정은 임시 부서(있으면)'),
    (N'DECLARE @MB TABLE (NewId nvarchar(450) PRIMARY KEY, RoleCode nvarchar(50) NULL, Result nvarchar(10) NOT NULL);'),
    (N'INSERT @MB (NewId, RoleCode, Result)'),
    (N'SELECT um.NewId, u.RoleCode,'),
    (N'       CASE WHEN um.Kind = N''admin'' THEN N''admin'''),
    (N'            WHEN u.RoleCode IS NULL THEN N''norole'''),
    (N'            WHEN EXISTS (SELECT 1 FROM dbo.DeptRoleUserAssoc d JOIN app.Tenant t ON t.Id = d.TenantId WHERE d.UserId = um.NewId AND t.IsLobby = 0 AND d.TenantId <> @tid) THEN N''otherdept'''),
    (N'            ELSE N''kids'' END'),
    (N'FROM @UM um JOIN @U u ON u.Id = um.OldId;'),
    (N'DECLARE @fromLobby int = (SELECT COUNT(*) FROM @MB m WHERE m.Result = N''kids'' AND EXISTS (SELECT 1 FROM dbo.DeptRoleUserAssoc d WHERE d.UserId = m.NewId AND d.TenantId = @lobby));'),
    (N'DELETE d FROM dbo.DeptRoleUserAssoc d JOIN @MB m ON m.NewId = d.UserId AND m.Result = N''kids'' WHERE d.TenantId <> @tid;'),
    (N'UPDATE d SET RoleCode = m.RoleCode, updateDT = @now FROM dbo.DeptRoleUserAssoc d JOIN @MB m ON m.NewId = d.UserId AND m.Result = N''kids'' WHERE d.TenantId = @tid;'),
    (N'INSERT dbo.DeptRoleUserAssoc (TenantId, RoleCode, UserId, InsertDT, updateDT)'),
    (N'SELECT @tid, m.RoleCode, m.NewId, @now, @now FROM @MB m WHERE m.Result = N''kids'' AND NOT EXISTS (SELECT 1 FROM dbo.DeptRoleUserAssoc d WHERE d.TenantId = @tid AND d.UserId = m.NewId);'),
    (N'INSERT dbo.DeptRoleUserAssoc (TenantId, RoleCode, UserId, InsertDT, updateDT)'),
    (N'SELECT @lobby, N''T1001'', m.NewId, @now, @now FROM @MB m JOIN @UM um ON um.NewId = m.NewId AND um.Kind = N''new'''),
    (N'WHERE m.Result = N''norole'' AND @lobby IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.DeptRoleUserAssoc d WHERE d.UserId = m.NewId);'),
    (N' '),
    (N'-- 3. 유년부 명부 비우기(명단 올리기와 같다, R-301 · R-302): 계정 없는 줄 · 반 배정 · 그 사람들 출석 · 신청. 고른 · 산 · 준 선물이 있는 줄은 남긴다(R-322)'),
    (N'DECLARE @all TABLE (Id int PRIMARY KEY);'),
    (N'INSERT @all SELECT Id FROM app.Person WHERE TenantId = @tid AND UserId IS NULL;'),
    (N'DECLARE @keep TABLE (Id int PRIMARY KEY);'),
    (N'INSERT @keep SELECT DISTINCT g.PersonId FROM app.Gift g JOIN @all a ON a.Id = g.PersonId WHERE g.TenantId = @tid AND g.State IN (N''Picked'', N''Purchased'', N''Delivered'');'),
    (N'DECLARE @drop TABLE (Id int PRIMARY KEY);'),
    (N'INSERT @drop SELECT Id FROM @all WHERE Id NOT IN (SELECT Id FROM @keep);'),
    (N'DECLARE @dropAtt int = (SELECT COUNT(*) FROM app.AttendanceCheck a JOIN @all x ON x.Id = a.PersonId WHERE a.TenantId = @tid);'),
    (N'DELETE l FROM app.GiftLog l JOIN app.Gift g ON g.TenantId = l.TenantId AND g.Id = l.GiftId JOIN @drop d ON d.Id = g.PersonId WHERE g.TenantId = @tid;'),
    (N'DELETE u FROM app.GiftPointUse u JOIN app.Gift g ON g.TenantId = u.TenantId AND g.Id = u.GiftId JOIN @drop d ON d.Id = g.PersonId WHERE g.TenantId = @tid;'),
    (N'DELETE g FROM app.Gift g JOIN @drop d ON d.Id = g.PersonId WHERE g.TenantId = @tid;'),
    (N'DELETE a FROM app.AttendanceCheck a JOIN @all x ON x.Id = a.PersonId WHERE a.TenantId = @tid;'),
    (N'DELETE FROM app.ClassMember WHERE TenantId = @tid;'),
    (N'DELETE s FROM dbo.SignupApplication s JOIN @all x ON x.Id = s.PersonId WHERE s.TenantId = @tid;'),
    (N'DELETE p FROM app.Person p JOIN @drop d ON d.Id = p.Id WHERE p.TenantId = @tid;'),
    (N' '),
    (N'-- 4. 명부 줄 짝(예전 PersonId → 새 Person.Id)'),
    (N'DECLARE @PM TABLE (OldPid int PRIMARY KEY, NewPid int NULL, UserId nvarchar(450) NULL, Kind nvarchar(10) NOT NULL);'),
    (N'INSERT @PM (OldPid, NewPid, UserId, Kind)'),
    (N'SELECT p.OldPid, NULL, m.NewId,'),
    (N'       CASE WHEN m.Result = N''admin'' THEN N''admin'' WHEN m.Result = N''kids'' THEN N''linked'' WHEN m.Result = N''otherdept'' THEN N''skip'' ELSE N''roster'' END'),
    (N'FROM @P p'),
    (N'LEFT JOIN @U u ON p.UserName IS NOT NULL AND u.UserName = p.UserName'),
    (N'LEFT JOIN @UM um ON um.OldId = u.Id'),
    (N'LEFT JOIN @MB m ON m.NewId = um.NewId;'),
    (N'UPDATE @PM SET UserId = NULL WHERE Kind = N''roster'';'),
    (N'-- 같은 계정에 예전 명부 줄이 둘 이상이면 하나만 잇는다'),
    (N'WITH d AS (SELECT Kind, ROW_NUMBER() OVER (PARTITION BY UserId ORDER BY OldPid) n FROM @PM WHERE UserId IS NOT NULL) UPDATE d SET Kind = N''skip'' WHERE n > 1;'),
    (N'-- 관리자 · 계정 있는 사람: 유년부에 이미 있는 줄'),
    (N'UPDATE pm SET NewPid = x.Id FROM @PM pm JOIN app.Person x ON x.TenantId = @tid AND x.UserId = pm.UserId WHERE pm.Kind IN (N''admin'', N''linked'');'),
    (N'-- 남긴 줄(선물): 같은 이름이 한 줄뿐이면 그 줄을 다시 쓴다'),
    (N'UPDATE pm SET NewPid = x.Id'),
    (N'FROM @PM pm JOIN @P p ON p.OldPid = pm.OldPid'),
    (N'JOIN app.Person x ON x.TenantId = @tid AND x.UserId IS NULL AND x.Id IN (SELECT Id FROM @keep) AND x.Name = p.Name'),
    (N'WHERE pm.Kind = N''roster'' AND (SELECT COUNT(*) FROM @P q WHERE q.Name = p.Name) = 1 AND (SELECT COUNT(*) FROM app.Person y WHERE y.TenantId = @tid AND y.UserId IS NULL AND y.Id IN (SELECT Id FROM @keep) AND y.Name = p.Name) = 1;'),
    (N' '),
    (N'UPDATE x SET BirthMonth = ISNULL(p.BirthMonth, x.BirthMonth), BirthDay = CASE WHEN p.BirthMonth IS NULL THEN x.BirthDay ELSE p.BirthDay END,'),
    (N'             Phone1 = ISNULL(p.Phone1, x.Phone1), Phone2 = ISNULL(p.Phone2, x.Phone2),'),
    (N'             Name = CASE WHEN pm.Kind = N''roster'' THEN p.Name ELSE x.Name END, RoleCode = CASE WHEN pm.Kind = N''roster'' THEN p.RoleCode ELSE x.RoleCode END'),
    (N'FROM app.Person x JOIN @PM pm ON pm.NewPid = x.Id AND pm.Kind IN (N''linked'', N''roster'') JOIN @P p ON p.OldPid = pm.OldPid WHERE x.TenantId = @tid;'),
    (N' '),
    (N'DECLARE @ins TABLE (OldPid int PRIMARY KEY, NewPid int);'),
    (N'MERGE app.Person AS t'),
    (N'USING (SELECT pm.OldPid, pm.UserId, pm.Kind, p.Name, p.RoleCode, p.BirthMonth, p.BirthDay, p.Phone1, p.Phone2'),
    (N'       FROM @PM pm JOIN @P p ON p.OldPid = pm.OldPid WHERE pm.NewPid IS NULL AND pm.Kind IN (N''linked'', N''roster'')) AS s'),
    (N'ON 1 = 0'),
    (N'WHEN NOT MATCHED THEN INSERT (TenantId, Name, RoleCode, BirthMonth, BirthDay, Phone1, Phone2, UserId, CreatedUtc, CreatedByUserName)'),
    (N'     VALUES (@tid, s.Name, CASE WHEN s.Kind = N''linked'' THEN NULL ELSE s.RoleCode END, s.BirthMonth, s.BirthDay, s.Phone1, s.Phone2, s.UserId, @now, N''yuyunbu-import'')'),
    (N'OUTPUT s.OldPid, inserted.Id INTO @ins (OldPid, NewPid);'),
    (N'UPDATE pm SET NewPid = i.NewPid FROM @PM pm JOIN @ins i ON i.OldPid = pm.OldPid;'),
    (N' '),
    (N'-- 5. 반: 반 묶음 · 새친구반이 없으면 만들고, 예전 반 이름(ClassMaster)으로 반을 만든다. 한 사람은 한 반에만(R-232), 담임 · 부담임은 선생님으로'),
    (N'DECLARE @term int = (SELECT TOP 1 Id FROM app.ClassTerm WHERE TenantId = @tid ORDER BY Id);'),
    (N'IF @term IS NULL'),
    (N'BEGIN'),
    (N'    INSERT app.ClassTerm (TenantId, Year, StartDate, CreatedUtc) VALUES (@tid, YEAR(@now), CAST(@now AS date), @now);'),
    (N'    SET @term = SCOPE_IDENTITY();'),
    (N'END;'),
    (N'IF NOT EXISTS (SELECT 1 FROM app.Class WHERE TenantId = @tid AND TermId = @term AND IsNewcomer = 1)'),
    (N'    INSERT app.Class (TenantId, TermId, Name, IsNewcomer, CreatedUtc) VALUES (@tid, @term, N''새친구반'', 1, @now);'),
    (N'DECLARE @newClasses int = (SELECT COUNT(*) FROM @C c WHERE NOT EXISTS (SELECT 1 FROM app.Class k WHERE k.TenantId = @tid AND k.TermId = @term AND k.Name = c.Name));'),
    (N'INSERT app.Class (TenantId, TermId, Name, IsNewcomer, CreatedUtc)'),
    (N'SELECT @tid, @term, c.Name, 0, @now FROM @C c WHERE NOT EXISTS (SELECT 1 FROM app.Class k WHERE k.TenantId = @tid AND k.TermId = @term AND k.Name = c.Name);'),
    (N'INSERT app.ClassMember (TenantId, TermId, ClassId, PersonId, IsTeacher, CreatedUtc)'),
    (N'SELECT @tid, @term, x.ClassId, x.NewPid, x.IsTeacher, @now'),
    (N'FROM (SELECT k.Id ClassId, pm.NewPid, m.IsTeacher, ROW_NUMBER() OVER (PARTITION BY pm.NewPid ORDER BY m.IsTeacher DESC, k.Id) n'),
    (N'      FROM @M m JOIN @C c ON c.ClassYear = m.ClassYear AND c.ClassBan = m.ClassBan'),
    (N'      JOIN app.Class k ON k.TenantId = @tid AND k.TermId = @term AND k.Name = c.Name AND k.IsNewcomer = 0'),
    (N'      JOIN @PM pm ON pm.OldPid = m.OldPid AND pm.NewPid IS NOT NULL) x'),
    (N'WHERE x.n = 1;'),
    (N' '),
    (N'-- 6. 기록 · 결과'),
    (N'INSERT app.AuditLog (OccurredUtc, ActorUserName, Action, TenantId, Detail)'),
    (N'VALUES (@now, N''yuyunbu-import'', N''YuyunbuImport'', @tid,'),
    (N'        CONCAT(N''new='', (SELECT COUNT(*) FROM @UM WHERE Kind = N''new''), N'';update='', (SELECT COUNT(*) FROM @UM WHERE Kind = N''update''),'),
    (N'               N'';people='', (SELECT COUNT(*) FROM @PM WHERE NewPid IS NOT NULL), N'';dropped='', (SELECT COUNT(*) FROM @drop)));'),
    (N'SELECT N''계정 새로'' AS [항목], COUNT(*) AS [수] FROM @UM WHERE Kind = N''new'''),
    (N'UNION ALL SELECT N''계정 덮어씀(같은 이메일 · 아이디)'', COUNT(*) FROM @UM WHERE Kind = N''update'''),
    (N'UNION ALL SELECT N''관리자라 건너뜀'', COUNT(*) FROM @UM WHERE Kind = N''admin'''),
    (N'UNION ALL SELECT N''유년부 소속'', COUNT(*) FROM @MB WHERE Result = N''kids'''),
    (N'UNION ALL SELECT N''└ 임시 부서에서 옮김'', @fromLobby'),
    (N'UNION ALL SELECT N''다른 부서 소속이라 그대로'', COUNT(*) FROM @MB WHERE Result = N''otherdept'''),
    (N'UNION ALL SELECT N''예전 역할 없음'', COUNT(*) FROM @MB WHERE Result = N''norole'''),
    (N'UNION ALL SELECT N''명부 줄(계정과 이음)'', COUNT(*) FROM @PM WHERE Kind = N''linked'' AND NewPid IS NOT NULL'),
    (N'UNION ALL SELECT N''명부 줄(계정 없음)'', COUNT(*) FROM @PM WHERE Kind = N''roster'' AND NewPid IS NOT NULL'),
    (N'UNION ALL SELECT N''명부 줄 건너뜀'', COUNT(*) FROM @PM WHERE Kind = N''skip'''),
    (N'UNION ALL SELECT N''새로 만든 반'', @newClasses'),
    (N'UNION ALL SELECT N''반 배정(학생 · 선생님)'', (SELECT COUNT(*) FROM app.ClassMember WHERE TenantId = @tid)'),
    (N'UNION ALL SELECT N''비운 명부 줄(예전 새 사이트 것)'', (SELECT COUNT(*) FROM @drop)'),
    (N'UNION ALL SELECT N''└ 함께 지운 출석'', @dropAtt'),
    (N'UNION ALL SELECT N''선물이 있어 남긴 줄'', (SELECT COUNT(*) FROM @keep);'),
    (N'SELECT u.UserName AS [건너뛴 계정], u.Email AS [이메일], CASE m.Result WHEN N''admin'' THEN N''관리자(바꾸지 않음)'' WHEN N''otherdept'' THEN N''다른 부서 소속'' ELSE N''예전 역할 없음'' END AS [까닭]'),
    (N'FROM @MB m JOIN @UM um ON um.NewId = m.NewId JOIN @U u ON u.Id = um.OldId WHERE m.Result <> N''kids'' ORDER BY m.Result, u.UserName;');
SELECT s FROM @L ORDER BY n;
