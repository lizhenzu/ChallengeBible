-- migrate-shared-tables.sql 실행 전 점검 (읽기만, 끝에 되돌린다). 파일 전체를 실행한다.
-- 결과 1 ~ 5 표를 보고, "판정" 칸이 모두 OK 인지 확인한다.
SET NOCOUNT ON;
BEGIN TRANSACTION;
EXEC sp_set_session_context N'PlatformScope', 1;   -- RLS 때문에 0건으로 보이지 않게(이 연결에만)

-- 1. 이미 옮겼는가 · 마지막 마이그레이션
SELECT N'1 적용 여부' AS [점검],
       CASE WHEN EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE MigrationId = N'20261110032322_SharedTables') THEN N'이미 옮김(다시 실행해도 아무 일 없음)' ELSE N'아직 안 옮김' END AS [결과],
       (SELECT TOP 1 MigrationId FROM [__EFMigrationsHistory] ORDER BY MigrationId DESC) AS [마지막 마이그레이션],
       CASE WHEN (SELECT TOP 1 MigrationId FROM [__EFMigrationsHistory] WHERE MigrationId < N'20261110032322_SharedTables' ORDER BY MigrationId DESC) = N'20261106224302_FamilyInviteSnooze'
            THEN N'OK' ELSE N'확인 필요: 바로 앞이 20261106224302_FamilyInviteSnooze 여야 함' END AS [판정];

-- 2. 새로 만들 표와 이름이 같은 표가 이미 있는가(있으면 실행이 실패하고 전부 되돌아간다)
SELECT N'2 이름 겹침' AS [점검], v.N AS [표], CASE WHEN OBJECT_ID(v.N, N'U') IS NULL THEN N'OK' ELSE N'이미 있음 - 실행 전에 알려 주세요' END AS [판정]
FROM (VALUES (N'dbo.CodeData'), (N'dbo.DeptRoleUserAssoc'), (N'dbo.SignupApplication'),
             (N'dbo.DM2_ReadingBookmark'), (N'dbo.DM2_ReadingRound'), (N'dbo.DM2_ReadingChapterProgress'), (N'dbo.DM2_ReadingChapterRead'), (N'dbo.DM2_ReadingVerse'),
             (N'dbo.DM2_FamilyMilestoneConfig'), (N'dbo.DM2_FamilyNotice'), (N'dbo.DM2_FamilyTeam'), (N'dbo.DM2_MissionActivity'), (N'dbo.DM2_QuizAttempt'),
             (N'dbo.DM2_TeamAward'), (N'dbo.DM2_FamilyMilestoneHidden'), (N'dbo.DM2_FamilyMember'), (N'dbo.DM2_FamilyRequest'),
             (N'Identity.Role'), (N'Identity.User'), (N'Identity.RoleClaims'), (N'Identity.UserClaims'), (N'Identity.UserLogins'), (N'Identity.UserRoles'), (N'Identity.UserTokens'),
             (N'app.UserSetting')) v(N)
WHERE NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE MigrationId = N'20261110032322_SharedTables')
ORDER BY CASE WHEN OBJECT_ID(v.N, N'U') IS NULL THEN 1 ELSE 0 END, v.N;

-- 3 ~ 5 는 아직 옮기지 않았을 때만 의미가 있다
IF OBJECT_ID(N'app.Account', N'U') IS NOT NULL
BEGIN
    -- 3. 남는 사람: 활성 플랫폼 관리자. 0명이면 아무도 로그인할 수 없다(콘텐츠가 있으면 실행도 실패). 아이디(이메일 @ 앞)가 겹치면 실패한다
    EXEC(N'SELECT N''3 남는 관리자'' AS [점검], LEFT(a.Email, CHARINDEX(N''@'', a.Email + N''@'') - 1) AS [새 아이디], a.DisplayName AS [이름],
               (SELECT COUNT(*) FROM app.Account b WHERE b.IsPlatformAdmin = 1 AND b.Status = N''Active''
                  AND LEFT(b.Email, CHARINDEX(N''@'', b.Email + N''@'') - 1) = LEFT(a.Email, CHARINDEX(N''@'', a.Email + N''@'') - 1)) AS [같은 아이디 수],
               CASE WHEN (SELECT COUNT(*) FROM app.Account b WHERE b.IsPlatformAdmin = 1 AND b.Status = N''Active''
                  AND LEFT(b.Email, CHARINDEX(N''@'', b.Email + N''@'') - 1) = LEFT(a.Email, CHARINDEX(N''@'', a.Email + N''@'') - 1)) = 1 THEN N''OK'' ELSE N''아이디 겹침 - 알려 주세요'' END AS [판정]
         FROM app.Account a WHERE a.IsPlatformAdmin = 1 AND a.Status = N''Active'';
         IF NOT EXISTS (SELECT 1 FROM app.Account WHERE IsPlatformAdmin = 1 AND Status = N''Active'')
             SELECT N''3 남는 관리자'' AS [점검], N''0명 - 실행하지 말고 알려 주세요'' AS [판정];');

    -- 4. 지워지는 것(되돌릴 수 없음, 실행 전에 DB 백업)
    EXEC(N'SELECT N''4 지워지는 것'' AS [점검],
               (SELECT COUNT(*) FROM app.Account WHERE NOT (IsPlatformAdmin = 1 AND Status = N''Active'')) AS [계정],
               (SELECT COUNT(*) FROM app.Membership) AS [소속],
               (SELECT COUNT(*) FROM app.FamilyTeam) AS [가족팀],
               (SELECT COUNT(*) FROM app.Activity) AS [미션 기록],
               (SELECT COUNT(*) FROM app.PointLedger) AS [점수 기록],
               (SELECT COUNT(*) FROM app.ChapterRead) AS [통독 장],
               (SELECT COUNT(*) FROM app.AuditLog) AS [감사 로그];');

    -- 5. 남는 콘텐츠(바뀌지 않음, 만든 사람 칸만 아이디로)
    EXEC(N'SELECT N''5 남는 것'' AS [점검],
               (SELECT COUNT(*) FROM app.Tenant) AS [부서],
               (SELECT COUNT(*) FROM app.DailyWord) AS [말씀],
               (SELECT COUNT(*) FROM app.DailyQuiz) AS [퀴즈],
               (SELECT COUNT(*) FROM app.DailyPrayer) AS [기도],
               (SELECT COUNT(*) FROM app.WeeklyVerse) AS [암송],
               (SELECT COUNT(*) FROM app.BibleVerse) AS [성경 절];');
END;

ROLLBACK;
EXEC sp_set_session_context N'PlatformScope', NULL;
PRINT N'점검 끝(바꾼 것 없음).';
