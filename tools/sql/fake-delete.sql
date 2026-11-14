SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET QUOTED_IDENTIFIER ON; SET NUMERIC_ROUNDABORT OFF;
-- ============================================================================
-- 가짜 시험 데이터 지우기 (생성기 tools/sql/gen-fake-seed.py, 2026-11-14)
-- fake-create.sql 이 넣은 것만 지운다: 이메일 @fake-seed.test 계정과 그 기록, 명부 · 출석 · 보호 관계(CreatedByUserName = FAKE-SEED-20261114),
-- 가짜 계정이 만든 가족팀 · 친구팀과 그 시상 기록, 가짜 반(만든 시각 2026-01-01 00:00:00.123)
-- 실행: 먼저 @Commit = 0(시험 실행) → 결과 표 확인 → @Commit = 1
-- ============================================================================
DECLARE @Commit bit = 0;                 -- 0 = 시험 실행(끝에 되돌림), 1 = 진짜 실행
DECLARE @Marker nvarchar(50) = N'FAKE-SEED-20261114';
DECLARE @MarkTime datetime2(3) = '2026-01-01T00:00:00.123';
DECLARE @Step nvarchar(20);

EXEC sp_set_session_context N'PlatformScope', 1;

BEGIN TRY
BEGIN TRANSACTION;

SET @Step = N'0 지우기 전';
SELECT @Step AS [단계],
       (SELECT COUNT(*) FROM [Identity].[User]) AS [계정],
       (SELECT COUNT(*) FROM dbo.DeptRoleUserAssoc) AS [소속],
       (SELECT COUNT(*) FROM app.Person) AS [명부],
       (SELECT COUNT(*) FROM app.ClassMember) AS [반 배정],
       (SELECT COUNT(*) FROM app.AttendanceCheck) AS [출석],
       (SELECT COUNT(*) FROM dbo.DM2_MissionActivity) AS [미션 기록],
       (SELECT COUNT(*) FROM dbo.DM2_FamilyTeam) AS [팀],
       (SELECT COUNT(*) FROM dbo.DM2_FamilyMember) AS [팀 구성원],
       (SELECT COUNT(*) FROM dbo.DM2_TeamAward) AS [시상 기록],
       (SELECT COUNT(*) FROM app.GuardianLink) AS [보호 관계],
       (SELECT COUNT(*) FROM dbo.DM2_ReadingRound) AS [통독 회차],
       (SELECT COUNT(*) FROM dbo.DM2_ReadingChapterRead) AS [통독 장],
       (SELECT COUNT(*) FROM dbo.SignupApplication) AS [참여 신청],
       (SELECT COUNT(*) FROM app.AuditLog) AS [감사 로그];

CREATE TABLE #Del (UserId nvarchar(450) COLLATE DATABASE_DEFAULT PRIMARY KEY, UserName nvarchar(256) COLLATE DATABASE_DEFAULT);
INSERT #Del SELECT Id, UserName FROM [Identity].[User] WHERE Email LIKE N'%@fake-seed.test';
CREATE TABLE #DelTeam (TenantId int, TeamId int, AwardKey nvarchar(200) COLLATE DATABASE_DEFAULT);
INSERT #DelTeam SELECT TenantId, FamilyTeamId, ISNULL(AwardKey, TeamName) FROM dbo.DM2_FamilyTeam WHERE CreatorUserName IN (SELECT UserName FROM #Del);
CREATE TABLE #DelPerson (TenantId int, PersonId int);
INSERT #DelPerson SELECT TenantId, Id FROM app.Person WHERE CreatedByUserName = @Marker OR UserId IN (SELECT UserId FROM #Del);

DELETE a FROM app.AttendanceCheck a JOIN #DelPerson d ON d.TenantId = a.TenantId AND d.PersonId = a.PersonId;
DELETE FROM app.AttendanceCheck WHERE CreatedByUserName = @Marker;
DELETE m FROM app.ClassMember m JOIN #DelPerson d ON d.TenantId = m.TenantId AND d.PersonId = m.PersonId;
DELETE r FROM dbo.DM2_FamilyRequest r JOIN #DelTeam t ON t.TenantId = r.TenantId AND t.TeamId = r.FamilyTeamId;
DELETE FROM dbo.DM2_FamilyRequest WHERE TargetUserName IN (SELECT UserName FROM #Del) OR FromUserName IN (SELECT UserName FROM #Del);
DELETE FROM dbo.DM2_FamilyNotice WHERE UserName IN (SELECT UserName FROM #Del);
DELETE h FROM dbo.DM2_FamilyMilestoneHidden h JOIN #DelTeam t ON t.TenantId = h.TenantId AND t.TeamId = h.FamilyTeamId;
DELETE a FROM dbo.DM2_TeamAward a JOIN #DelTeam t ON t.TenantId = a.TenantId AND t.AwardKey = a.AwardKey;
DELETE m FROM dbo.DM2_FamilyMember m JOIN #DelTeam t ON t.TenantId = m.TenantId AND t.TeamId = m.FamilyTeamId;
DELETE FROM dbo.DM2_FamilyMember WHERE UserName IN (SELECT UserName FROM #Del);
DELETE f FROM dbo.DM2_FamilyTeam f JOIN #DelTeam t ON t.TenantId = f.TenantId AND t.TeamId = f.FamilyTeamId;
DELETE FROM app.GuardianLink WHERE CreatedByUserName = @Marker OR GuardianUserName IN (SELECT UserName FROM #Del) OR StudentUserName IN (SELECT UserName FROM #Del);
DELETE FROM app.PromotionRequest WHERE UserName IN (SELECT UserName FROM #Del);
DELETE FROM dbo.DM2_QuizAttempt WHERE UserName IN (SELECT UserName FROM #Del);
DELETE FROM dbo.DM2_MissionActivity WHERE UserName IN (SELECT UserName FROM #Del);
DELETE FROM dbo.DM2_ReadingVerse WHERE UserName IN (SELECT UserName FROM #Del);
DELETE FROM dbo.DM2_ReadingChapterRead WHERE UserName IN (SELECT UserName FROM #Del);
DELETE FROM dbo.DM2_ReadingChapterProgress WHERE UserName IN (SELECT UserName FROM #Del);
DELETE FROM dbo.DM2_ReadingBookmark WHERE UserName IN (SELECT UserName FROM #Del);
DELETE FROM dbo.DM2_ReadingRound WHERE UserName IN (SELECT UserName FROM #Del);
DELETE p FROM app.Person p JOIN #DelPerson d ON d.TenantId = p.TenantId AND d.PersonId = p.Id;
DELETE FROM dbo.SignupApplication WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM dbo.DeptRoleUserAssoc WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM app.UserSetting WHERE UserName IN (SELECT UserName FROM #Del);
DELETE FROM app.AuditLog WHERE ActorUserName IN (SELECT UserName FROM #Del);
DELETE FROM [Identity].[UserTokens] WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM [Identity].[UserLogins] WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM [Identity].[UserClaims] WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM [Identity].[UserRoles] WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM [Identity].[User] WHERE Id IN (SELECT UserId FROM #Del);

-- 가짜 반: 남은 사람이 없을 때만 지운다(누가 반에 넣었으면 남김)
DELETE c FROM app.Class c WHERE c.CreatedUtc = @MarkTime
  AND NOT EXISTS (SELECT 1 FROM app.ClassMember m WHERE m.TenantId = c.TenantId AND m.ClassId = c.Id);
DELETE t FROM app.ClassTerm t WHERE t.CreatedUtc = @MarkTime
  AND NOT EXISTS (SELECT 1 FROM app.Class c WHERE c.TenantId = t.TenantId AND c.TermId = t.Id);

SET @Step = N'1 지운 뒤';
SELECT @Step AS [단계],
       (SELECT COUNT(*) FROM [Identity].[User]) AS [계정],
       (SELECT COUNT(*) FROM dbo.DeptRoleUserAssoc) AS [소속],
       (SELECT COUNT(*) FROM app.Person) AS [명부],
       (SELECT COUNT(*) FROM app.ClassMember) AS [반 배정],
       (SELECT COUNT(*) FROM app.AttendanceCheck) AS [출석],
       (SELECT COUNT(*) FROM dbo.DM2_MissionActivity) AS [미션 기록],
       (SELECT COUNT(*) FROM dbo.DM2_FamilyTeam) AS [팀],
       (SELECT COUNT(*) FROM dbo.DM2_FamilyMember) AS [팀 구성원],
       (SELECT COUNT(*) FROM dbo.DM2_TeamAward) AS [시상 기록],
       (SELECT COUNT(*) FROM app.GuardianLink) AS [보호 관계],
       (SELECT COUNT(*) FROM dbo.DM2_ReadingRound) AS [통독 회차],
       (SELECT COUNT(*) FROM dbo.DM2_ReadingChapterRead) AS [통독 장],
       (SELECT COUNT(*) FROM dbo.SignupApplication) AS [참여 신청],
       (SELECT COUNT(*) FROM app.AuditLog) AS [감사 로그];

IF @Commit = 1
BEGIN
    COMMIT;
    SELECT N'끝: 지웠습니다(진짜 실행).' AS [결과];
END
ELSE
BEGIN
    ROLLBACK;
    SELECT N'끝: 시험 실행이라 모두 되돌렸습니다. 위 숫자가 맞으면 @Commit = 1 로 바꿔 다시 실행하세요.' AS [결과];
END;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    EXEC sp_set_session_context N'PlatformScope', NULL;
    THROW;
END CATCH;
EXEC sp_set_session_context N'PlatformScope', NULL;
