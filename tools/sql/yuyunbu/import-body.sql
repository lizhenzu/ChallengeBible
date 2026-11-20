-- ===== 넣기(새 사이트 SQL 화면) — 위의 @U @P @C @M 자료를 유년부(jamsil-kids)에 넣는다 =====
-- 정한 것(2026-11-20 사용자): 계정 + 역할 + 명부 + 반 · 같은 이메일/아이디가 있으면 예전 것으로 덮어씀 · 권한은 옮기지 않음(Basic)
-- SuperAdmin 은 가져오지 않음 · 관리자(Admin · SuperAdmin) 계정은 절대 바꾸지 않음(R-315) · 유년부 계정 없는 명부 · 반 배정은 비우고 채움
SET NOCOUNT ON;
DECLARE @tid int = (SELECT Id FROM app.Tenant WHERE Slug = N'jamsil-kids');
IF @tid IS NULL THROW 50001, N'유년부(jamsil-kids) 부서를 찾을 수 없어요.', 1;
DECLARE @lobby int = (SELECT TOP 1 Id FROM app.Tenant WHERE IsLobby = 1);
DECLARE @basic nvarchar(450) = (SELECT TOP 1 Id FROM [Identity].[Role] WHERE NormalizedName = N'BASIC');
DECLARE @admins TABLE (Id nvarchar(450) PRIMARY KEY);
INSERT @admins SELECT DISTINCT ur.UserId FROM [Identity].[UserRoles] ur JOIN [Identity].[Role] r ON r.Id = ur.RoleId WHERE r.NormalizedName IN (N'ADMIN', N'SUPERADMIN');

-- 1. 계정 짝(예전 Id → 새 Id): 같은 이메일, 없으면 같은 아이디. 관리자면 건드리지 않는다
DECLARE @UM TABLE (OldId nvarchar(450) PRIMARY KEY, NewId nvarchar(450) NULL, Kind nvarchar(10) NULL);
INSERT @UM (OldId, NewId)
SELECT u.Id, (SELECT TOP 1 x.Id FROM [Identity].[User] x
              WHERE (u.Email IS NOT NULL AND x.NormalizedEmail = UPPER(u.Email)) OR x.NormalizedUserName = UPPER(u.UserName) OR x.Id = u.Id
              ORDER BY CASE WHEN u.Email IS NOT NULL AND x.NormalizedEmail = UPPER(u.Email) THEN 0 WHEN x.NormalizedUserName = UPPER(u.UserName) THEN 1 ELSE 2 END)
FROM @U u;
UPDATE @UM SET Kind = CASE WHEN NewId IS NULL THEN N'new' WHEN NewId IN (SELECT Id FROM @admins) THEN N'admin' ELSE N'update' END;
UPDATE @UM SET NewId = OldId WHERE Kind = N'new';

UPDATE x SET PasswordHash = u.PasswordHash, SecurityStamp = ISNULL(u.SecurityStamp, CONVERT(nvarchar(36), NEWID())), ConcurrencyStamp = CONVERT(nvarchar(36), NEWID()),
             FirstName = u.FirstName, LastName = u.LastName, PhoneNumber = u.PhoneNumber
FROM [Identity].[User] x JOIN @UM um ON um.NewId = x.Id AND um.Kind = N'update' JOIN @U u ON u.Id = um.OldId;

INSERT [Identity].[User] (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber,
                          PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount, FirstName, LastName, ProfilePicture, UsernameChangeLimit, insertDT)
SELECT u.Id, u.UserName, UPPER(u.UserName), u.Email, UPPER(u.Email), u.EmailConfirmed, u.PasswordHash, ISNULL(u.SecurityStamp, CONVERT(nvarchar(36), NEWID())), CONVERT(nvarchar(36), NEWID()), u.PhoneNumber,
       0, 0, NULL, 1, 0, u.FirstName, u.LastName, NULL, u.UsernameChangeLimit, ISNULL(u.InsertDT, @now)
FROM @U u JOIN @UM um ON um.OldId = u.Id AND um.Kind = N'new';
INSERT [Identity].[UserRoles] (UserId, RoleId) SELECT um.NewId, @basic FROM @UM um WHERE um.Kind = N'new' AND @basic IS NOT NULL;

-- 2. 소속: 예전 역할로 유년부에. 관리자 · 다른 정식 부서 소속은 그대로 두고, 역할이 없던 새 계정은 임시 부서(있으면)
DECLARE @MB TABLE (NewId nvarchar(450) PRIMARY KEY, RoleCode nvarchar(50) NULL, Result nvarchar(10) NOT NULL);
INSERT @MB (NewId, RoleCode, Result)
SELECT um.NewId, u.RoleCode,
       CASE WHEN um.Kind = N'admin' THEN N'admin'
            WHEN u.RoleCode IS NULL THEN N'norole'
            WHEN EXISTS (SELECT 1 FROM dbo.DeptRoleUserAssoc d JOIN app.Tenant t ON t.Id = d.TenantId WHERE d.UserId = um.NewId AND t.IsLobby = 0 AND d.TenantId <> @tid) THEN N'otherdept'
            ELSE N'kids' END
FROM @UM um JOIN @U u ON u.Id = um.OldId;
DECLARE @fromLobby int = (SELECT COUNT(*) FROM @MB m WHERE m.Result = N'kids' AND EXISTS (SELECT 1 FROM dbo.DeptRoleUserAssoc d WHERE d.UserId = m.NewId AND d.TenantId = @lobby));
DELETE d FROM dbo.DeptRoleUserAssoc d JOIN @MB m ON m.NewId = d.UserId AND m.Result = N'kids' WHERE d.TenantId <> @tid;
UPDATE d SET RoleCode = m.RoleCode, updateDT = @now FROM dbo.DeptRoleUserAssoc d JOIN @MB m ON m.NewId = d.UserId AND m.Result = N'kids' WHERE d.TenantId = @tid;
INSERT dbo.DeptRoleUserAssoc (TenantId, RoleCode, UserId, InsertDT, updateDT)
SELECT @tid, m.RoleCode, m.NewId, @now, @now FROM @MB m WHERE m.Result = N'kids' AND NOT EXISTS (SELECT 1 FROM dbo.DeptRoleUserAssoc d WHERE d.TenantId = @tid AND d.UserId = m.NewId);
INSERT dbo.DeptRoleUserAssoc (TenantId, RoleCode, UserId, InsertDT, updateDT)
SELECT @lobby, N'T1001', m.NewId, @now, @now FROM @MB m JOIN @UM um ON um.NewId = m.NewId AND um.Kind = N'new'
WHERE m.Result = N'norole' AND @lobby IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.DeptRoleUserAssoc d WHERE d.UserId = m.NewId);

-- 3. 유년부 명부 비우기(명단 올리기와 같다, R-301 · R-302): 계정 없는 줄 · 반 배정 · 그 사람들 출석 · 신청. 고른 · 산 · 준 선물이 있는 줄은 남긴다(R-322)
DECLARE @all TABLE (Id int PRIMARY KEY);
INSERT @all SELECT Id FROM app.Person WHERE TenantId = @tid AND UserId IS NULL;
DECLARE @keep TABLE (Id int PRIMARY KEY);
INSERT @keep SELECT DISTINCT g.PersonId FROM app.Gift g JOIN @all a ON a.Id = g.PersonId WHERE g.TenantId = @tid AND g.State IN (N'Picked', N'Purchased', N'Delivered');
DECLARE @drop TABLE (Id int PRIMARY KEY);
INSERT @drop SELECT Id FROM @all WHERE Id NOT IN (SELECT Id FROM @keep);
DECLARE @dropAtt int = (SELECT COUNT(*) FROM app.AttendanceCheck a JOIN @all x ON x.Id = a.PersonId WHERE a.TenantId = @tid);
DELETE l FROM app.GiftLog l JOIN app.Gift g ON g.TenantId = l.TenantId AND g.Id = l.GiftId JOIN @drop d ON d.Id = g.PersonId WHERE g.TenantId = @tid;
DELETE u FROM app.GiftPointUse u JOIN app.Gift g ON g.TenantId = u.TenantId AND g.Id = u.GiftId JOIN @drop d ON d.Id = g.PersonId WHERE g.TenantId = @tid;
DELETE g FROM app.Gift g JOIN @drop d ON d.Id = g.PersonId WHERE g.TenantId = @tid;
DELETE a FROM app.AttendanceCheck a JOIN @all x ON x.Id = a.PersonId WHERE a.TenantId = @tid;
DELETE FROM app.ClassMember WHERE TenantId = @tid;
DELETE s FROM dbo.SignupApplication s JOIN @all x ON x.Id = s.PersonId WHERE s.TenantId = @tid;
DELETE p FROM app.Person p JOIN @drop d ON d.Id = p.Id WHERE p.TenantId = @tid;

-- 4. 명부 줄 짝(예전 PersonId → 새 Person.Id)
DECLARE @PM TABLE (OldPid int PRIMARY KEY, NewPid int NULL, UserId nvarchar(450) NULL, Kind nvarchar(10) NOT NULL);
INSERT @PM (OldPid, NewPid, UserId, Kind)
SELECT p.OldPid, NULL, m.NewId,
       CASE WHEN m.Result = N'admin' THEN N'admin' WHEN m.Result = N'kids' THEN N'linked' WHEN m.Result = N'otherdept' THEN N'skip' ELSE N'roster' END
FROM @P p
LEFT JOIN @U u ON p.UserName IS NOT NULL AND u.UserName = p.UserName
LEFT JOIN @UM um ON um.OldId = u.Id
LEFT JOIN @MB m ON m.NewId = um.NewId;
UPDATE @PM SET UserId = NULL WHERE Kind = N'roster';
-- 같은 계정에 예전 명부 줄이 둘 이상이면 하나만 잇는다
WITH d AS (SELECT Kind, ROW_NUMBER() OVER (PARTITION BY UserId ORDER BY OldPid) n FROM @PM WHERE UserId IS NOT NULL) UPDATE d SET Kind = N'skip' WHERE n > 1;
-- 관리자 · 계정 있는 사람: 유년부에 이미 있는 줄
UPDATE pm SET NewPid = x.Id FROM @PM pm JOIN app.Person x ON x.TenantId = @tid AND x.UserId = pm.UserId WHERE pm.Kind IN (N'admin', N'linked');
-- 남긴 줄(선물): 같은 이름이 한 줄뿐이면 그 줄을 다시 쓴다
UPDATE pm SET NewPid = x.Id
FROM @PM pm JOIN @P p ON p.OldPid = pm.OldPid
JOIN app.Person x ON x.TenantId = @tid AND x.UserId IS NULL AND x.Id IN (SELECT Id FROM @keep) AND x.Name = p.Name
WHERE pm.Kind = N'roster' AND (SELECT COUNT(*) FROM @P q WHERE q.Name = p.Name) = 1 AND (SELECT COUNT(*) FROM app.Person y WHERE y.TenantId = @tid AND y.UserId IS NULL AND y.Id IN (SELECT Id FROM @keep) AND y.Name = p.Name) = 1;

UPDATE x SET BirthMonth = ISNULL(p.BirthMonth, x.BirthMonth), BirthDay = CASE WHEN p.BirthMonth IS NULL THEN x.BirthDay ELSE p.BirthDay END,
             Phone1 = ISNULL(p.Phone1, x.Phone1), Phone2 = ISNULL(p.Phone2, x.Phone2),
             Name = CASE WHEN pm.Kind = N'roster' THEN p.Name ELSE x.Name END, RoleCode = CASE WHEN pm.Kind = N'roster' THEN p.RoleCode ELSE x.RoleCode END
FROM app.Person x JOIN @PM pm ON pm.NewPid = x.Id AND pm.Kind IN (N'linked', N'roster') JOIN @P p ON p.OldPid = pm.OldPid WHERE x.TenantId = @tid;

DECLARE @ins TABLE (OldPid int PRIMARY KEY, NewPid int);
MERGE app.Person AS t
USING (SELECT pm.OldPid, pm.UserId, pm.Kind, p.Name, p.RoleCode, p.BirthMonth, p.BirthDay, p.Phone1, p.Phone2
       FROM @PM pm JOIN @P p ON p.OldPid = pm.OldPid WHERE pm.NewPid IS NULL AND pm.Kind IN (N'linked', N'roster')) AS s
ON 1 = 0
WHEN NOT MATCHED THEN INSERT (TenantId, Name, RoleCode, BirthMonth, BirthDay, Phone1, Phone2, UserId, CreatedUtc, CreatedByUserName)
     VALUES (@tid, s.Name, CASE WHEN s.Kind = N'linked' THEN NULL ELSE s.RoleCode END, s.BirthMonth, s.BirthDay, s.Phone1, s.Phone2, s.UserId, @now, N'yuyunbu-import')
OUTPUT s.OldPid, inserted.Id INTO @ins (OldPid, NewPid);
UPDATE pm SET NewPid = i.NewPid FROM @PM pm JOIN @ins i ON i.OldPid = pm.OldPid;

-- 5. 반: 반 묶음 · 새친구반이 없으면 만들고, 예전 반 이름(ClassMaster)으로 반을 만든다. 한 사람은 한 반에만(R-232), 담임 · 부담임은 선생님으로
DECLARE @term int = (SELECT TOP 1 Id FROM app.ClassTerm WHERE TenantId = @tid ORDER BY Id);
IF @term IS NULL
BEGIN
    INSERT app.ClassTerm (TenantId, Year, StartDate, CreatedUtc) VALUES (@tid, YEAR(@now), CAST(@now AS date), @now);
    SET @term = SCOPE_IDENTITY();
END;
IF NOT EXISTS (SELECT 1 FROM app.Class WHERE TenantId = @tid AND TermId = @term AND IsNewcomer = 1)
    INSERT app.Class (TenantId, TermId, Name, IsNewcomer, CreatedUtc) VALUES (@tid, @term, N'새친구반', 1, @now);
DECLARE @newClasses int = (SELECT COUNT(*) FROM @C c WHERE NOT EXISTS (SELECT 1 FROM app.Class k WHERE k.TenantId = @tid AND k.TermId = @term AND k.Name = c.Name));
INSERT app.Class (TenantId, TermId, Name, IsNewcomer, CreatedUtc)
SELECT @tid, @term, c.Name, 0, @now FROM @C c WHERE NOT EXISTS (SELECT 1 FROM app.Class k WHERE k.TenantId = @tid AND k.TermId = @term AND k.Name = c.Name);
INSERT app.ClassMember (TenantId, TermId, ClassId, PersonId, IsTeacher, CreatedUtc)
SELECT @tid, @term, x.ClassId, x.NewPid, x.IsTeacher, @now
FROM (SELECT k.Id ClassId, pm.NewPid, m.IsTeacher, ROW_NUMBER() OVER (PARTITION BY pm.NewPid ORDER BY m.IsTeacher DESC, k.Id) n
      FROM @M m JOIN @C c ON c.ClassYear = m.ClassYear AND c.ClassBan = m.ClassBan
      JOIN app.Class k ON k.TenantId = @tid AND k.TermId = @term AND k.Name = c.Name AND k.IsNewcomer = 0
      JOIN @PM pm ON pm.OldPid = m.OldPid AND pm.NewPid IS NOT NULL) x
WHERE x.n = 1;

-- 6. 기록 · 결과
INSERT app.AuditLog (OccurredUtc, ActorUserName, Action, TenantId, Detail)
VALUES (@now, N'yuyunbu-import', N'YuyunbuImport', @tid,
        CONCAT(N'new=', (SELECT COUNT(*) FROM @UM WHERE Kind = N'new'), N';update=', (SELECT COUNT(*) FROM @UM WHERE Kind = N'update'),
               N';people=', (SELECT COUNT(*) FROM @PM WHERE NewPid IS NOT NULL), N';dropped=', (SELECT COUNT(*) FROM @drop)));
SELECT N'계정 새로' AS [항목], COUNT(*) AS [수] FROM @UM WHERE Kind = N'new'
UNION ALL SELECT N'계정 덮어씀(같은 이메일 · 아이디)', COUNT(*) FROM @UM WHERE Kind = N'update'
UNION ALL SELECT N'관리자라 건너뜀', COUNT(*) FROM @UM WHERE Kind = N'admin'
UNION ALL SELECT N'유년부 소속', COUNT(*) FROM @MB WHERE Result = N'kids'
UNION ALL SELECT N'└ 임시 부서에서 옮김', @fromLobby
UNION ALL SELECT N'다른 부서 소속이라 그대로', COUNT(*) FROM @MB WHERE Result = N'otherdept'
UNION ALL SELECT N'예전 역할 없음', COUNT(*) FROM @MB WHERE Result = N'norole'
UNION ALL SELECT N'명부 줄(계정과 이음)', COUNT(*) FROM @PM WHERE Kind = N'linked' AND NewPid IS NOT NULL
UNION ALL SELECT N'명부 줄(계정 없음)', COUNT(*) FROM @PM WHERE Kind = N'roster' AND NewPid IS NOT NULL
UNION ALL SELECT N'명부 줄 건너뜀', COUNT(*) FROM @PM WHERE Kind = N'skip'
UNION ALL SELECT N'새로 만든 반', @newClasses
UNION ALL SELECT N'반 배정(학생 · 선생님)', (SELECT COUNT(*) FROM app.ClassMember WHERE TenantId = @tid)
UNION ALL SELECT N'비운 명부 줄(예전 새 사이트 것)', (SELECT COUNT(*) FROM @drop)
UNION ALL SELECT N'└ 함께 지운 출석', @dropAtt
UNION ALL SELECT N'선물이 있어 남긴 줄', (SELECT COUNT(*) FROM @keep);
SELECT u.UserName AS [건너뛴 계정], u.Email AS [이메일], CASE m.Result WHEN N'admin' THEN N'관리자(바꾸지 않음)' WHEN N'otherdept' THEN N'다른 부서 소속' ELSE N'예전 역할 없음' END AS [까닭]
FROM @MB m JOIN @UM um ON um.NewId = m.NewId JOIN @U u ON u.Id = um.OldId WHERE m.Result <> N'kids' ORDER BY m.Result, u.UserName;
