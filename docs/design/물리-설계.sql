-- =====================================================================
-- 도전 성경말씀 — 참조 DDL (SQL Server 2019 이상). 공유 표는 유년부 부록 08 + TenantId (ADR 0090).
-- EF 모델에서 뽑은 표 정의 + RLS. 실DB 옮기기는 tools/sql/migrate-shared-tables.sql.
-- 08 의 현재 시각 기본값은 두지 않는다 — 앱이 값을 넣는다(프로젝트 원칙).
-- =====================================================================
CREATE SCHEMA app;
GO
CREATE SCHEMA sec;
GO
CREATE SCHEMA [Identity];
GO

CREATE TABLE [app].[AuditLog] (
    [Id] bigint NOT NULL IDENTITY,
    [OccurredUtc] datetime2(3) NOT NULL,
    [ActorUserName] nvarchar(256) NOT NULL,
    [Action] varchar(40) NOT NULL,
    [TenantId] int NULL,
    [Detail] nvarchar(400) NULL,
    [ImpersonatorUserName] nvarchar(256) NULL,
    CONSTRAINT [PK_AuditLog] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [app].[BibleBook] (
    [Id] tinyint NOT NULL,
    [Name] nvarchar(30) NOT NULL,
    [ShortName] nvarchar(10) NOT NULL,
    [Testament] char(2) NOT NULL,
    [ChapterCount] smallint NOT NULL,
    [Intro] nvarchar(max) NULL,
    CONSTRAINT [PK_BibleBook] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_BibleBook_ChapterCount] CHECK (ChapterCount >= 1),
    CONSTRAINT [CK_BibleBook_Id] CHECK (Id BETWEEN 1 AND 66),
    CONSTRAINT [CK_BibleBook_Testament] CHECK (Testament IN ('OT','NT'))
);
GO

CREATE TABLE [app].[BibleTranslation] (
    [Id] tinyint NOT NULL,
    [Code] varchar(20) NOT NULL,
    [Name] nvarchar(50) NOT NULL,
    [SortOrder] tinyint NOT NULL,
    CONSTRAINT [PK_BibleTranslation] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [app].[DailyWordClip] (
    [Id] int NOT NULL IDENTITY,
    [WordDate] date NOT NULL,
    [Seq] tinyint NOT NULL,
    [Chapter] smallint NOT NULL,
    [YoutubeVideoId] varchar(20) NULL,
    [StartSec] int NULL,
    [EndSec] int NULL,
    [UpdatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_DailyWordClip] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [app].[DataProtectionKey] (
    [Id] int NOT NULL IDENTITY,
    [FriendlyName] nvarchar(100) NULL,
    [Xml] nvarchar(max) NULL,
    CONSTRAINT [PK_DataProtectionKey] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [dbo].[DM2_ReadingBookmark] (
    [UserName] nvarchar(256) NOT NULL,
    [BookId] tinyint NOT NULL,
    [Chapter] smallint NOT NULL,
    [Verse] smallint NOT NULL,
    [CreatedAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_DM2_ReadingBookmark] PRIMARY KEY ([UserName], [BookId], [Chapter], [Verse])
);
GO

CREATE TABLE [dbo].[DM2_ReadingRound] (
    [UserName] nvarchar(256) NOT NULL,
    [RoundNo] int NOT NULL,
    [StartedAt] datetime2(3) NOT NULL,
    [CompletedAt] datetime2(3) NULL,
    [ClosedAt] datetime2(3) NULL,
    CONSTRAINT [PK_DM2_ReadingRound] PRIMARY KEY ([UserName], [RoundNo]),
    CONSTRAINT [CK_DM2_ReadingRound_End] CHECK ([CompletedAt] IS NULL OR [ClosedAt] IS NULL),
    CONSTRAINT [CK_DM2_ReadingRound_No] CHECK ([RoundNo]>=(1))
);
GO

CREATE TABLE [app].[PlatformRuleSetVersion] (
    [Id] int NOT NULL IDENTITY,
    [VersionNo] int NOT NULL,
    [Values] nvarchar(max) NOT NULL,
    [EffectiveFrom] date NOT NULL,
    [ChangedUtc] datetime2(3) NOT NULL,
    [ChangedByUserName] nvarchar(256) NOT NULL,
    CONSTRAINT [PK_PlatformRuleSetVersion] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_PlatformRuleSetVersion_Values] CHECK (ISJSON([Values]) = 1)
);
GO

CREATE TABLE [Identity].[Role] (
    [Id] nvarchar(450) NOT NULL,
    [Name] nvarchar(256) NULL,
    [NormalizedName] nvarchar(256) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    CONSTRAINT [PK_Role] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [app].[Tenant] (
    [Id] int NOT NULL IDENTITY,
    [Slug] varchar(40) NOT NULL,
    [Name] nvarchar(100) NOT NULL,
    [JoinCode] char(6) NOT NULL,
    [TimeZoneId] varchar(64) NOT NULL,
    [ThemeColor] varchar(20) NOT NULL,
    [AppName] nvarchar(30) NULL,
    [AppShortName] nvarchar(12) NULL,
    [MissionEnabled] bit NOT NULL,
    [ReadingEnabled] bit NOT NULL,
    [ReadingBoardPublic] bit NOT NULL,
    [JoinListed] bit NOT NULL,
    [IsLobby] bit NOT NULL,
    [Milestones] varchar(100) NULL,
    [Status] varchar(20) NOT NULL,
    [CreatedUtc] datetime2(3) NOT NULL,
    CONSTRAINT [PK_Tenant] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Tenant_JoinCode] CHECK (JoinCode NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN),
    CONSTRAINT [CK_Tenant_Program] CHECK (MissionEnabled = 1 OR ReadingEnabled = 1),
    CONSTRAINT [CK_Tenant_Slug] CHECK (LEN(Slug) >= 2 AND Slug NOT LIKE '%[^a-z0-9-]%'),
    CONSTRAINT [CK_Tenant_Status] CHECK (Status IN ('Active','Suspended'))
);
GO

CREATE TABLE [Identity].[User] (
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
);
GO

CREATE TABLE [app].[BibleBookOverview] (
    [BookId] tinyint NOT NULL,
    [Author] nvarchar(200) NULL,
    [WhenWritten] nvarchar(200) NULL,
    [Summary] nvarchar(max) NULL,
    [Purpose] nvarchar(max) NULL,
    [DetailAuthor] nvarchar(200) NULL,
    [AuthorDescription] nvarchar(max) NULL,
    [WrittenFrom] nvarchar(20) NULL,
    [WrittenTo] nvarchar(20) NULL,
    [AlternativeDate] nvarchar(100) NULL,
    [WrittenPlace] nvarchar(200) NULL,
    [WrittenTarget] nvarchar(max) NULL,
    [HistoricalPeriod] nvarchar(100) NULL,
    [HistoricalBackground] nvarchar(max) NULL,
    [Keywords] nvarchar(300) NULL,
    [ContentSummary] nvarchar(max) NULL,
    [VideoUrl1] nvarchar(200) NULL,
    [VideoUrl2] nvarchar(200) NULL,
    [UpdatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_BibleBookOverview] PRIMARY KEY ([BookId]),
    CONSTRAINT [FK_BibleBookOverview_Book] FOREIGN KEY ([BookId]) REFERENCES [app].[BibleBook] ([Id])
);
GO

CREATE TABLE [app].[BibleChapter] (
    [BookId] tinyint NOT NULL,
    [Chapter] smallint NOT NULL,
    [VerseCount] smallint NOT NULL,
    CONSTRAINT [PK_BibleChapter] PRIMARY KEY ([BookId], [Chapter]),
    CONSTRAINT [CK_BibleChapter_Verses] CHECK (VerseCount >= 1),
    CONSTRAINT [FK_BibleChapter_Book] FOREIGN KEY ([BookId]) REFERENCES [app].[BibleBook] ([Id])
);
GO

CREATE TABLE [app].[ChapterMedia] (
    [Id] int NOT NULL IDENTITY,
    [BookId] tinyint NOT NULL,
    [Chapter] smallint NOT NULL,
    [Kind] varchar(20) NOT NULL,
    [SortOrder] tinyint NOT NULL,
    [YoutubeVideoId] varchar(20) NOT NULL,
    [Title] nvarchar(200) NULL,
    [DurationSec] int NULL,
    [UpdatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_ChapterMedia] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ChapterMedia_Kind] CHECK (Kind IN ('Listen','Overview')),
    CONSTRAINT [FK_ChapterMedia_Book] FOREIGN KEY ([BookId]) REFERENCES [app].[BibleBook] ([Id])
);
GO

CREATE TABLE [app].[BibleVerse] (
    [TranslationId] tinyint NOT NULL,
    [BookId] tinyint NOT NULL,
    [Chapter] smallint NOT NULL,
    [Verse] smallint NOT NULL,
    [Text] nvarchar(1000) NOT NULL,
    CONSTRAINT [PK_BibleVerse] PRIMARY KEY ([TranslationId], [BookId], [Chapter], [Verse]),
    CONSTRAINT [FK_BibleVerse_Book] FOREIGN KEY ([BookId]) REFERENCES [app].[BibleBook] ([Id]),
    CONSTRAINT [FK_BibleVerse_Translation] FOREIGN KEY ([TranslationId]) REFERENCES [app].[BibleTranslation] ([Id])
);
GO

CREATE TABLE [dbo].[DM2_ReadingChapterProgress] (
    [UserName] nvarchar(256) NOT NULL,
    [RoundNo] int NOT NULL,
    [BookId] tinyint NOT NULL,
    [Chapter] smallint NOT NULL,
    [LastViewedVerse] smallint NOT NULL,
    [UpdatedAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_DM2_ReadingChapterProgress] PRIMARY KEY ([UserName], [RoundNo], [BookId], [Chapter]),
    CONSTRAINT [CK_DM2_ReadingChapterProgress_Verse] CHECK ([LastViewedVerse]>=(1)),
    CONSTRAINT [FK_DM2_ReadingChapterProgress_Round] FOREIGN KEY ([UserName], [RoundNo]) REFERENCES [dbo].[DM2_ReadingRound] ([UserName], [RoundNo])
);
GO

CREATE TABLE [dbo].[DM2_ReadingChapterRead] (
    [UserName] nvarchar(256) NOT NULL,
    [RoundNo] int NOT NULL,
    [BookId] tinyint NOT NULL,
    [Chapter] smallint NOT NULL,
    [ReadAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_DM2_ReadingChapterRead] PRIMARY KEY ([UserName], [RoundNo], [BookId], [Chapter]),
    CONSTRAINT [FK_DM2_ReadingChapterRead_Round] FOREIGN KEY ([UserName], [RoundNo]) REFERENCES [dbo].[DM2_ReadingRound] ([UserName], [RoundNo])
);
GO

CREATE TABLE [dbo].[DM2_ReadingVerse] (
    [UserName] nvarchar(256) NOT NULL,
    [RoundNo] int NOT NULL,
    [BookId] tinyint NOT NULL,
    [Chapter] smallint NOT NULL,
    [Verse] smallint NOT NULL,
    [ReadAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_DM2_ReadingVerse] PRIMARY KEY ([UserName], [RoundNo], [BookId], [Chapter], [Verse]),
    CONSTRAINT [FK_DM2_ReadingVerse_Round] FOREIGN KEY ([UserName], [RoundNo]) REFERENCES [dbo].[DM2_ReadingRound] ([UserName], [RoundNo])
);
GO

CREATE TABLE [Identity].[RoleClaims] (
    [Id] int NOT NULL IDENTITY,
    [RoleId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_RoleClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_RoleClaims_Role_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Identity].[Role] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [dbo].[CodeData] (
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
);
GO

CREATE TABLE [app].[DailyPrayer] (
    [Id] int NOT NULL IDENTITY,
    [DayNo] tinyint NULL,
    [ExposeDate] date NULL,
    [TraitName] nvarchar(30) NOT NULL,
    [TraitNameEn] nvarchar(40) NULL,
    [Paragraphs] nvarchar(max) NOT NULL,
    [TenantId] int NULL,
    [Revision] int NOT NULL,
    [IsCurrent] bit NOT NULL,
    [Status] varchar(20) NOT NULL,
    [ReplacesId] int NULL,
    [CreatedUtc] datetime2(3) NOT NULL,
    [CreatedByUserName] nvarchar(256) NOT NULL,
    [BookId] tinyint NOT NULL,
    [StartChapter] smallint NOT NULL,
    [StartVerse] smallint NOT NULL,
    [EndChapter] smallint NOT NULL,
    [EndVerse] smallint NOT NULL,
    CONSTRAINT [PK_DailyPrayer] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_DailyPrayer_Paragraphs] CHECK (ISJSON(Paragraphs) = 1),
    CONSTRAINT [CK_DailyPrayer_Range] CHECK (EndChapter > StartChapter OR (EndChapter = StartChapter AND EndVerse >= StartVerse)),
    CONSTRAINT [CK_DailyPrayer_Slot] CHECK ((TenantId IS NULL AND DayNo BETWEEN 1 AND 31 AND ExposeDate IS NULL)
OR (TenantId IS NOT NULL AND DayNo IS NULL AND ExposeDate IS NOT NULL)),
    CONSTRAINT [CK_DailyPrayer_Status] CHECK (Status IN ('Draft','Published','Archived')),
    CONSTRAINT [FK_DailyPrayer_Book] FOREIGN KEY ([BookId]) REFERENCES [app].[BibleBook] ([Id]),
    CONSTRAINT [FK_DailyPrayer_Replaces] FOREIGN KEY ([ReplacesId]) REFERENCES [app].[DailyPrayer] ([Id]),
    CONSTRAINT [FK_DailyPrayer_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);
GO

CREATE TABLE [app].[DailyQuiz] (
    [Id] int NOT NULL IDENTITY,
    [ExposeDate] date NOT NULL,
    [Question] nvarchar(1000) NOT NULL,
    [Background] nvarchar(max) NULL,
    [Format] varchar(10) NOT NULL,
    [CorrectChoiceId] int NULL,
    [Explanation] nvarchar(max) NULL,
    [TenantId] int NULL,
    [Revision] int NOT NULL,
    [IsCurrent] bit NOT NULL,
    [Status] varchar(20) NOT NULL,
    [ReplacesId] int NULL,
    [CreatedUtc] datetime2(3) NOT NULL,
    [CreatedByUserName] nvarchar(256) NOT NULL,
    CONSTRAINT [PK_DailyQuiz] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_DailyQuiz_Correct] CHECK (Status <> 'Published' OR Format = 'Text' OR CorrectChoiceId IS NOT NULL),
    CONSTRAINT [CK_DailyQuiz_Format] CHECK (Format IN ('Choice','Text')),
    CONSTRAINT [CK_DailyQuiz_Status] CHECK (Status IN ('Draft','Published','Archived')),
    CONSTRAINT [FK_DailyQuiz_Replaces] FOREIGN KEY ([ReplacesId]) REFERENCES [app].[DailyQuiz] ([Id]),
    CONSTRAINT [FK_DailyQuiz_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);
GO

CREATE TABLE [app].[DailyWord] (
    [Id] int NOT NULL IDENTITY,
    [ExposeDate] date NOT NULL,
    [Title] nvarchar(100) NOT NULL,
    [Summary] nvarchar(max) NOT NULL,
    [Commentary] nvarchar(max) NULL,
    [TenantId] int NULL,
    [Revision] int NOT NULL,
    [IsCurrent] bit NOT NULL,
    [Status] varchar(20) NOT NULL,
    [ReplacesId] int NULL,
    [CreatedUtc] datetime2(3) NOT NULL,
    [CreatedByUserName] nvarchar(256) NOT NULL,
    [BookId] tinyint NOT NULL,
    [StartChapter] smallint NOT NULL,
    [StartVerse] smallint NOT NULL,
    [EndChapter] smallint NOT NULL,
    [EndVerse] smallint NOT NULL,
    CONSTRAINT [PK_DailyWord] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_DailyWord_Range] CHECK (EndChapter > StartChapter OR (EndChapter = StartChapter AND EndVerse >= StartVerse)),
    CONSTRAINT [CK_DailyWord_Status] CHECK (Status IN ('Draft','Published','Archived')),
    CONSTRAINT [FK_DailyWord_Book] FOREIGN KEY ([BookId]) REFERENCES [app].[BibleBook] ([Id]),
    CONSTRAINT [FK_DailyWord_Replaces] FOREIGN KEY ([ReplacesId]) REFERENCES [app].[DailyWord] ([Id]),
    CONSTRAINT [FK_DailyWord_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);
GO

CREATE TABLE [dbo].[DeptRoleUserAssoc] (
    [TenantId] int NOT NULL,
    [Id] int NOT NULL IDENTITY,
    [RoleCode] nvarchar(50) NULL,
    [UserId] nvarchar(450) NULL,
    [InsertDT] datetime NOT NULL,
    [updateDT] datetime NOT NULL,
    CONSTRAINT [PK_DeptRoleUserAssoc] PRIMARY KEY ([TenantId], [Id]),
    CONSTRAINT [FK_DeptRoleUserAssoc_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);
GO

CREATE TABLE [dbo].[DM2_FamilyMilestoneConfig] (
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
    CONSTRAINT [CK_DM2FMC_Type] CHECK ([MilestoneType]='APPLAUSE' OR [MilestoneType]='AWARD'),
    CONSTRAINT [FK_DM2_FamilyMilestoneConfig_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);
GO

CREATE TABLE [dbo].[DM2_FamilyNotice] (
    [TenantId] int NOT NULL,
    [Id] int NOT NULL IDENTITY,
    [UserName] nvarchar(200) NOT NULL,
    [FamilyTeamId] int NOT NULL,
    [TeamName] nvarchar(100) NOT NULL,
    [Kind] varchar(10) NOT NULL DEFAULT 'JOINED',
    [CreatedAt] datetime NOT NULL,
    CONSTRAINT [PK_DM2_FamilyNotice] PRIMARY KEY ([TenantId], [Id]),
    CONSTRAINT [FK_DM2_FamilyNotice_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);
GO

CREATE TABLE [dbo].[DM2_FamilyTeam] (
    [TenantId] int NOT NULL,
    [FamilyTeamId] int NOT NULL IDENTITY,
    [TeamName] nvarchar(50) NOT NULL,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [Memo] nvarchar(200) NULL,
    [InsertDT] datetime NOT NULL,
    [UpdateDT] datetime NOT NULL,
    [AwardKey] nvarchar(100) NULL,
    [CreatorUserName] nvarchar(200) NULL,
    [TeamType] varchar(10) NOT NULL DEFAULT 'FAMILY',
    CONSTRAINT [PK_DM2_FamilyTeam] PRIMARY KEY ([TenantId], [FamilyTeamId]),
    CONSTRAINT [CK_DM2_FamilyTeam_TeamType] CHECK ([TeamType]='FRIEND' OR [TeamType]='FAMILY'),
    CONSTRAINT [FK_DM2_FamilyTeam_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);
GO

CREATE TABLE [dbo].[DM2_MissionActivity] (
    [TenantId] int NOT NULL,
    [ActivityId] bigint NOT NULL IDENTITY,
    [UserName] nvarchar(256) NOT NULL,
    [MissionDate] date NOT NULL,
    [MissionItem] varchar(20) NOT NULL,
    [Points] int NOT NULL,
    [ReciteWeekStart] date NULL,
    [PerformedAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_DM2_MissionActivity] PRIMARY KEY ([TenantId], [ActivityId]),
    CONSTRAINT [CK_DM2_MissionActivity_Item] CHECK ([MissionItem]='Recite' OR [MissionItem]='Quiz.Correct' OR [MissionItem]='Prayer.Others' OR [MissionItem]='Prayer.Me' OR [MissionItem]='Word.Parent' OR [MissionItem]='Word.Me'),
    CONSTRAINT [CK_DM2_MissionActivity_Points] CHECK ([Points]>=(0)),
    CONSTRAINT [CK_DM2_MissionActivity_Week] CHECK ([MissionItem]='Recite' AND [ReciteWeekStart] IS NOT NULL OR [MissionItem]<>'Recite' AND [ReciteWeekStart] IS NULL),
    CONSTRAINT [FK_DM2_MissionActivity_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);
GO

CREATE TABLE [dbo].[DM2_QuizAttempt] (
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
);
GO

CREATE TABLE [dbo].[DM2_TeamAward] (
    [TenantId] int NOT NULL,
    [AwardId] int NOT NULL IDENTITY,
    [Kind] nvarchar(10) NOT NULL,
    [AwardKey] nvarchar(200) NOT NULL,
    [Threshold] int NOT NULL,
    [AwardedAt] datetime NOT NULL,
    CONSTRAINT [PK_DM2_TeamAward] PRIMARY KEY ([TenantId], [AwardId]),
    CONSTRAINT [CK_DM2_TeamAward_Kind] CHECK ([Kind]=N'FRIEND' OR [Kind]=N'FAMILY'),
    CONSTRAINT [FK_DM2_TeamAward_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);
GO

CREATE TABLE [app].[GuardianLink] (
    [Id] int NOT NULL IDENTITY,
    [TenantId] int NOT NULL,
    [GuardianUserName] nvarchar(256) NOT NULL,
    [StudentUserName] nvarchar(256) NOT NULL,
    [CreatedUtc] datetime2(3) NOT NULL,
    [CreatedByUserName] nvarchar(256) NOT NULL,
    CONSTRAINT [PK_GuardianLink] PRIMARY KEY NONCLUSTERED ([Id]),
    CONSTRAINT [UQ_GuardianLink_TenantId_Id] UNIQUE CLUSTERED ([TenantId], [Id]),
    CONSTRAINT [FK_GuardianLink_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);
GO

CREATE TABLE [app].[PromotionRequest] (
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
    CONSTRAINT [CK_PromotionRequest_Status] CHECK (Status IN ('Pending','Approved','Rejected','Cancelled')),
    CONSTRAINT [CK_PromotionRequest_Tenants] CHECK (SourceTenantId <> TargetTenantId),
    CONSTRAINT [FK_PromotionRequest_Source] FOREIGN KEY ([SourceTenantId]) REFERENCES [app].[Tenant] ([Id]),
    CONSTRAINT [FK_PromotionRequest_Target] FOREIGN KEY ([TargetTenantId]) REFERENCES [app].[Tenant] ([Id])
);
GO

CREATE TABLE [app].[SearchLog] (
    [Id] bigint NOT NULL IDENTITY,
    [TenantId] int NOT NULL,
    [Term] nvarchar(50) NOT NULL,
    [SearchedUtc] datetime2(3) NOT NULL,
    CONSTRAINT [PK_SearchLog] PRIMARY KEY NONCLUSTERED ([Id]),
    CONSTRAINT [FK_SearchLog_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);
GO

CREATE TABLE [dbo].[SignupApplication] (
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
);
GO

CREATE TABLE [app].[TenantSeason] (
    [TenantId] int NOT NULL,
    [Year] smallint NOT NULL,
    [StartDate] date NOT NULL,
    [ChangedUtc] datetime2(3) NOT NULL,
    [ChangedByUserName] nvarchar(256) NOT NULL,
    CONSTRAINT [PK_TenantSeason] PRIMARY KEY ([TenantId], [Year]),
    CONSTRAINT [CK_TenantSeason_StartInYear] CHECK (YEAR(StartDate) = [Year]),
    CONSTRAINT [FK_TenantSeason_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);
GO

CREATE TABLE [app].[UserSetting] (
    [UserName] nvarchar(256) NOT NULL,
    [MustChangePassword] bit NOT NULL,
    [PhotoPath] nvarchar(260) NULL,
    [LastTenantId] int NULL,
    [ReadingTheme] varchar(20) NULL,
    [MissionTheme] varchar(20) NULL,
    [FamilyInvitesSnoozedAt] datetime2 NULL,
    CONSTRAINT [PK_UserSetting] PRIMARY KEY ([UserName]),
    CONSTRAINT [FK_UserSetting_LastTenant] FOREIGN KEY ([LastTenantId]) REFERENCES [app].[Tenant] ([Id])
);
GO

CREATE TABLE [app].[WeeklyVerse] (
    [Id] int NOT NULL IDENTITY,
    [WeekNo] tinyint NULL,
    [StartDate] date NULL,
    [EndDate] date NULL,
    [Title] nvarchar(200) NOT NULL,
    [LessonNo] smallint NULL,
    [LessonSummary] nvarchar(200) NULL,
    [LessonPoint] nvarchar(max) NULL,
    [LessonApply] nvarchar(max) NULL,
    [TenantId] int NULL,
    [Revision] int NOT NULL,
    [IsCurrent] bit NOT NULL,
    [Status] varchar(20) NOT NULL,
    [ReplacesId] int NULL,
    [CreatedUtc] datetime2(3) NOT NULL,
    [CreatedByUserName] nvarchar(256) NOT NULL,
    [BookId] tinyint NOT NULL,
    [StartChapter] smallint NOT NULL,
    [StartVerse] smallint NOT NULL,
    [EndChapter] smallint NOT NULL,
    [EndVerse] smallint NOT NULL,
    CONSTRAINT [PK_WeeklyVerse] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_WeeklyVerse_Range] CHECK (EndChapter > StartChapter OR (EndChapter = StartChapter AND EndVerse >= StartVerse)),
    CONSTRAINT [CK_WeeklyVerse_Slot] CHECK ((TenantId IS NULL AND WeekNo BETWEEN 1 AND 55 AND StartDate IS NULL AND EndDate IS NULL)
OR (TenantId IS NOT NULL AND WeekNo IS NULL AND DATEDIFF(DAY, '19000107', StartDate) % 7 = 0 AND EndDate = DATEADD(DAY, 6, StartDate))),
    CONSTRAINT [CK_WeeklyVerse_Status] CHECK (Status IN ('Draft','Published','Archived')),
    CONSTRAINT [FK_WeeklyVerse_Book] FOREIGN KEY ([BookId]) REFERENCES [app].[BibleBook] ([Id]),
    CONSTRAINT [FK_WeeklyVerse_Replaces] FOREIGN KEY ([ReplacesId]) REFERENCES [app].[WeeklyVerse] ([Id]),
    CONSTRAINT [FK_WeeklyVerse_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
);
GO

CREATE TABLE [Identity].[UserClaims] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_UserClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_UserClaims_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [Identity].[User] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [Identity].[UserLogins] (
    [LoginProvider] nvarchar(450) NOT NULL,
    [ProviderKey] nvarchar(450) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_UserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_UserLogins_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [Identity].[User] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [Identity].[UserRoles] (
    [UserId] nvarchar(450) NOT NULL,
    [RoleId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_UserRoles] PRIMARY KEY ([UserId], [RoleId]),
    CONSTRAINT [FK_UserRoles_Role_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Identity].[Role] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_UserRoles_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [Identity].[User] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [Identity].[UserTokens] (
    [UserId] nvarchar(450) NOT NULL,
    [LoginProvider] nvarchar(450) NOT NULL,
    [Name] nvarchar(450) NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_UserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_UserTokens_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [Identity].[User] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [app].[DailyQuizAcceptedAnswer] (
    [DailyQuizId] int NOT NULL,
    [NormalizedText] nvarchar(200) NOT NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_DailyQuizAcceptedAnswer] PRIMARY KEY ([DailyQuizId], [NormalizedText]),
    CONSTRAINT [FK_DailyQuizAcceptedAnswer_Quiz] FOREIGN KEY ([DailyQuizId]) REFERENCES [app].[DailyQuiz] ([Id])
);
GO

CREATE TABLE [app].[DailyQuizChoice] (
    [Id] int NOT NULL IDENTITY,
    [TenantId] int NULL,
    [DailyQuizId] int NOT NULL,
    [SortOrder] tinyint NOT NULL,
    [Text] nvarchar(300) NOT NULL,
    CONSTRAINT [PK_DailyQuizChoice] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_DailyQuizChoice_Id_Quiz] UNIQUE ([Id], [DailyQuizId]),
    CONSTRAINT [FK_DailyQuizChoice_Quiz] FOREIGN KEY ([DailyQuizId]) REFERENCES [app].[DailyQuiz] ([Id])
);
GO

CREATE TABLE [dbo].[DM2_FamilyMilestoneHidden] (
    [TenantId] int NOT NULL,
    [Id] int NOT NULL IDENTITY,
    [MilestoneConfigId] int NOT NULL,
    [FamilyTeamId] int NOT NULL,
    [HiddenAt] datetime2(7) NOT NULL,
    CONSTRAINT [PK_DM2_FamilyMilestoneHidden] PRIMARY KEY ([TenantId], [Id]),
    CONSTRAINT [FK_DM2FMH_Config] FOREIGN KEY ([TenantId], [MilestoneConfigId]) REFERENCES [dbo].[DM2_FamilyMilestoneConfig] ([TenantId], [Id])
);
GO

CREATE TABLE [dbo].[DM2_FamilyMember] (
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
);
GO

CREATE TABLE [dbo].[DM2_FamilyRequest] (
    [TenantId] int NOT NULL,
    [Id] int NOT NULL IDENTITY,
    [FamilyTeamId] int NOT NULL,
    [Kind] char(1) NOT NULL,
    [TargetUserName] nvarchar(200) NOT NULL,
    [FromUserName] nvarchar(200) NOT NULL,
    [RelationKey] varchar(10) NOT NULL,
    [CreatedAt] datetime NOT NULL,
    CONSTRAINT [PK_DM2_FamilyRequest] PRIMARY KEY ([TenantId], [Id]),
    CONSTRAINT [CK_DM2_FamilyRequest_Kind] CHECK ([Kind]='I' OR [Kind]='R'),
    CONSTRAINT [FK_DM2_FamilyRequest_Team] FOREIGN KEY ([TenantId], [FamilyTeamId]) REFERENCES [dbo].[DM2_FamilyTeam] ([TenantId], [FamilyTeamId])
);
GO

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ChapterCount', N'Intro', N'Name', N'ShortName', N'Testament') AND [object_id] = OBJECT_ID(N'[app].[BibleBook]'))
    SET IDENTITY_INSERT [app].[BibleBook] ON;
INSERT INTO [app].[BibleBook] ([Id], [ChapterCount], [Intro], [Name], [ShortName], [Testament])
VALUES (CAST(1 AS tinyint), CAST(50 AS smallint), NULL, N'창세기', N'창', 'OT'),
(CAST(2 AS tinyint), CAST(40 AS smallint), NULL, N'출애굽기', N'출', 'OT'),
(CAST(3 AS tinyint), CAST(27 AS smallint), NULL, N'레위기', N'레', 'OT'),
(CAST(4 AS tinyint), CAST(36 AS smallint), NULL, N'민수기', N'민', 'OT'),
(CAST(5 AS tinyint), CAST(34 AS smallint), NULL, N'신명기', N'신', 'OT'),
(CAST(6 AS tinyint), CAST(24 AS smallint), NULL, N'여호수아', N'수', 'OT'),
(CAST(7 AS tinyint), CAST(21 AS smallint), NULL, N'사사기', N'삿', 'OT'),
(CAST(8 AS tinyint), CAST(4 AS smallint), NULL, N'룻기', N'룻', 'OT'),
(CAST(9 AS tinyint), CAST(31 AS smallint), NULL, N'사무엘상', N'삼상', 'OT'),
(CAST(10 AS tinyint), CAST(24 AS smallint), NULL, N'사무엘하', N'삼하', 'OT'),
(CAST(11 AS tinyint), CAST(22 AS smallint), NULL, N'열왕기상', N'왕상', 'OT'),
(CAST(12 AS tinyint), CAST(25 AS smallint), NULL, N'열왕기하', N'왕하', 'OT'),
(CAST(13 AS tinyint), CAST(29 AS smallint), NULL, N'역대상', N'대상', 'OT'),
(CAST(14 AS tinyint), CAST(36 AS smallint), NULL, N'역대하', N'대하', 'OT'),
(CAST(15 AS tinyint), CAST(10 AS smallint), NULL, N'에스라', N'스', 'OT'),
(CAST(16 AS tinyint), CAST(13 AS smallint), NULL, N'느헤미야', N'느', 'OT'),
(CAST(17 AS tinyint), CAST(10 AS smallint), NULL, N'에스더', N'에', 'OT'),
(CAST(18 AS tinyint), CAST(42 AS smallint), NULL, N'욥기', N'욥', 'OT'),
(CAST(19 AS tinyint), CAST(150 AS smallint), NULL, N'시편', N'시', 'OT'),
(CAST(20 AS tinyint), CAST(31 AS smallint), NULL, N'잠언', N'잠', 'OT'),
(CAST(21 AS tinyint), CAST(12 AS smallint), NULL, N'전도서', N'전', 'OT'),
(CAST(22 AS tinyint), CAST(8 AS smallint), NULL, N'아가', N'아', 'OT'),
(CAST(23 AS tinyint), CAST(66 AS smallint), NULL, N'이사야', N'사', 'OT'),
(CAST(24 AS tinyint), CAST(52 AS smallint), NULL, N'예레미야', N'렘', 'OT'),
(CAST(25 AS tinyint), CAST(5 AS smallint), NULL, N'예레미야애가', N'애', 'OT'),
(CAST(26 AS tinyint), CAST(48 AS smallint), NULL, N'에스겔', N'겔', 'OT'),
(CAST(27 AS tinyint), CAST(12 AS smallint), NULL, N'다니엘', N'단', 'OT'),
(CAST(28 AS tinyint), CAST(14 AS smallint), NULL, N'호세아', N'호', 'OT'),
(CAST(29 AS tinyint), CAST(3 AS smallint), NULL, N'요엘', N'욜', 'OT'),
(CAST(30 AS tinyint), CAST(9 AS smallint), NULL, N'아모스', N'암', 'OT'),
(CAST(31 AS tinyint), CAST(1 AS smallint), NULL, N'오바댜', N'옵', 'OT'),
(CAST(32 AS tinyint), CAST(4 AS smallint), NULL, N'요나', N'욘', 'OT'),
(CAST(33 AS tinyint), CAST(7 AS smallint), NULL, N'미가', N'미', 'OT'),
(CAST(34 AS tinyint), CAST(3 AS smallint), NULL, N'나훔', N'나', 'OT'),
(CAST(35 AS tinyint), CAST(3 AS smallint), NULL, N'하박국', N'합', 'OT'),
(CAST(36 AS tinyint), CAST(3 AS smallint), NULL, N'스바냐', N'습', 'OT'),
(CAST(37 AS tinyint), CAST(2 AS smallint), NULL, N'학개', N'학', 'OT'),
(CAST(38 AS tinyint), CAST(14 AS smallint), NULL, N'스가랴', N'슥', 'OT'),
(CAST(39 AS tinyint), CAST(4 AS smallint), NULL, N'말라기', N'말', 'OT'),
(CAST(40 AS tinyint), CAST(28 AS smallint), NULL, N'마태복음', N'마', 'NT'),
(CAST(41 AS tinyint), CAST(16 AS smallint), NULL, N'마가복음', N'막', 'NT'),
(CAST(42 AS tinyint), CAST(24 AS smallint), NULL, N'누가복음', N'눅', 'NT');
INSERT INTO [app].[BibleBook] ([Id], [ChapterCount], [Intro], [Name], [ShortName], [Testament])
VALUES (CAST(43 AS tinyint), CAST(21 AS smallint), NULL, N'요한복음', N'요', 'NT'),
(CAST(44 AS tinyint), CAST(28 AS smallint), NULL, N'사도행전', N'행', 'NT'),
(CAST(45 AS tinyint), CAST(16 AS smallint), NULL, N'로마서', N'롬', 'NT'),
(CAST(46 AS tinyint), CAST(16 AS smallint), NULL, N'고린도전서', N'고전', 'NT'),
(CAST(47 AS tinyint), CAST(13 AS smallint), NULL, N'고린도후서', N'고후', 'NT'),
(CAST(48 AS tinyint), CAST(6 AS smallint), NULL, N'갈라디아서', N'갈', 'NT'),
(CAST(49 AS tinyint), CAST(6 AS smallint), NULL, N'에베소서', N'엡', 'NT'),
(CAST(50 AS tinyint), CAST(4 AS smallint), NULL, N'빌립보서', N'빌', 'NT'),
(CAST(51 AS tinyint), CAST(4 AS smallint), NULL, N'골로새서', N'골', 'NT'),
(CAST(52 AS tinyint), CAST(5 AS smallint), NULL, N'데살로니가전서', N'살전', 'NT'),
(CAST(53 AS tinyint), CAST(3 AS smallint), NULL, N'데살로니가후서', N'살후', 'NT'),
(CAST(54 AS tinyint), CAST(6 AS smallint), NULL, N'디모데전서', N'딤전', 'NT'),
(CAST(55 AS tinyint), CAST(4 AS smallint), NULL, N'디모데후서', N'딤후', 'NT'),
(CAST(56 AS tinyint), CAST(3 AS smallint), NULL, N'디도서', N'딛', 'NT'),
(CAST(57 AS tinyint), CAST(1 AS smallint), NULL, N'빌레몬서', N'몬', 'NT'),
(CAST(58 AS tinyint), CAST(13 AS smallint), NULL, N'히브리서', N'히', 'NT'),
(CAST(59 AS tinyint), CAST(5 AS smallint), NULL, N'야고보서', N'약', 'NT'),
(CAST(60 AS tinyint), CAST(5 AS smallint), NULL, N'베드로전서', N'벧전', 'NT'),
(CAST(61 AS tinyint), CAST(3 AS smallint), NULL, N'베드로후서', N'벧후', 'NT'),
(CAST(62 AS tinyint), CAST(5 AS smallint), NULL, N'요한일서', N'요일', 'NT'),
(CAST(63 AS tinyint), CAST(1 AS smallint), NULL, N'요한이서', N'요이', 'NT'),
(CAST(64 AS tinyint), CAST(1 AS smallint), NULL, N'요한삼서', N'요삼', 'NT'),
(CAST(65 AS tinyint), CAST(1 AS smallint), NULL, N'유다서', N'유', 'NT'),
(CAST(66 AS tinyint), CAST(22 AS smallint), NULL, N'요한계시록', N'계', 'NT');
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ChapterCount', N'Intro', N'Name', N'ShortName', N'Testament') AND [object_id] = OBJECT_ID(N'[app].[BibleBook]'))
    SET IDENTITY_INSERT [app].[BibleBook] OFF;
GO

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Code', N'Name', N'SortOrder') AND [object_id] = OBJECT_ID(N'[app].[BibleTranslation]'))
    SET IDENTITY_INSERT [app].[BibleTranslation] ON;
INSERT INTO [app].[BibleTranslation] ([Id], [Code], [Name], [SortOrder])
VALUES (CAST(1 AS tinyint), 'KRV', N'개역개정', CAST(1 AS tinyint)),
(CAST(2 AS tinyint), 'KLB', N'현대인의성경', CAST(2 AS tinyint)),
(CAST(3 AS tinyint), 'NIV', N'NIV', CAST(3 AS tinyint));
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Code', N'Name', N'SortOrder') AND [object_id] = OBJECT_ID(N'[app].[BibleTranslation]'))
    SET IDENTITY_INSERT [app].[BibleTranslation] OFF;
GO

CREATE INDEX [IX_AuditLog_Occurred] ON [app].[AuditLog] ([OccurredUtc]);
GO

CREATE UNIQUE INDEX [UQ_BibleTranslation_Code] ON [app].[BibleTranslation] ([Code]);
GO

CREATE INDEX [IX_BibleVerse_BookId] ON [app].[BibleVerse] ([BookId]);
GO

CREATE UNIQUE INDEX [UQ_ChapterMedia_Slot] ON [app].[ChapterMedia] ([BookId], [Chapter], [Kind], [SortOrder]);
GO

CREATE INDEX [IX_DailyPrayer_BookId] ON [app].[DailyPrayer] ([BookId]);
GO

CREATE INDEX [IX_DailyPrayer_ReplacesId] ON [app].[DailyPrayer] ([ReplacesId]);
GO

CREATE UNIQUE INDEX [UX_DailyPrayer_Date] ON [app].[DailyPrayer] ([TenantId], [ExposeDate]) WHERE [IsCurrent] = 1 AND [ExposeDate] IS NOT NULL;
GO

CREATE UNIQUE INDEX [UX_DailyPrayer_Day] ON [app].[DailyPrayer] ([DayNo]) WHERE [IsCurrent] = 1 AND [TenantId] IS NULL;
GO

CREATE INDEX [IX_DailyQuiz_ReplacesId] ON [app].[DailyQuiz] ([ReplacesId]);
GO

CREATE UNIQUE INDEX [UX_DailyQuiz_Date] ON [app].[DailyQuiz] ([TenantId], [ExposeDate]) WHERE [IsCurrent] = 1;
GO

CREATE UNIQUE INDEX [UQ_DailyQuizChoice] ON [app].[DailyQuizChoice] ([DailyQuizId], [SortOrder]);
GO

CREATE INDEX [IX_DailyWord_BookId] ON [app].[DailyWord] ([BookId]);
GO

CREATE INDEX [IX_DailyWord_ReplacesId] ON [app].[DailyWord] ([ReplacesId]);
GO

CREATE UNIQUE INDEX [UX_DailyWord_Date] ON [app].[DailyWord] ([TenantId], [ExposeDate]) WHERE [IsCurrent] = 1;
GO

CREATE UNIQUE INDEX [UQ_DailyWordClip_Slot] ON [app].[DailyWordClip] ([WordDate], [Seq]);
GO

CREATE INDEX [IX_DeptRoleUserAssoc_UserId] ON [dbo].[DeptRoleUserAssoc] ([UserId]);
GO

CREATE INDEX [IX_DM2_FamilyMember_FamilyTeamId_IsActive] ON [dbo].[DM2_FamilyMember] ([TenantId], [FamilyTeamId], [IsActive]);
GO

CREATE INDEX [IX_DM2_FamilyMember_Team] ON [dbo].[DM2_FamilyMember] ([TenantId], [FamilyTeamId], [RoleCode], [IsActive]);
GO

CREATE INDEX [IX_DM2_FamilyMember_UserName_IsActive] ON [dbo].[DM2_FamilyMember] ([TenantId], [UserName], [IsActive]);
GO

CREATE UNIQUE INDEX [UX_DM2_FamilyMember_Team_Self] ON [dbo].[DM2_FamilyMember] ([TenantId], [FamilyTeamId], [RelationCode]) WHERE [IsActive]=(1) AND [RoleCode]=(0) AND [RelationCode]=(0);
GO

CREATE UNIQUE INDEX [UX_DM2_FamilyMember_Team_User] ON [dbo].[DM2_FamilyMember] ([TenantId], [FamilyTeamId], [UserName]);
GO

CREATE UNIQUE INDEX [UX_DM2_FamilyMember_UserName] ON [dbo].[DM2_FamilyMember] ([UserName]);
GO

CREATE INDEX [IX_DM2_FamilyMilestoneConfig_Year_IsActive] ON [dbo].[DM2_FamilyMilestoneConfig] ([TenantId], [Year], [IsActive]);
GO

CREATE UNIQUE INDEX [UQ_DM2FMC_YearPoint] ON [dbo].[DM2_FamilyMilestoneConfig] ([TenantId], [Year], [PointThreshold]);
GO

CREATE INDEX [IX_DM2FMH_ConfigId] ON [dbo].[DM2_FamilyMilestoneHidden] ([TenantId], [MilestoneConfigId]);
GO

CREATE UNIQUE INDEX [UQ_DM2FMH_ConfigTeam] ON [dbo].[DM2_FamilyMilestoneHidden] ([TenantId], [MilestoneConfigId], [FamilyTeamId]);
GO

CREATE INDEX [IX_DM2_FamilyNotice_User] ON [dbo].[DM2_FamilyNotice] ([TenantId], [UserName]);
GO

CREATE INDEX [IX_DM2_FamilyRequest_From] ON [dbo].[DM2_FamilyRequest] ([TenantId], [FromUserName], [Kind]);
GO

CREATE INDEX [IX_DM2_FamilyRequest_Target] ON [dbo].[DM2_FamilyRequest] ([TenantId], [TargetUserName], [Kind]);
GO

CREATE UNIQUE INDEX [UX_DM2_FamilyRequest_Team_Kind_Target] ON [dbo].[DM2_FamilyRequest] ([TenantId], [FamilyTeamId], [Kind], [TargetUserName]);
GO

CREATE INDEX [IX_DM2_FamilyTeam_IsActive] ON [dbo].[DM2_FamilyTeam] ([TenantId], [IsActive]);
GO

CREATE UNIQUE INDEX [UX_DM2_FamilyTeam_AwardKey] ON [dbo].[DM2_FamilyTeam] ([TenantId], [AwardKey]) WHERE [AwardKey] IS NOT NULL;
GO

CREATE INDEX [IX_DM2_MissionActivity_Date] ON [dbo].[DM2_MissionActivity] ([TenantId], [MissionDate]);
GO

CREATE UNIQUE INDEX [UX_DM2_MissionActivity_Daily] ON [dbo].[DM2_MissionActivity] ([TenantId], [UserName], [MissionDate], [MissionItem]) WHERE [MissionItem]<>'Recite';
GO

CREATE UNIQUE INDEX [UX_DM2_MissionActivity_Recite] ON [dbo].[DM2_MissionActivity] ([TenantId], [UserName], [ReciteWeekStart]) WHERE [MissionItem]='Recite';
GO

CREATE INDEX [IX_DM2_ReadingChapterProgress_Recent] ON [dbo].[DM2_ReadingChapterProgress] ([UserName], [UpdatedAt] DESC);
GO

CREATE INDEX [IX_DM2_ReadingChapterRead_ReadAt] ON [dbo].[DM2_ReadingChapterRead] ([UserName], [ReadAt]);
GO

CREATE UNIQUE INDEX [UX_DM2_ReadingRound_Open] ON [dbo].[DM2_ReadingRound] ([UserName]) WHERE [CompletedAt] IS NULL AND [ClosedAt] IS NULL;
GO

CREATE INDEX [IX_DM2_ReadingVerse_ReadAt] ON [dbo].[DM2_ReadingVerse] ([ReadAt]);
GO

CREATE INDEX [IX_DM2_TeamAward_Key] ON [dbo].[DM2_TeamAward] ([TenantId], [AwardKey]);
GO

CREATE UNIQUE INDEX [UQ_DM2_TeamAward] ON [dbo].[DM2_TeamAward] ([TenantId], [Kind], [AwardKey], [Threshold]);
GO

CREATE INDEX [IX_GuardianLink_Student] ON [app].[GuardianLink] ([TenantId], [StudentUserName]);
GO

CREATE UNIQUE INDEX [UQ_GuardianLink_Pair] ON [app].[GuardianLink] ([TenantId], [GuardianUserName], [StudentUserName]);
GO

CREATE UNIQUE INDEX [UQ_PlatformRuleSetVersion_No] ON [app].[PlatformRuleSetVersion] ([VersionNo]);
GO

CREATE INDEX [IX_PromotionRequest_Source] ON [app].[PromotionRequest] ([SourceTenantId], [Status]);
GO

CREATE INDEX [IX_PromotionRequest_Target] ON [app].[PromotionRequest] ([TargetTenantId], [Status]);
GO

CREATE UNIQUE INDEX [UQ_PromotionRequest_Pending] ON [app].[PromotionRequest] ([UserName], [SourceTenantId]) WHERE [Status] = 'Pending';
GO

CREATE UNIQUE INDEX [RoleNameIndex] ON [Identity].[Role] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;
GO

CREATE INDEX [IX_RoleClaims_RoleId] ON [Identity].[RoleClaims] ([RoleId]);
GO

CREATE INDEX [IX_SearchLog_Recent] ON [app].[SearchLog] ([TenantId], [SearchedUtc]) INCLUDE ([Term]);
GO

CREATE UNIQUE CLUSTERED INDEX [UQ_SearchLog_TenantId_Id] ON [app].[SearchLog] ([TenantId], [Id]);
GO

CREATE UNIQUE INDEX [UQ_Tenant_JoinCode] ON [app].[Tenant] ([JoinCode]);
GO

CREATE UNIQUE INDEX [UQ_Tenant_Slug] ON [app].[Tenant] ([Slug]);
GO

CREATE UNIQUE INDEX [UX_Tenant_Lobby] ON [app].[Tenant] ([IsLobby]) WHERE [IsLobby] = 1;
GO

CREATE INDEX [EmailIndex] ON [Identity].[User] ([NormalizedEmail]);
GO

CREATE INDEX [IX_IdentityUser_UserName] ON [Identity].[User] ([UserName]);
GO

CREATE UNIQUE INDEX [UserNameIndex] ON [Identity].[User] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;
GO

CREATE INDEX [IX_UserClaims_UserId] ON [Identity].[UserClaims] ([UserId]);
GO

CREATE INDEX [IX_UserLogins_UserId] ON [Identity].[UserLogins] ([UserId]);
GO

CREATE INDEX [IX_UserRoles_RoleId] ON [Identity].[UserRoles] ([RoleId]);
GO

CREATE INDEX [IX_UserSetting_LastTenantId] ON [app].[UserSetting] ([LastTenantId]);
GO

CREATE INDEX [IX_WeeklyVerse_BookId] ON [app].[WeeklyVerse] ([BookId]);
GO

CREATE INDEX [IX_WeeklyVerse_ReplacesId] ON [app].[WeeklyVerse] ([ReplacesId]);
GO

CREATE UNIQUE INDEX [UX_WeeklyVerse_Start] ON [app].[WeeklyVerse] ([TenantId], [StartDate]) WHERE [IsCurrent] = 1 AND [StartDate] IS NOT NULL;
GO

CREATE UNIQUE INDEX [UX_WeeklyVerse_Week] ON [app].[WeeklyVerse] ([WeekNo]) WHERE [IsCurrent] = 1 AND [TenantId] IS NULL;
GO

-- =====================================================================
-- RLS — 세션 값: TenantId, UserName, UserId, PlatformScope. 통독 · Identity 는 부서 필터 없음
-- =====================================================================
CREATE FUNCTION sec.fn_Tenant (@TenantId int)
RETURNS TABLE WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS Allowed
    WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
       OR CAST(SESSION_CONTEXT(N'PlatformScope') AS bit) = 1;
GO

CREATE FUNCTION sec.fn_Membership (@TenantId int, @UserId nvarchar(450))
RETURNS TABLE WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS Allowed
    WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
       OR @UserId   = CAST(SESSION_CONTEXT(N'UserId') AS nvarchar(450))
       OR CAST(SESSION_CONTEXT(N'PlatformScope') AS bit) = 1;
GO

CREATE FUNCTION sec.fn_SharedRead (@TenantId int)
RETURNS TABLE WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS Allowed
    WHERE @TenantId IS NULL
       OR @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
       OR CAST(SESSION_CONTEXT(N'PlatformScope') AS bit) = 1;
GO

CREATE FUNCTION sec.fn_SharedWrite (@TenantId int)
RETURNS TABLE WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS Allowed
    WHERE (@TenantId IS NOT NULL AND @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int))
       OR CAST(SESSION_CONTEXT(N'PlatformScope') AS bit) = 1;            -- 공용(NULL)은 플랫폼만
GO

CREATE FUNCTION sec.fn_Person (@UserName nvarchar(256))
RETURNS TABLE WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS Allowed
    WHERE @UserName = CAST(SESSION_CONTEXT(N'UserName') AS nvarchar(256))
       OR CAST(SESSION_CONTEXT(N'PlatformScope') AS bit) = 1;
GO

CREATE FUNCTION sec.fn_ReadingRead (@UserName nvarchar(256))
RETURNS TABLE WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS Allowed
    WHERE @UserName = CAST(SESSION_CONTEXT(N'UserName') AS nvarchar(256))
       OR CAST(SESSION_CONTEXT(N'PlatformScope') AS bit) = 1
       OR (
            EXISTS (SELECT 1 FROM dbo.DeptRoleUserAssoc d
                    JOIN [Identity].[User] u ON u.Id = d.UserId
                    WHERE d.TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
                      AND u.UserName = @UserName)
        AND EXISTS (SELECT 1 FROM dbo.DeptRoleUserAssoc s
                    WHERE s.TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
                      AND s.UserId = CAST(SESSION_CONTEXT(N'UserId') AS nvarchar(450))
                      AND (
                           (s.RoleCode IS NOT NULL AND s.RoleCode NOT IN (N'T1001', N'T1099'))
                        OR EXISTS (SELECT 1 FROM [Identity].[UserRoles] ur
                                   JOIN [Identity].[Role] r ON r.Id = ur.RoleId
                                   WHERE ur.UserId = s.UserId AND r.NormalizedName = N'ADMIN')
                        OR EXISTS (SELECT 1 FROM app.Tenant t
                                   WHERE t.Id = s.TenantId AND t.ReadingBoardPublic = 1)
                      ))
       );
GO

CREATE FUNCTION sec.fn_Promotion (@SourceTenantId int, @TargetTenantId int)
RETURNS TABLE WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS Allowed
    WHERE @SourceTenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
       OR @TargetTenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
       OR CAST(SESSION_CONTEXT(N'PlatformScope') AS bit) = 1;
GO

CREATE SECURITY POLICY sec.TenantIsolation
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
WITH (STATE = ON, SCHEMABINDING = ON);
GO
