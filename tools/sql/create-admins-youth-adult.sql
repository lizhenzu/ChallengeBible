-- 청년부 · 장년부 부서 관리자 만들기(사이트 SQL 화면용, 2026-11-18 사용자)
-- 영아부 관리자(jamsil-infant-admin)를 본으로: 비밀번호(암호값) · 이메일 끝 · 역할 · 소속 역할 코드 · 관리자 꼬리표를 그대로 복사. 본 계정은 읽기만 한다(R-315).
-- 아이디 jamsil-youth-admin(관리청) · jamsil-adult-admin(관리장). 이미 있으면 건너뛴다.
DECLARE @At datetime2(3) = '2026-11-18T10:10:00';
DECLARE @Tpl nvarchar(450) = (SELECT Id FROM [Identity].[User] WHERE NormalizedUserName = 'JAMSIL-INFANT-ADMIN');
IF @Tpl IS NULL THROW 50001, N'본 계정 jamsil-infant-admin 이 없어요.', 1;
DECLARE @TplTenant int = (SELECT TOP 1 TenantId FROM dbo.DeptRoleUserAssoc WHERE UserId = @Tpl);
DECLARE @TplRole nvarchar(50) = (SELECT TOP 1 RoleCode FROM dbo.DeptRoleUserAssoc WHERE UserId = @Tpl);
DECLARE @Domain nvarchar(256) = (SELECT SUBSTRING(Email, CHARINDEX('@', Email), 256) FROM [Identity].[User] WHERE Id = @Tpl);
DECLARE @New TABLE (Ord int, Slug varchar(40), Ch nchar(1));
INSERT @New VALUES (1, 'jamsil-youth', N'청'), (2, 'jamsil-adult', N'장');
DECLARE @Ord int = 1, @Slug varchar(40), @Ch nchar(1), @T int, @U nvarchar(450), @Name nvarchar(256), @Login nvarchar(256);
WHILE @Ord <= 2
BEGIN
    SELECT @Slug = Slug, @Ch = Ch FROM @New WHERE Ord = @Ord;
    SET @T = (SELECT Id FROM app.Tenant WHERE Slug = @Slug);
    SET @Login = @Slug + '-admin';
    IF @T IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [Identity].[User] WHERE NormalizedUserName = UPPER(@Login))
    BEGIN
        SET @U = LOWER(CONVERT(nvarchar(36), NEWID()));
        INSERT [Identity].[User] (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp,
                                  PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, FirstName, LastName, UsernameChangeLimit, insertDT)
        SELECT @U, @Login, UPPER(@Login), @Login + @Domain, UPPER(@Login + @Domain), EmailConfirmed, PasswordHash,
               UPPER(REPLACE(CONVERT(nvarchar(36), NEWID()), '-', '')), LOWER(CONVERT(nvarchar(36), NEWID())),
               0, 0, 1, 0, LEFT(FirstName, LEN(FirstName) - 1) + @Ch, LastName, 0, @At
        FROM [Identity].[User] WHERE Id = @Tpl;
        INSERT [Identity].[UserRoles] (UserId, RoleId) SELECT @U, RoleId FROM [Identity].[UserRoles] WHERE UserId = @Tpl;
        INSERT dbo.DeptRoleUserAssoc (TenantId, UserId, RoleCode, InsertDT, updateDT) VALUES (@T, @U, @TplRole, @At, @At);
        INSERT app.MemberTag (TenantId, UserId, Tag, CreatedUtc, CreatedByUserName)
        SELECT @T, @U, Tag, @At, CreatedByUserName FROM app.MemberTag WHERE UserId = @Tpl AND TenantId = @TplTenant;
        SET @Name = (SELECT ISNULL(LastName, '') + ISNULL(FirstName, '') FROM [Identity].[User] WHERE Id = @U);
        INSERT app.Person (TenantId, Name, UserId, CreatedUtc, CreatedByUserName) VALUES (@T, @Name, @U, @At, 'superadmin');
    END
    SET @Ord += 1;
END
SELECT t.Name AS 부서, u.UserName AS 아이디, ISNULL(u.LastName, '') + ISNULL(u.FirstName, '') AS 이름, d.RoleCode AS 역할코드,
       (SELECT COUNT(*) FROM [Identity].[UserRoles] r WHERE r.UserId = u.Id) AS 권한수,
       (SELECT COUNT(*) FROM app.Person p WHERE p.UserId = u.Id) AS 명부,
       CASE WHEN u.PasswordHash = (SELECT PasswordHash FROM [Identity].[User] WHERE Id = @Tpl) THEN N'같음' ELSE N'다름' END AS 비밀번호
FROM [Identity].[User] u JOIN dbo.DeptRoleUserAssoc d ON d.UserId = u.Id JOIN app.Tenant t ON t.Id = d.TenantId
WHERE u.NormalizedUserName IN ('JAMSIL-INFANT-ADMIN', 'JAMSIL-YOUTH-ADMIN', 'JAMSIL-ADULT-ADMIN') ORDER BY t.Id;
