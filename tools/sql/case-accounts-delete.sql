-- case-accounts-delete.sql : one-time cleanup of old [CASE] test accounts (2026-11-16, user approved "1")
-- Target: accounts whose UserName looks like '<dept>-k-<role>' AND email ends with @fake-seed.test.
-- Never touches: '<dept>-admin' operating admins, SuperAdmin accounts, real accounts, classes.
-- Run in SSMS. @Commit = 0 shows the list and rolls back. Check the list, then set @Commit = 1 and run again.
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Commit bit = 0;   -- 0 = preview only (rollback), 1 = delete

EXEC sp_set_session_context N'PlatformScope', 1;   -- cross tenant isolation on this connection only
BEGIN TRY
BEGIN TRANSACTION;

CREATE TABLE #Del (UserId nvarchar(450) COLLATE DATABASE_DEFAULT PRIMARY KEY, UserName nvarchar(256) COLLATE DATABASE_DEFAULT);
INSERT #Del
SELECT u.Id, u.UserName FROM [Identity].[User] u
WHERE u.UserName LIKE N'%-k-%'
  AND u.Email LIKE N'%@fake-seed.test'
  AND u.UserName NOT LIKE N'%-admin'
  AND NOT EXISTS (SELECT 1 FROM [Identity].[UserRoles] ur JOIN [Identity].[Role] r ON r.Id = ur.RoleId
                  WHERE ur.UserId = u.Id AND r.NormalizedName = N'SUPERADMIN');

CREATE TABLE #DelPerson (TenantId int, PersonId int);
INSERT #DelPerson SELECT p.TenantId, p.Id FROM app.Person p WHERE p.UserId IN (SELECT UserId FROM #Del);

-- 1) list (check this before @Commit = 1)
SELECT N'to delete' AS [Step], t.Slug AS [Dept], d.UserName, u.LastName + u.FirstName AS [Name], u.Email
FROM #Del d JOIN [Identity].[User] u ON u.Id = d.UserId
LEFT JOIN dbo.DeptRoleUserAssoc a ON a.UserId = d.UserId
LEFT JOIN app.Tenant t ON t.Id = a.TenantId
ORDER BY t.Slug, d.UserName;

-- 2) rows that hang on these accounts
DELETE a FROM app.AttendanceCheck a JOIN #DelPerson x ON x.TenantId = a.TenantId AND x.PersonId = a.PersonId;
DELETE m FROM app.ClassMember m JOIN #DelPerson x ON x.TenantId = m.TenantId AND x.PersonId = m.PersonId;
DELETE l FROM app.GiftLog l JOIN app.Gift g ON g.TenantId = l.TenantId AND g.Id = l.GiftId
WHERE g.UserName IN (SELECT UserName FROM #Del) OR EXISTS (SELECT 1 FROM #DelPerson x WHERE x.TenantId = g.TenantId AND x.PersonId = g.PersonId);
DELETE FROM app.GiftPointUse WHERE UserName IN (SELECT UserName FROM #Del);
DELETE g FROM app.Gift g
WHERE g.UserName IN (SELECT UserName FROM #Del) OR EXISTS (SELECT 1 FROM #DelPerson x WHERE x.TenantId = g.TenantId AND x.PersonId = g.PersonId);
DELETE FROM app.GuardianLink WHERE GuardianUserName IN (SELECT UserName FROM #Del) OR StudentUserName IN (SELECT UserName FROM #Del);
DELETE FROM dbo.DM2_MissionActivity WHERE UserName IN (SELECT UserName FROM #Del);
DELETE FROM app.MemberTag WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM dbo.SignupApplication WHERE UserId IN (SELECT UserId FROM #Del);
DELETE p FROM app.Person p JOIN #DelPerson x ON x.TenantId = p.TenantId AND x.PersonId = p.Id;

-- 3) the accounts
DELETE FROM dbo.DeptRoleUserAssoc WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM app.UserSetting WHERE UserName IN (SELECT UserName FROM #Del);
DELETE FROM [Identity].[UserTokens] WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM [Identity].[UserLogins] WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM [Identity].[UserClaims] WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM [Identity].[UserRoles] WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM [Identity].[User] WHERE Id IN (SELECT UserId FROM #Del);

SELECT N'accounts' AS [Step], (SELECT COUNT(*) FROM #Del) AS [Deleted],
       (SELECT COUNT(*) FROM [Identity].[User] WHERE UserName LIKE N'%-admin' AND Email LIKE N'%@fake-seed.test') AS [DeptAdminsKept];

IF @Commit = 1 BEGIN COMMIT; SELECT N'DONE: deleted.' AS [Result]; END
ELSE BEGIN ROLLBACK; SELECT N'DONE: rolled back (preview only).' AS [Result]; END
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    EXEC sp_set_session_context N'PlatformScope', NULL;
    THROW;
END CATCH;
EXEC sp_set_session_context N'PlatformScope', NULL;
