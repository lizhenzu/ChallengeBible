-- 유년부와 공유 표로 옮기기 (ADR 0090). 한 번만 실행된다(이미 했으면 아무 일도 하지 않는다).
-- 시험 데이터(계정 · 소속 · 가족팀 · 미션 · 통독 기록)는 지워진다. 플랫폼 관리자 계정과 콘텐츠는 남는다.
-- 파일 전체를 실행한다. 실패하면 전체가 되돌아간다.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261110032322_SharedTables')
BEGIN
-- 1. RLS 를 내린다
IF EXISTS (SELECT * FROM sys.security_policies WHERE name = N'TenantIsolation') EXEC(N'DROP SECURITY POLICY sec.TenantIsolation');
EXEC(N'DROP FUNCTION IF EXISTS sec.fn_Tenant');
EXEC(N'DROP FUNCTION IF EXISTS sec.fn_Membership');
EXEC(N'DROP FUNCTION IF EXISTS sec.fn_SharedRead');
EXEC(N'DROP FUNCTION IF EXISTS sec.fn_SharedWrite');
EXEC(N'DROP FUNCTION IF EXISTS sec.fn_Account');
EXEC(N'DROP FUNCTION IF EXISTS sec.fn_ReadingRead');
EXEC(N'DROP FUNCTION IF EXISTS sec.fn_Promotion');
EXEC(N'DROP FUNCTION IF EXISTS sec.fn_Person');

-- 2. 남길 사람을 기억한다: 플랫폼 관리자 계정, 그 사람이 승인 소속인 부서 하나(임시 부서가 아닌 곳 먼저)
CREATE TABLE #keep (AccountId int NOT NULL, NewId nvarchar(450) NOT NULL, UserName nvarchar(256) NOT NULL, Email nvarchar(256) NOT NULL, PasswordHash nvarchar(max) NOT NULL,
    DisplayName nvarchar(50) NOT NULL, PhotoPath nvarchar(260) NULL, ReadingTheme varchar(20) NULL, MissionTheme varchar(20) NULL, LastTenantId int NULL, CreatedUtc datetime2(3) NOT NULL,
    TenantId int NULL, DeptAdmin bit NOT NULL DEFAULT 0, IsTeacher bit NOT NULL DEFAULT 0);
EXEC(N'INSERT #keep (AccountId, NewId, UserName, Email, PasswordHash, DisplayName, PhotoPath, ReadingTheme, MissionTheme, LastTenantId, CreatedUtc)
    SELECT a.Id, LOWER(CONVERT(nvarchar(36), NEWID())), LEFT(a.Email, CHARINDEX(N''@'', a.Email + N''@'') - 1), a.Email, a.PasswordHash, a.DisplayName, a.PhotoPath, a.ReadingTheme, a.MissionTheme, a.LastTenantId, a.CreatedUtc
    FROM app.Account a WHERE a.IsPlatformAdmin = 1 AND a.Status = N''Active'';
    UPDATE k SET k.TenantId = x.TenantId, k.DeptAdmin = x.A, k.IsTeacher = x.T FROM #keep k
    CROSS APPLY (SELECT TOP 1 m.TenantId,
            CASE WHEN EXISTS (SELECT 1 FROM app.MembershipRole r WHERE r.MembershipId = m.Id AND r.Role = N''TenantAdmin'') THEN 1 ELSE 0 END A,
            CASE WHEN EXISTS (SELECT 1 FROM app.MembershipRole r WHERE r.MembershipId = m.Id AND r.Role IN (N''Teacher'', N''TenantAdmin'')) THEN 1 ELSE 0 END T
        FROM app.Membership m JOIN app.Tenant t ON t.Id = m.TenantId
        WHERE m.AccountId = k.AccountId AND m.Status = N''Approved'' ORDER BY t.IsLobby, m.Id) x;');

-- 3. 지울 표를 가리키는 외래키를 지운다
DECLARE @fk nvarchar(max) = N'';
SELECT @fk = @fk + N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(fk.parent_object_id)) + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
FROM sys.foreign_keys fk
WHERE (OBJECT_SCHEMA_NAME(fk.referenced_object_id) = N'app' AND OBJECT_NAME(fk.referenced_object_id) IN (N'Activity',N'QuizAttempt',N'PointLedger',N'MilestoneReached',N'FamilyInvite',N'FamilyJoinRequest',N'FamilyMilestoneDismiss',N'FamilyMilestoneReached',N'FamilyLink',N'FamilyRequest',N'Family',N'FamilyMember',N'FamilyTeam',N'GuardianLink',N'Participant',N'MembershipRole',N'PromotionRequest',N'Membership',N'VerseReadMark',N'Bookmark',N'ChapterProgress',N'RoundVerseRead',N'ChapterRead',N'ReadingRound',N'AuditLog',N'Account'))
   OR (OBJECT_SCHEMA_NAME(fk.parent_object_id) = N'app' AND OBJECT_NAME(fk.parent_object_id) IN (N'Activity',N'QuizAttempt',N'PointLedger',N'MilestoneReached',N'FamilyInvite',N'FamilyJoinRequest',N'FamilyMilestoneDismiss',N'FamilyMilestoneReached',N'FamilyLink',N'FamilyRequest',N'Family',N'FamilyMember',N'FamilyTeam',N'GuardianLink',N'Participant',N'MembershipRole',N'PromotionRequest',N'Membership',N'VerseReadMark',N'Bookmark',N'ChapterProgress',N'RoundVerseRead',N'ChapterRead',N'ReadingRound',N'AuditLog',N'Account'));
EXEC(@fk);

-- 4. 콘텐츠 · 시즌 · 규칙 표의 사람 칸을 아이디로 바꾼다(첫 번째 플랫폼 관리자, 본인이 만든 것이면 본인)
DECLARE @ix nvarchar(max);
EXEC(N'ALTER TABLE app.DailyWord ADD CreatedByUserName nvarchar(256) NULL;');
EXEC(N'UPDATE c SET c.CreatedByUserName = COALESCE((SELECT k.UserName FROM #keep k WHERE k.AccountId = c.CreatedByAccountId), (SELECT TOP 1 k.UserName FROM #keep k ORDER BY k.AccountId), N''system'') FROM app.DailyWord c;');
EXEC(N'ALTER TABLE app.DailyWord ALTER COLUMN CreatedByUserName nvarchar(256) NOT NULL;');
SET @ix = N''; SELECT @ix = @ix + N'DROP INDEX ' + QUOTENAME(i.name) + N' ON app.DailyWord;' FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE i.object_id = OBJECT_ID(N'app.DailyWord') AND c.name = N'CreatedByAccountId';
EXEC(@ix);
EXEC(N'ALTER TABLE app.DailyWord DROP COLUMN CreatedByAccountId;');
EXEC(N'ALTER TABLE app.DailyPrayer ADD CreatedByUserName nvarchar(256) NULL;');
EXEC(N'UPDATE c SET c.CreatedByUserName = COALESCE((SELECT k.UserName FROM #keep k WHERE k.AccountId = c.CreatedByAccountId), (SELECT TOP 1 k.UserName FROM #keep k ORDER BY k.AccountId), N''system'') FROM app.DailyPrayer c;');
EXEC(N'ALTER TABLE app.DailyPrayer ALTER COLUMN CreatedByUserName nvarchar(256) NOT NULL;');
SET @ix = N''; SELECT @ix = @ix + N'DROP INDEX ' + QUOTENAME(i.name) + N' ON app.DailyPrayer;' FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE i.object_id = OBJECT_ID(N'app.DailyPrayer') AND c.name = N'CreatedByAccountId';
EXEC(@ix);
EXEC(N'ALTER TABLE app.DailyPrayer DROP COLUMN CreatedByAccountId;');
EXEC(N'ALTER TABLE app.DailyQuiz ADD CreatedByUserName nvarchar(256) NULL;');
EXEC(N'UPDATE c SET c.CreatedByUserName = COALESCE((SELECT k.UserName FROM #keep k WHERE k.AccountId = c.CreatedByAccountId), (SELECT TOP 1 k.UserName FROM #keep k ORDER BY k.AccountId), N''system'') FROM app.DailyQuiz c;');
EXEC(N'ALTER TABLE app.DailyQuiz ALTER COLUMN CreatedByUserName nvarchar(256) NOT NULL;');
SET @ix = N''; SELECT @ix = @ix + N'DROP INDEX ' + QUOTENAME(i.name) + N' ON app.DailyQuiz;' FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE i.object_id = OBJECT_ID(N'app.DailyQuiz') AND c.name = N'CreatedByAccountId';
EXEC(@ix);
EXEC(N'ALTER TABLE app.DailyQuiz DROP COLUMN CreatedByAccountId;');
EXEC(N'ALTER TABLE app.WeeklyVerse ADD CreatedByUserName nvarchar(256) NULL;');
EXEC(N'UPDATE c SET c.CreatedByUserName = COALESCE((SELECT k.UserName FROM #keep k WHERE k.AccountId = c.CreatedByAccountId), (SELECT TOP 1 k.UserName FROM #keep k ORDER BY k.AccountId), N''system'') FROM app.WeeklyVerse c;');
EXEC(N'ALTER TABLE app.WeeklyVerse ALTER COLUMN CreatedByUserName nvarchar(256) NOT NULL;');
SET @ix = N''; SELECT @ix = @ix + N'DROP INDEX ' + QUOTENAME(i.name) + N' ON app.WeeklyVerse;' FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE i.object_id = OBJECT_ID(N'app.WeeklyVerse') AND c.name = N'CreatedByAccountId';
EXEC(@ix);
EXEC(N'ALTER TABLE app.WeeklyVerse DROP COLUMN CreatedByAccountId;');
EXEC(N'ALTER TABLE app.TenantSeason ADD ChangedByUserName nvarchar(256) NULL;');
EXEC(N'UPDATE c SET c.ChangedByUserName = COALESCE((SELECT k.UserName FROM #keep k WHERE k.AccountId = c.ChangedByAccountId), (SELECT TOP 1 k.UserName FROM #keep k ORDER BY k.AccountId), N''system'') FROM app.TenantSeason c;');
EXEC(N'ALTER TABLE app.TenantSeason ALTER COLUMN ChangedByUserName nvarchar(256) NOT NULL;');
SET @ix = N''; SELECT @ix = @ix + N'DROP INDEX ' + QUOTENAME(i.name) + N' ON app.TenantSeason;' FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE i.object_id = OBJECT_ID(N'app.TenantSeason') AND c.name = N'ChangedByAccountId';
EXEC(@ix);
EXEC(N'ALTER TABLE app.TenantSeason DROP COLUMN ChangedByAccountId;');
EXEC(N'ALTER TABLE app.PlatformRuleSetVersion ADD ChangedByUserName nvarchar(256) NULL;');
EXEC(N'UPDATE c SET c.ChangedByUserName = COALESCE((SELECT k.UserName FROM #keep k WHERE k.AccountId = c.ChangedByAccountId), (SELECT TOP 1 k.UserName FROM #keep k ORDER BY k.AccountId), N''system'') FROM app.PlatformRuleSetVersion c;');
EXEC(N'ALTER TABLE app.PlatformRuleSetVersion ALTER COLUMN ChangedByUserName nvarchar(256) NOT NULL;');
SET @ix = N''; SELECT @ix = @ix + N'DROP INDEX ' + QUOTENAME(i.name) + N' ON app.PlatformRuleSetVersion;' FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE i.object_id = OBJECT_ID(N'app.PlatformRuleSetVersion') AND c.name = N'ChangedByAccountId';
EXEC(@ix);
EXEC(N'ALTER TABLE app.PlatformRuleSetVersion DROP COLUMN ChangedByAccountId;');

-- 5. 옛 표를 지운다
EXEC(N'DROP TABLE IF EXISTS app.Activity');
EXEC(N'DROP TABLE IF EXISTS app.QuizAttempt');
EXEC(N'DROP TABLE IF EXISTS app.PointLedger');
EXEC(N'DROP TABLE IF EXISTS app.MilestoneReached');
EXEC(N'DROP TABLE IF EXISTS app.FamilyInvite');
EXEC(N'DROP TABLE IF EXISTS app.FamilyJoinRequest');
EXEC(N'DROP TABLE IF EXISTS app.FamilyMilestoneDismiss');
EXEC(N'DROP TABLE IF EXISTS app.FamilyMilestoneReached');
EXEC(N'DROP TABLE IF EXISTS app.FamilyLink');
EXEC(N'DROP TABLE IF EXISTS app.FamilyRequest');
EXEC(N'DROP TABLE IF EXISTS app.Family');
EXEC(N'DROP TABLE IF EXISTS app.FamilyMember');
EXEC(N'DROP TABLE IF EXISTS app.FamilyTeam');
EXEC(N'DROP TABLE IF EXISTS app.GuardianLink');
EXEC(N'DROP TABLE IF EXISTS app.Participant');
EXEC(N'DROP TABLE IF EXISTS app.MembershipRole');
EXEC(N'DROP TABLE IF EXISTS app.PromotionRequest');
EXEC(N'DROP TABLE IF EXISTS app.Membership');
EXEC(N'DROP TABLE IF EXISTS app.VerseReadMark');
EXEC(N'DROP TABLE IF EXISTS app.Bookmark');
EXEC(N'DROP TABLE IF EXISTS app.ChapterProgress');
EXEC(N'DROP TABLE IF EXISTS app.RoundVerseRead');
EXEC(N'DROP TABLE IF EXISTS app.ChapterRead');
EXEC(N'DROP TABLE IF EXISTS app.ReadingRound');
EXEC(N'DROP TABLE IF EXISTS app.AuditLog');
EXEC(N'DROP TABLE IF EXISTS app.Account');

-- 6. 새 표를 만든다
IF SCHEMA_ID(N'Identity') IS NULL EXEC(N'CREATE SCHEMA [Identity]');
EXEC(N'CREATE TABLE [app].[AuditLog] (
    [Id] bigint NOT NULL IDENTITY,
    [OccurredUtc] datetime2(3) NOT NULL,
    [ActorUserName] nvarchar(256) NOT NULL,
    [Action] varchar(40) NOT NULL,
    [TenantId] int NULL,
    [Detail] nvarchar(400) NULL,
    [ImpersonatorUserName] nvarchar(256) NULL,
    CONSTRAINT [PK_AuditLog] PRIMARY KEY ([Id])
);');
EXEC(N'CREATE TABLE [dbo].[DM2_ReadingBookmark] (
    [UserName] nvarchar(256) NOT NULL,
    [BookId] tinyint NOT NULL,
    [Chapter] smallint NOT NULL,
    [Verse] smallint NOT NULL,
    [CreatedAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_DM2_ReadingBookmark] PRIMARY KEY ([UserName], [BookId], [Chapter], [Verse])
);');
EXEC(N'CREATE TABLE [dbo].[DM2_ReadingRound] (
    [UserName] nvarchar(256) NOT NULL,
    [RoundNo] int NOT NULL,
    [StartedAt] datetime2(3) NOT NULL,
    [CompletedAt] datetime2(3) NULL,
    [ClosedAt] datetime2(3) NULL,
    CONSTRAINT [PK_DM2_ReadingRound] PRIMARY KEY ([UserName], [RoundNo]),
    CONSTRAINT [CK_DM2_ReadingRound_End] CHECK ([CompletedAt] IS NULL OR [ClosedAt] IS NULL),
    CONSTRAINT [CK_DM2_ReadingRound_No] CHECK ([RoundNo]>=(1))
);');
EXEC(N'CREATE TABLE [Identity].[Role] (
    [Id] nvarchar(450) NOT NULL,
    [Name] nvarchar(256) NULL,
    [NormalizedName] nvarchar(256) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    CONSTRAINT [PK_Role] PRIMARY KEY ([Id])
);');
EXEC(N'CREATE TABLE [Identity].[User] (
    [Id] nvarchar(450) NOT NULL,
    [UserName] nvarchar(256) NULL,
    [NormalizedUserName] nvarchar(256) NULL,
    [Email] nvarchar(256) NULL,
    [NormalizedEmail] nvarchar(256) NULL,
    [EmailConfirmed] bit NOT NULL,
    [PasswordHash] nvarchar(max) NULL,
    [SecurityStamp] nvarchar(max) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    [PhoneNumber] nvarchar(max) NULL,
    [PhoneNumberConfirmed] bit NOT NULL,
    [TwoFactorEnabled] bit NOT NULL,
    [LockoutEnd] datetimeoffset NULL,
    [LockoutEnabled] bit NOT NULL,
    [AccessFailedCount] int NOT NULL,
    [FirstName] nvarchar(max) NULL,
    [LastName] nvarchar(max) NULL,
    [ProfilePicture] varbinary(max) NULL,
    [UsernameChangeLimit] int NOT NULL DEFAULT 0,
    [insertDT] datetime NOT NULL,
    CONSTRAINT [PK_User] PRIMARY KEY ([Id])
);');
EXEC(N'CREATE TABLE [dbo].[DM2_ReadingChapterProgress] (
    [UserName] nvarchar(256) NOT NULL,
    [RoundNo] int NOT NULL,
    [BookId] tinyint NOT NULL,
    [Chapter] smallint NOT NULL,
    [LastViewedVerse] smallint NOT NULL,
    [UpdatedAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_DM2_ReadingChapterProgress] PRIMARY KEY ([UserName], [RoundNo], [BookId], [Chapter]),
    CONSTRAINT [CK_DM2_ReadingChapterProgress_Verse] CHECK ([LastViewedVerse]>=(1)),
    CONSTRAINT [FK_DM2_ReadingChapterProgress_Round] FOREIGN KEY ([UserName], [RoundNo]) REFERENCES [dbo].[DM2_ReadingRound] ([UserName], [RoundNo])
);');
EXEC(N'CREATE TABLE [dbo].[DM2_ReadingChapterRead] (
    [UserName] nvarchar(256) NOT NULL,
    [RoundNo] int NOT NULL,
    [BookId] tinyint NOT NULL,
    [Chapter] smallint NOT NULL,
    [ReadAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_DM2_ReadingChapterRead] PRIMARY KEY ([UserName], [RoundNo], [BookId], [Chapter]),
    CONSTRAINT [FK_DM2_ReadingChapterRead_Round] FOREIGN KEY ([UserName], [RoundNo]) REFERENCES [dbo].[DM2_ReadingRound] ([UserName], [RoundNo])
);');
EXEC(N'CREATE TABLE [dbo].[DM2_ReadingVerse] (
    [UserName] nvarchar(256) NOT NULL,
    [RoundNo] int NOT NULL,
    [BookId] tinyint NOT NULL,
    [Chapter] smallint NOT NULL,
    [Verse] smallint NOT NULL,
    [ReadAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_DM2_ReadingVerse] PRIMARY KEY ([UserName], [RoundNo], [BookId], [Chapter], [Verse]),
    CONSTRAINT [FK_DM2_ReadingVerse_Round] FOREIGN KEY ([UserName], [RoundNo]) REFERENCES [dbo].[DM2_ReadingRound] ([UserName], [RoundNo])
);');
EXEC(N'CREATE TABLE [Identity].[RoleClaims] (
    [Id] int NOT NULL IDENTITY,
    [RoleId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_RoleClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_RoleClaims_Role_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Identity].[Role] ([Id]) ON DELETE CASCADE
);');
EXEC(N'CREATE TABLE [dbo].[CodeData] (
    [TenantId] int NOT NULL,
    [Id] int NOT NULL IDENTITY,
    [MajorCD] nvarchar(50) NULL,
    [MinorCD] nvarchar(50) NULL,
    [MinorNM] nvarchar(250) NULL,
    [InsertDT] datetime NOT NULL,
    [updateDT] datetime NOT NULL,
    [displayOrder] int NULL,
    CONSTRAINT [PK_CodeData] PRIMARY KEY ([TenantId], [Id]),
    CONSTRAINT [FK_CodeData_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);');
EXEC(N'CREATE TABLE [dbo].[DeptRoleUserAssoc] (
    [TenantId] int NOT NULL,
    [Id] int NOT NULL IDENTITY,
    [RoleCode] nvarchar(50) NULL,
    [UserId] nvarchar(450) NULL,
    [InsertDT] datetime NOT NULL,
    [updateDT] datetime NOT NULL,
    CONSTRAINT [PK_DeptRoleUserAssoc] PRIMARY KEY ([TenantId], [Id]),
    CONSTRAINT [FK_DeptRoleUserAssoc_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);');
EXEC(N'CREATE TABLE [dbo].[DM2_FamilyMilestoneConfig] (
    [TenantId] int NOT NULL,
    [Id] int NOT NULL IDENTITY,
    [Year] int NOT NULL,
    [PointThreshold] int NOT NULL,
    [MilestoneType] nvarchar(20) NOT NULL,
    [DisplayTitle] nvarchar(200) NOT NULL,
    [DisplayEmoji] nvarchar(10) NULL,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [SortOrder] int NOT NULL DEFAULT 0,
    [CreatedAt] datetime2(7) NOT NULL,
    CONSTRAINT [PK_DM2_FamilyMilestoneConfig] PRIMARY KEY ([TenantId], [Id]),
    CONSTRAINT [CK_DM2FMC_Type] CHECK ([MilestoneType]=''APPLAUSE'' OR [MilestoneType]=''AWARD''),
    CONSTRAINT [FK_DM2_FamilyMilestoneConfig_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);');
EXEC(N'CREATE TABLE [dbo].[DM2_FamilyNotice] (
    [TenantId] int NOT NULL,
    [Id] int NOT NULL IDENTITY,
    [UserName] nvarchar(200) NOT NULL,
    [FamilyTeamId] int NOT NULL,
    [TeamName] nvarchar(100) NOT NULL,
    [Kind] varchar(10) NOT NULL DEFAULT ''JOINED'',
    [CreatedAt] datetime NOT NULL,
    CONSTRAINT [PK_DM2_FamilyNotice] PRIMARY KEY ([TenantId], [Id]),
    CONSTRAINT [FK_DM2_FamilyNotice_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);');
EXEC(N'CREATE TABLE [dbo].[DM2_FamilyTeam] (
    [TenantId] int NOT NULL,
    [FamilyTeamId] int NOT NULL IDENTITY,
    [TeamName] nvarchar(50) NOT NULL,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [Memo] nvarchar(200) NULL,
    [InsertDT] datetime NOT NULL,
    [UpdateDT] datetime NOT NULL,
    [AwardKey] nvarchar(100) NULL,
    [CreatorUserName] nvarchar(200) NULL,
    [TeamType] varchar(10) NOT NULL DEFAULT ''FAMILY'',
    CONSTRAINT [PK_DM2_FamilyTeam] PRIMARY KEY ([TenantId], [FamilyTeamId]),
    CONSTRAINT [CK_DM2_FamilyTeam_TeamType] CHECK ([TeamType]=''FRIEND'' OR [TeamType]=''FAMILY''),
    CONSTRAINT [FK_DM2_FamilyTeam_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);');
EXEC(N'CREATE TABLE [dbo].[DM2_MissionActivity] (
    [TenantId] int NOT NULL,
    [ActivityId] bigint NOT NULL IDENTITY,
    [UserName] nvarchar(256) NOT NULL,
    [MissionDate] date NOT NULL,
    [MissionItem] varchar(20) NOT NULL,
    [Points] int NOT NULL,
    [ReciteWeekStart] date NULL,
    [PerformedAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_DM2_MissionActivity] PRIMARY KEY ([TenantId], [ActivityId]),
    CONSTRAINT [CK_DM2_MissionActivity_Item] CHECK ([MissionItem]=''Recite'' OR [MissionItem]=''Quiz.Correct'' OR [MissionItem]=''Prayer.Others'' OR [MissionItem]=''Prayer.Me'' OR [MissionItem]=''Word.Parent'' OR [MissionItem]=''Word.Me''),
    CONSTRAINT [CK_DM2_MissionActivity_Points] CHECK ([Points]>=(0)),
    CONSTRAINT [CK_DM2_MissionActivity_Week] CHECK ([MissionItem]=''Recite'' AND [ReciteWeekStart] IS NOT NULL OR [MissionItem]<>''Recite'' AND [ReciteWeekStart] IS NULL),
    CONSTRAINT [FK_DM2_MissionActivity_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);');
EXEC(N'CREATE TABLE [dbo].[DM2_QuizAttempt] (
    [TenantId] int NOT NULL,
    [UserName] nvarchar(256) NOT NULL,
    [MissionDate] date NOT NULL,
    [QuestionId] int NOT NULL,
    [AttemptNo] tinyint NOT NULL,
    [AnswerText] nvarchar(400) NULL,
    [CorrectAnswerText] nvarchar(400) NULL,
    [IsCorrect] bit NOT NULL,
    [AttemptedAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_DM2_QuizAttempt] PRIMARY KEY ([TenantId], [UserName], [MissionDate], [QuestionId], [AttemptNo]),
    CONSTRAINT [CK_DM2_QuizAttempt_No] CHECK ([AttemptNo]>=(1)),
    CONSTRAINT [FK_DM2_QuizAttempt_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);');
EXEC(N'CREATE TABLE [dbo].[DM2_TeamAward] (
    [TenantId] int NOT NULL,
    [AwardId] int NOT NULL IDENTITY,
    [Kind] nvarchar(10) NOT NULL,
    [AwardKey] nvarchar(200) NOT NULL,
    [Threshold] int NOT NULL,
    [AwardedAt] datetime NOT NULL,
    CONSTRAINT [PK_DM2_TeamAward] PRIMARY KEY ([TenantId], [AwardId]),
    CONSTRAINT [CK_DM2_TeamAward_Kind] CHECK ([Kind]=N''FRIEND'' OR [Kind]=N''FAMILY''),
    CONSTRAINT [FK_DM2_TeamAward_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);');
EXEC(N'CREATE TABLE [app].[GuardianLink] (
    [Id] int NOT NULL IDENTITY,
    [TenantId] int NOT NULL,
    [GuardianUserName] nvarchar(256) NOT NULL,
    [StudentUserName] nvarchar(256) NOT NULL,
    [CreatedUtc] datetime2(3) NOT NULL,
    [CreatedByUserName] nvarchar(256) NOT NULL,
    CONSTRAINT [PK_GuardianLink] PRIMARY KEY NONCLUSTERED ([Id]),
    CONSTRAINT [UQ_GuardianLink_TenantId_Id] UNIQUE CLUSTERED ([TenantId], [Id]),
    CONSTRAINT [FK_GuardianLink_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);');
EXEC(N'CREATE TABLE [app].[PromotionRequest] (
    [Id] int NOT NULL IDENTITY,
    [UserName] nvarchar(256) NOT NULL,
    [SourceTenantId] int NOT NULL,
    [TargetTenantId] int NOT NULL,
    [Status] varchar(20) NOT NULL,
    [RequestedUtc] datetime2(3) NOT NULL,
    [RequestedByUserName] nvarchar(256) NOT NULL,
    [DecidedUtc] datetime2(3) NULL,
    [DecidedByUserName] nvarchar(256) NULL,
    CONSTRAINT [PK_PromotionRequest] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_PromotionRequest_Status] CHECK (Status IN (''Pending'',''Approved'',''Rejected'',''Cancelled'')),
    CONSTRAINT [CK_PromotionRequest_Tenants] CHECK (SourceTenantId <> TargetTenantId),
    CONSTRAINT [FK_PromotionRequest_Source] FOREIGN KEY ([SourceTenantId]) REFERENCES [app].[Tenant] ([Id]),
    CONSTRAINT [FK_PromotionRequest_Target] FOREIGN KEY ([TargetTenantId]) REFERENCES [app].[Tenant] ([Id])
);');
EXEC(N'CREATE TABLE [dbo].[SignupApplication] (
    [TenantId] int NOT NULL,
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [UserName] nvarchar(256) NOT NULL,
    [FullName] nvarchar(100) NOT NULL,
    [RoleKind] char(1) NOT NULL,
    [PersonId] int NOT NULL,
    [ClassYear] int NOT NULL,
    [ClassBan] int NOT NULL,
    [BMonth] int NOT NULL,
    [BDay] int NOT NULL,
    [Sex] nvarchar(1) NULL,
    [School] nvarchar(200) NULL,
    [Phone] nvarchar(50) NULL,
    [Kids] nvarchar(1000) NULL,
    [ReqType] nvarchar(10) NOT NULL,
    [PrevRole] nvarchar(100) NULL,
    [Status] nvarchar(10) NOT NULL,
    [RejectReason] nvarchar(300) NULL,
    [InsertDT] datetime NOT NULL,
    [DecidedDT] datetime NULL,
    [DecidedBy] nvarchar(256) NULL,
    CONSTRAINT [PK_SignupApplication] PRIMARY KEY ([TenantId], [Id]),
    CONSTRAINT [FK_SignupApplication_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);');
EXEC(N'CREATE TABLE [app].[UserSetting] (
    [UserName] nvarchar(256) NOT NULL,
    [MustChangePassword] bit NOT NULL,
    [PhotoPath] nvarchar(260) NULL,
    [LastTenantId] int NULL,
    [ReadingTheme] varchar(20) NULL,
    [MissionTheme] varchar(20) NULL,
    [FamilyInvitesSnoozedAt] datetime2 NULL,
    CONSTRAINT [PK_UserSetting] PRIMARY KEY ([UserName]),
    CONSTRAINT [FK_UserSetting_LastTenant] FOREIGN KEY ([LastTenantId]) REFERENCES [app].[Tenant] ([Id])
);');
EXEC(N'CREATE TABLE [Identity].[UserClaims] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_UserClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_UserClaims_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [Identity].[User] ([Id]) ON DELETE CASCADE
);');
EXEC(N'CREATE TABLE [Identity].[UserLogins] (
    [LoginProvider] nvarchar(450) NOT NULL,
    [ProviderKey] nvarchar(450) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_UserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_UserLogins_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [Identity].[User] ([Id]) ON DELETE CASCADE
);');
EXEC(N'CREATE TABLE [Identity].[UserRoles] (
    [UserId] nvarchar(450) NOT NULL,
    [RoleId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_UserRoles] PRIMARY KEY ([UserId], [RoleId]),
    CONSTRAINT [FK_UserRoles_Role_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Identity].[Role] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_UserRoles_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [Identity].[User] ([Id]) ON DELETE CASCADE
);');
EXEC(N'CREATE TABLE [Identity].[UserTokens] (
    [UserId] nvarchar(450) NOT NULL,
    [LoginProvider] nvarchar(450) NOT NULL,
    [Name] nvarchar(450) NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_UserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_UserTokens_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [Identity].[User] ([Id]) ON DELETE CASCADE
);');
EXEC(N'CREATE TABLE [dbo].[DM2_FamilyMilestoneHidden] (
    [TenantId] int NOT NULL,
    [Id] int NOT NULL IDENTITY,
    [MilestoneConfigId] int NOT NULL,
    [FamilyTeamId] int NOT NULL,
    [HiddenAt] datetime2(7) NOT NULL,
    CONSTRAINT [PK_DM2_FamilyMilestoneHidden] PRIMARY KEY ([TenantId], [Id]),
    CONSTRAINT [FK_DM2FMH_Config] FOREIGN KEY ([TenantId], [MilestoneConfigId]) REFERENCES [dbo].[DM2_FamilyMilestoneConfig] ([TenantId], [Id])
);');
EXEC(N'CREATE TABLE [dbo].[DM2_FamilyMember] (
    [TenantId] int NOT NULL,
    [Id] int NOT NULL IDENTITY,
    [FamilyTeamId] int NOT NULL,
    [UserName] nvarchar(100) NOT NULL,
    [RoleCode] tinyint NOT NULL,
    [SortNo] int NOT NULL DEFAULT 0,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [InsertDT] datetime NOT NULL,
    [UpdateDT] datetime NOT NULL,
    [RelationCode] tinyint NOT NULL DEFAULT CAST(9 AS tinyint),
    [RelationLabel] nvarchar(20) NULL,
    CONSTRAINT [PK_DM2_FamilyMember] PRIMARY KEY ([TenantId], [Id]),
    CONSTRAINT [FK_DM2_FamilyMember_Team] FOREIGN KEY ([TenantId], [FamilyTeamId]) REFERENCES [dbo].[DM2_FamilyTeam] ([TenantId], [FamilyTeamId])
);');
EXEC(N'CREATE TABLE [dbo].[DM2_FamilyRequest] (
    [TenantId] int NOT NULL,
    [Id] int NOT NULL IDENTITY,
    [FamilyTeamId] int NOT NULL,
    [Kind] char(1) NOT NULL,
    [TargetUserName] nvarchar(200) NOT NULL,
    [FromUserName] nvarchar(200) NOT NULL,
    [RelationKey] varchar(10) NOT NULL,
    [CreatedAt] datetime NOT NULL,
    CONSTRAINT [PK_DM2_FamilyRequest] PRIMARY KEY ([TenantId], [Id]),
    CONSTRAINT [CK_DM2_FamilyRequest_Kind] CHECK ([Kind]=''I'' OR [Kind]=''R''),
    CONSTRAINT [FK_DM2_FamilyRequest_Team] FOREIGN KEY ([TenantId], [FamilyTeamId]) REFERENCES [dbo].[DM2_FamilyTeam] ([TenantId], [FamilyTeamId])
);');
EXEC(N'CREATE INDEX [IX_AuditLog_Occurred] ON [app].[AuditLog] ([OccurredUtc]);');
EXEC(N'CREATE INDEX [IX_DeptRoleUserAssoc_UserId] ON [dbo].[DeptRoleUserAssoc] ([UserId]);');
EXEC(N'CREATE INDEX [IX_DM2_FamilyMember_FamilyTeamId_IsActive] ON [dbo].[DM2_FamilyMember] ([TenantId], [FamilyTeamId], [IsActive]);');
EXEC(N'CREATE INDEX [IX_DM2_FamilyMember_Team] ON [dbo].[DM2_FamilyMember] ([TenantId], [FamilyTeamId], [RoleCode], [IsActive]);');
EXEC(N'CREATE INDEX [IX_DM2_FamilyMember_UserName_IsActive] ON [dbo].[DM2_FamilyMember] ([TenantId], [UserName], [IsActive]);');
EXEC(N'CREATE UNIQUE INDEX [UX_DM2_FamilyMember_Team_Self] ON [dbo].[DM2_FamilyMember] ([TenantId], [FamilyTeamId], [RelationCode]) WHERE [IsActive]=(1) AND [RoleCode]=(0) AND [RelationCode]=(0);');
EXEC(N'CREATE UNIQUE INDEX [UX_DM2_FamilyMember_Team_User] ON [dbo].[DM2_FamilyMember] ([TenantId], [FamilyTeamId], [UserName]);');
EXEC(N'CREATE UNIQUE INDEX [UX_DM2_FamilyMember_UserName] ON [dbo].[DM2_FamilyMember] ([UserName]);');
EXEC(N'CREATE INDEX [IX_DM2_FamilyMilestoneConfig_Year_IsActive] ON [dbo].[DM2_FamilyMilestoneConfig] ([TenantId], [Year], [IsActive]);');
EXEC(N'CREATE UNIQUE INDEX [UQ_DM2FMC_YearPoint] ON [dbo].[DM2_FamilyMilestoneConfig] ([TenantId], [Year], [PointThreshold]);');
EXEC(N'CREATE INDEX [IX_DM2FMH_ConfigId] ON [dbo].[DM2_FamilyMilestoneHidden] ([TenantId], [MilestoneConfigId]);');
EXEC(N'CREATE UNIQUE INDEX [UQ_DM2FMH_ConfigTeam] ON [dbo].[DM2_FamilyMilestoneHidden] ([TenantId], [MilestoneConfigId], [FamilyTeamId]);');
EXEC(N'CREATE INDEX [IX_DM2_FamilyNotice_User] ON [dbo].[DM2_FamilyNotice] ([TenantId], [UserName]);');
EXEC(N'CREATE INDEX [IX_DM2_FamilyRequest_From] ON [dbo].[DM2_FamilyRequest] ([TenantId], [FromUserName], [Kind]);');
EXEC(N'CREATE INDEX [IX_DM2_FamilyRequest_Target] ON [dbo].[DM2_FamilyRequest] ([TenantId], [TargetUserName], [Kind]);');
EXEC(N'CREATE UNIQUE INDEX [UX_DM2_FamilyRequest_Team_Kind_Target] ON [dbo].[DM2_FamilyRequest] ([TenantId], [FamilyTeamId], [Kind], [TargetUserName]);');
EXEC(N'CREATE INDEX [IX_DM2_FamilyTeam_IsActive] ON [dbo].[DM2_FamilyTeam] ([TenantId], [IsActive]);');
EXEC(N'CREATE UNIQUE INDEX [UX_DM2_FamilyTeam_AwardKey] ON [dbo].[DM2_FamilyTeam] ([TenantId], [AwardKey]) WHERE [AwardKey] IS NOT NULL;');
EXEC(N'CREATE INDEX [IX_DM2_MissionActivity_Date] ON [dbo].[DM2_MissionActivity] ([TenantId], [MissionDate]);');
EXEC(N'CREATE UNIQUE INDEX [UX_DM2_MissionActivity_Daily] ON [dbo].[DM2_MissionActivity] ([TenantId], [UserName], [MissionDate], [MissionItem]) WHERE [MissionItem]<>''Recite'';');
EXEC(N'CREATE UNIQUE INDEX [UX_DM2_MissionActivity_Recite] ON [dbo].[DM2_MissionActivity] ([TenantId], [UserName], [ReciteWeekStart]) WHERE [MissionItem]=''Recite'';');
EXEC(N'CREATE INDEX [IX_DM2_ReadingChapterProgress_Recent] ON [dbo].[DM2_ReadingChapterProgress] ([UserName], [UpdatedAt] DESC);');
EXEC(N'CREATE INDEX [IX_DM2_ReadingChapterRead_ReadAt] ON [dbo].[DM2_ReadingChapterRead] ([UserName], [ReadAt]);');
EXEC(N'CREATE UNIQUE INDEX [UX_DM2_ReadingRound_Open] ON [dbo].[DM2_ReadingRound] ([UserName]) WHERE [CompletedAt] IS NULL AND [ClosedAt] IS NULL;');
EXEC(N'CREATE INDEX [IX_DM2_ReadingVerse_ReadAt] ON [dbo].[DM2_ReadingVerse] ([ReadAt]);');
EXEC(N'CREATE INDEX [IX_DM2_TeamAward_Key] ON [dbo].[DM2_TeamAward] ([TenantId], [AwardKey]);');
EXEC(N'CREATE UNIQUE INDEX [UQ_DM2_TeamAward] ON [dbo].[DM2_TeamAward] ([TenantId], [Kind], [AwardKey], [Threshold]);');
EXEC(N'CREATE INDEX [IX_GuardianLink_Student] ON [app].[GuardianLink] ([TenantId], [StudentUserName]);');
EXEC(N'CREATE UNIQUE INDEX [UQ_GuardianLink_Pair] ON [app].[GuardianLink] ([TenantId], [GuardianUserName], [StudentUserName]);');
EXEC(N'CREATE INDEX [IX_PromotionRequest_Source] ON [app].[PromotionRequest] ([SourceTenantId], [Status]);');
EXEC(N'CREATE INDEX [IX_PromotionRequest_Target] ON [app].[PromotionRequest] ([TargetTenantId], [Status]);');
EXEC(N'CREATE UNIQUE INDEX [UQ_PromotionRequest_Pending] ON [app].[PromotionRequest] ([UserName], [SourceTenantId]) WHERE [Status] = ''Pending'';');
EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [Identity].[Role] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;');
EXEC(N'CREATE INDEX [IX_RoleClaims_RoleId] ON [Identity].[RoleClaims] ([RoleId]);');
EXEC(N'CREATE INDEX [EmailIndex] ON [Identity].[User] ([NormalizedEmail]);');
EXEC(N'CREATE INDEX [IX_IdentityUser_UserName] ON [Identity].[User] ([UserName]);');
EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [Identity].[User] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;');
EXEC(N'CREATE INDEX [IX_UserClaims_UserId] ON [Identity].[UserClaims] ([UserId]);');
EXEC(N'CREATE INDEX [IX_UserLogins_UserId] ON [Identity].[UserLogins] ([UserId]);');
EXEC(N'CREATE INDEX [IX_UserRoles_RoleId] ON [Identity].[UserRoles] ([RoleId]);');
EXEC(N'CREATE INDEX [IX_UserSetting_LastTenantId] ON [app].[UserSetting] ([LastTenantId]);');

-- 7. 처음 값: 역할 4개, 플랫폼 관리자, 부서마다 역할 코드 18개
EXEC(N'INSERT [Identity].[Role] (Id, Name, NormalizedName, ConcurrencyStamp)
    SELECT LOWER(CONVERT(nvarchar(36), NEWID())), v.N, UPPER(v.N), LOWER(CONVERT(nvarchar(36), NEWID())) FROM (VALUES (N''SuperAdmin''), (N''Admin''), (N''Moderator''), (N''Basic'')) v(N);');
EXEC(N'INSERT [Identity].[User] (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, FirstName, LastName, UsernameChangeLimit, insertDT)
    SELECT NewId, UserName, UPPER(UserName), Email, UPPER(Email), 1, PasswordHash, LOWER(CONVERT(nvarchar(36), NEWID())), LOWER(CONVERT(nvarchar(36), NEWID())), 0, 0, 1, 0, DisplayName, N'''', 0, CONVERT(datetime, CreatedUtc) FROM #keep;');
EXEC(N'INSERT [Identity].[UserRoles] (UserId, RoleId) SELECT k.NewId, r.Id FROM #keep k JOIN [Identity].[Role] r ON r.Name = N''SuperAdmin'';
    INSERT [Identity].[UserRoles] (UserId, RoleId) SELECT k.NewId, r.Id FROM #keep k JOIN [Identity].[Role] r ON r.Name = N''Admin'' WHERE k.DeptAdmin = 1;');
EXEC(N'INSERT app.UserSetting (UserName, MustChangePassword, PhotoPath, LastTenantId, ReadingTheme, MissionTheme) SELECT UserName, 0, PhotoPath, CASE WHEN LastTenantId = TenantId THEN LastTenantId END, ReadingTheme, MissionTheme FROM #keep;');
EXEC(N'INSERT dbo.DeptRoleUserAssoc (TenantId, RoleCode, UserId, InsertDT, updateDT) SELECT TenantId, CASE WHEN IsTeacher = 1 THEN N''T1002'' ELSE N''T1001'' END, NewId, ''2026-11-10T00:00:00'', ''2026-11-10T00:00:00'' FROM #keep WHERE TenantId IS NOT NULL;');
EXEC(N'INSERT dbo.CodeData (TenantId, MajorCD, MinorCD, MinorNM, InsertDT, updateDT, displayOrder)
    SELECT t.Id, N''T001'', v.C, v.N, ''2026-11-10T00:00:00'', ''2026-11-10T00:00:00'', v.O FROM app.Tenant t CROSS JOIN (VALUES (N''T1001'', N''학생'', 1), (N''T1002'', N''교사'', 2), (N''T1003'', N''교사(S)'', 3), (N''T1004'', N''지도'', 4), (N''T1005'', N''부장'', 5), (N''T1006'', N''부감'', 6), (N''T1008'', N''서기'', 7), (N''T1009'', N''회계'', 8), (N''T1010'', N''보조'', 9), (N''T1013'', N''지원'', 10), (N''T1015'', N''율동'', 11), (N''T1016'', N''촬영'', 12), (N''T1017'', N''영상'', 13), (N''T1018'', N''반주'', 14), (N''T1019'', N''스텝'', 15), (N''T1020'', N''새친구'', 16), (N''T1040'', N''교장'', 17), (N''T1099'', N''기타'', 18)) v(C, N, O);');
DROP TABLE #keep;

-- 8. RLS 를 새 표로 다시 건다
EXEC(N'CREATE FUNCTION sec.fn_Tenant (@TenantId int)
RETURNS TABLE WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS Allowed
    WHERE @TenantId = CAST(SESSION_CONTEXT(N''TenantId'') AS int)
       OR CAST(SESSION_CONTEXT(N''PlatformScope'') AS bit) = 1;');
EXEC(N'CREATE FUNCTION sec.fn_Membership (@TenantId int, @UserId nvarchar(450))
RETURNS TABLE WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS Allowed
    WHERE @TenantId = CAST(SESSION_CONTEXT(N''TenantId'') AS int)
       OR @UserId   = CAST(SESSION_CONTEXT(N''UserId'') AS nvarchar(450))
       OR CAST(SESSION_CONTEXT(N''PlatformScope'') AS bit) = 1;');
EXEC(N'CREATE FUNCTION sec.fn_SharedRead (@TenantId int)
RETURNS TABLE WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS Allowed
    WHERE @TenantId IS NULL
       OR @TenantId = CAST(SESSION_CONTEXT(N''TenantId'') AS int)
       OR CAST(SESSION_CONTEXT(N''PlatformScope'') AS bit) = 1;');
EXEC(N'CREATE FUNCTION sec.fn_SharedWrite (@TenantId int)
RETURNS TABLE WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS Allowed
    WHERE (@TenantId IS NOT NULL AND @TenantId = CAST(SESSION_CONTEXT(N''TenantId'') AS int))
       OR CAST(SESSION_CONTEXT(N''PlatformScope'') AS bit) = 1;            -- 공용(NULL)은 플랫폼만');
EXEC(N'CREATE FUNCTION sec.fn_Person (@UserName nvarchar(256))
RETURNS TABLE WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS Allowed
    WHERE @UserName = CAST(SESSION_CONTEXT(N''UserName'') AS nvarchar(256))
       OR CAST(SESSION_CONTEXT(N''PlatformScope'') AS bit) = 1;');
EXEC(N'CREATE FUNCTION sec.fn_ReadingRead (@UserName nvarchar(256))
RETURNS TABLE WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS Allowed
    WHERE @UserName = CAST(SESSION_CONTEXT(N''UserName'') AS nvarchar(256))
       OR CAST(SESSION_CONTEXT(N''PlatformScope'') AS bit) = 1
       OR (
            EXISTS (SELECT 1 FROM dbo.DeptRoleUserAssoc d
                    JOIN [Identity].[User] u ON u.Id = d.UserId
                    WHERE d.TenantId = CAST(SESSION_CONTEXT(N''TenantId'') AS int)
                      AND u.UserName = @UserName)
        AND EXISTS (SELECT 1 FROM dbo.DeptRoleUserAssoc s
                    WHERE s.TenantId = CAST(SESSION_CONTEXT(N''TenantId'') AS int)
                      AND s.UserId = CAST(SESSION_CONTEXT(N''UserId'') AS nvarchar(450))
                      AND (
                           (s.RoleCode IS NOT NULL AND s.RoleCode NOT IN (N''T1001'', N''T1099''))
                        OR EXISTS (SELECT 1 FROM [Identity].[UserRoles] ur
                                   JOIN [Identity].[Role] r ON r.Id = ur.RoleId
                                   WHERE ur.UserId = s.UserId AND r.NormalizedName = N''ADMIN'')
                        OR EXISTS (SELECT 1 FROM app.Tenant t
                                   WHERE t.Id = s.TenantId AND t.ReadingBoardPublic = 1)
                      ))
       );');
EXEC(N'CREATE FUNCTION sec.fn_Promotion (@SourceTenantId int, @TargetTenantId int)
RETURNS TABLE WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS Allowed
    WHERE @SourceTenantId = CAST(SESSION_CONTEXT(N''TenantId'') AS int)
       OR @TargetTenantId = CAST(SESSION_CONTEXT(N''TenantId'') AS int)
       OR CAST(SESSION_CONTEXT(N''PlatformScope'') AS bit) = 1;');
EXEC(N'CREATE SECURITY POLICY sec.TenantIsolation
    ADD FILTER PREDICATE sec.fn_Membership(TenantId, UserId) ON dbo.DeptRoleUserAssoc,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.DeptRoleUserAssoc AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.DeptRoleUserAssoc AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Membership(TenantId, UserId) ON dbo.SignupApplication,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.SignupApplication AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.SignupApplication AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON dbo.CodeData,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.CodeData AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.CodeData AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.GuardianLink,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GuardianLink AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GuardianLink AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.TenantSeason,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.TenantSeason AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.TenantSeason AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.SearchLog,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.SearchLog AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.SearchLog AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyTeam,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyTeam AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyTeam AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyMember,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyMember AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyMember AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyRequest,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyRequest AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyRequest AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyNotice,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyNotice AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyNotice AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyMilestoneConfig,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyMilestoneConfig AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyMilestoneConfig AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyMilestoneHidden,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyMilestoneHidden AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyMilestoneHidden AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_TeamAward,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_TeamAward AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_TeamAward AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_MissionActivity,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_MissionActivity AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_MissionActivity AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_QuizAttempt,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_QuizAttempt AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_QuizAttempt AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Promotion(SourceTenantId, TargetTenantId) ON app.PromotionRequest,
    ADD BLOCK PREDICATE sec.fn_Promotion(SourceTenantId, TargetTenantId) ON app.PromotionRequest AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_Promotion(SourceTenantId, TargetTenantId) ON app.PromotionRequest AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_SharedRead(TenantId) ON app.DailyWord,
    ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyWord AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyWord AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_SharedRead(TenantId) ON app.DailyPrayer,
    ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyPrayer AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyPrayer AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_SharedRead(TenantId) ON app.DailyQuiz,
    ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyQuiz AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyQuiz AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_SharedRead(TenantId) ON app.DailyQuizChoice,
    ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyQuizChoice AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyQuizChoice AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_SharedRead(TenantId) ON app.DailyQuizAcceptedAnswer,
    ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyQuizAcceptedAnswer AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyQuizAcceptedAnswer AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_SharedRead(TenantId) ON app.WeeklyVerse,
    ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.WeeklyVerse AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.WeeklyVerse AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingBookmark,
    ADD BLOCK PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingBookmark AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingBookmark AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_ReadingRead(UserName) ON dbo.DM2_ReadingRound,
    ADD BLOCK PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingRound AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingRound AFTER UPDATE,
    ADD BLOCK PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingRound BEFORE UPDATE,
    ADD BLOCK PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingRound BEFORE DELETE,
    ADD FILTER PREDICATE sec.fn_ReadingRead(UserName) ON dbo.DM2_ReadingVerse,
    ADD BLOCK PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingVerse AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingVerse AFTER UPDATE,
    ADD BLOCK PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingVerse BEFORE UPDATE,
    ADD BLOCK PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingVerse BEFORE DELETE,
    ADD FILTER PREDICATE sec.fn_ReadingRead(UserName) ON dbo.DM2_ReadingChapterRead,
    ADD BLOCK PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingChapterRead AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingChapterRead AFTER UPDATE,
    ADD BLOCK PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingChapterRead BEFORE UPDATE,
    ADD BLOCK PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingChapterRead BEFORE DELETE,
    ADD FILTER PREDICATE sec.fn_ReadingRead(UserName) ON dbo.DM2_ReadingChapterProgress,
    ADD BLOCK PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingChapterProgress AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingChapterProgress AFTER UPDATE,
    ADD BLOCK PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingChapterProgress BEFORE UPDATE,
    ADD BLOCK PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingChapterProgress BEFORE DELETE
WITH (STATE = ON, SCHEMABINDING = ON);');

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20261110032322_SharedTables', N'9.0.10');
END;
COMMIT;
