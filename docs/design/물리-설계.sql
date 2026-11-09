-- =====================================================================
-- 도전 성경말씀 — 참조 DDL (SQL Server 2019 이상)
-- 설계 확인용. 실제 DB 는 EF Core 마이그레이션으로 만든다(물리-설계.md 5절).
-- 규칙: 현재 시각 함수 기본값 금지, 영역 표는 TenantId 선두 클러스터, 영역 간 참조는 복합 FK.
-- =====================================================================

CREATE SCHEMA app;
GO
CREATE SCHEMA sec;
GO
CREATE SCHEMA [Identity];
GO

-- =====================================================================
-- 1. 플랫폼 범위
-- =====================================================================

CREATE TABLE app.Tenant (
    Id          int IDENTITY      NOT NULL CONSTRAINT PK_Tenant PRIMARY KEY,
    Slug        varchar(40)       NOT NULL CONSTRAINT UQ_Tenant_Slug UNIQUE,
    Name        nvarchar(100)     NOT NULL,
    JoinCode    char(6)           NOT NULL CONSTRAINT UQ_Tenant_JoinCode UNIQUE,   -- 참여 코드(0048)
    TimeZoneId  varchar(64)       NOT NULL,
    ThemeColor  varchar(20)       NOT NULL,
    AppName     nvarchar(30)      NULL,           -- NULL 이면 기본 서비스 이름(0038)
    AppShortName nvarchar(12)     NULL,
    MissionEnabled bit            NOT NULL,       -- 성경말씀(0039)
    ReadingEnabled bit            NOT NULL,       -- 성경통독(0039)
    ReadingBoardPublic bit        NOT NULL,       -- 통독 참여자 현황 소속원 공개(0041, 기본 1)
    Status      varchar(20)       NOT NULL CONSTRAINT CK_Tenant_Status CHECK (Status IN ('Active','Suspended')),
    CreatedUtc  datetime2(3)      NOT NULL,
    CONSTRAINT CK_Tenant_Program CHECK (MissionEnabled = 1 OR ReadingEnabled = 1),   -- R-150
    CONSTRAINT CK_Tenant_Slug CHECK (LEN(Slug) >= 2 AND Slug NOT LIKE '%[^a-z0-9-]%'),
    CONSTRAINT CK_Tenant_JoinCode CHECK (JoinCode NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN)
);



CREATE TABLE app.PlatformRuleSetVersion (
    Id                  int IDENTITY  NOT NULL CONSTRAINT PK_PlatformRuleSetVersion PRIMARY KEY,
    VersionNo           int           NOT NULL CONSTRAINT UQ_PlatformRuleSetVersion_No UNIQUE,
    [Values]            nvarchar(max) NOT NULL CONSTRAINT CK_PlatformRuleSetVersion_Values CHECK (ISJSON([Values]) = 1),
    EffectiveFrom       date          NOT NULL,     -- 0060: 이 날부터 적용(내일 · 다음 주 시작일). 그날 규칙 = 시작일이 지난 버전 중 번호가 가장 큰 것
    ChangedUtc          datetime2(3)  NOT NULL,
    ChangedByUserName  nvarchar(256)           NOT NULL
);

-- 성경 참조 데이터 -----------------------------------------------------

CREATE TABLE app.BibleTranslation (
    Id        tinyint       NOT NULL CONSTRAINT PK_BibleTranslation PRIMARY KEY,
    Code      varchar(20)   NOT NULL CONSTRAINT UQ_BibleTranslation_Code UNIQUE,
    Name      nvarchar(50)  NOT NULL,
    SortOrder tinyint       NOT NULL
);

CREATE TABLE app.BibleBook (
    Id        tinyint       NOT NULL CONSTRAINT PK_BibleBook PRIMARY KEY CONSTRAINT CK_BibleBook_Id CHECK (Id BETWEEN 1 AND 66),
    Name      nvarchar(30)  NOT NULL,
    ShortName nvarchar(10)  NOT NULL,
    Testament char(2)       NOT NULL CONSTRAINT CK_BibleBook_Testament CHECK (Testament IN ('OT','NT')),
    ChapterCount smallint   NOT NULL CONSTRAINT CK_BibleBook_ChapterCount CHECK (ChapterCount >= 1),   -- 합계 1,189(0040)
    Intro     nvarchar(max) NULL                                    -- 책 소개(R-116)
);

CREATE TABLE app.BibleVerse (
    TranslationId tinyint        NOT NULL CONSTRAINT FK_BibleVerse_Translation REFERENCES app.BibleTranslation(Id),
    BookId        tinyint        NOT NULL CONSTRAINT FK_BibleVerse_Book REFERENCES app.BibleBook(Id),
    Chapter       smallint       NOT NULL,
    Verse         smallint       NOT NULL,
    Text          nvarchar(1000) NOT NULL,
    CONSTRAINT PK_BibleVerse PRIMARY KEY (TranslationId, BookId, Chapter, Verse)
);

-- 퀴즈 문제 은행 (오늘의 퀴즈를 만들 때 복사해 씀) ----------------------

CREATE TABLE app.QuizBankItem (
    Id          int IDENTITY   NOT NULL CONSTRAINT PK_QuizBankItem PRIMARY KEY,
    Question    nvarchar(1000) NOT NULL,
    Background  nvarchar(max)  NULL,
    Format      varchar(10)    NOT NULL CONSTRAINT CK_QuizBankItem_Format CHECK (Format IN ('Choice','Text')),
    Explanation nvarchar(max)  NULL,
    Status      varchar(20)    NOT NULL CONSTRAINT CK_QuizBankItem_Status CHECK (Status IN ('Draft','Reviewed','Archived')),
    CreatedUtc  datetime2(3)   NOT NULL
);

CREATE TABLE app.QuizBankChoice (
    Id          int IDENTITY  NOT NULL CONSTRAINT PK_QuizBankChoice PRIMARY KEY,
    ItemId      int           NOT NULL CONSTRAINT FK_QuizBankChoice_Item REFERENCES app.QuizBankItem(Id),
    SortOrder   tinyint       NOT NULL,
    Text        nvarchar(300) NOT NULL,
    IsCorrect   bit           NOT NULL,
    CONSTRAINT UQ_QuizBankChoice UNIQUE (ItemId, SortOrder)
);

CREATE TABLE app.QuizBankAcceptedAnswer (
    ItemId          int           NOT NULL CONSTRAINT FK_QuizBankAcceptedAnswer_Item REFERENCES app.QuizBankItem(Id),
    NormalizedText  nvarchar(200) NOT NULL,
    CONSTRAINT PK_QuizBankAcceptedAnswer PRIMARY KEY (ItemId, NormalizedText)
);

-- =====================================================================
-- 2. 영역 범위 — 시즌
-- =====================================================================






CREATE TABLE app.TenantSeason (                                                 -- 0028
    TenantId            int          NOT NULL CONSTRAINT FK_TenantSeason_Tenant REFERENCES app.Tenant(Id),
    [Year]              smallint     NOT NULL,
    StartDate           date         NOT NULL,
    ChangedUtc          datetime2(3) NOT NULL,
    ChangedByUserName  nvarchar(256)          NOT NULL,
    CONSTRAINT PK_TenantSeason PRIMARY KEY (TenantId, [Year]),
    CONSTRAINT CK_TenantSeason_StartInYear CHECK (YEAR(StartDate) = [Year])
);

-- =====================================================================
-- 3. 플랫폼 + 영역 — 콘텐츠 (TenantId NULL = 공용, 값 = 영역 덮어쓰기)
--    개정 = 새 행. 같은 소유 · 날짜의 현재 개정은 1건(R-121, R-124, X-18)
-- =====================================================================

CREATE TABLE app.DailyWord (
    Id                  int IDENTITY   NOT NULL CONSTRAINT PK_DailyWord PRIMARY KEY,
    TenantId            int            NULL CONSTRAINT FK_DailyWord_Tenant REFERENCES app.Tenant(Id),
    ExposeDate          date           NOT NULL,
    Revision            int            NOT NULL,
    IsCurrent           bit            NOT NULL,
    Status              varchar(20)    NOT NULL CONSTRAINT CK_DailyWord_Status CHECK (Status IN ('Draft','Published','Archived')),
    ReplacesId          int            NULL CONSTRAINT FK_DailyWord_Replaces REFERENCES app.DailyWord(Id),
    BookId              tinyint        NOT NULL CONSTRAINT FK_DailyWord_Book REFERENCES app.BibleBook(Id),
    StartChapter        smallint       NOT NULL,
    StartVerse          smallint       NOT NULL,
    EndChapter          smallint       NOT NULL,
    EndVerse            smallint       NOT NULL,
    Title               nvarchar(100)  NOT NULL,      -- 오늘의 말씀 제목(예: 믿음의 종의 기도) — 1차 구현에서 더함
    Summary             nvarchar(max)  NOT NULL,
    Commentary          nvarchar(max)  NULL,
    CreatedUtc          datetime2(3)   NOT NULL,
    CreatedByUserName  nvarchar(256)            NOT NULL,
    CONSTRAINT CK_DailyWord_Range CHECK (EndChapter > StartChapter OR (EndChapter = StartChapter AND EndVerse >= StartVerse))
);
CREATE UNIQUE INDEX UX_DailyWord_Date ON app.DailyWord (TenantId, ExposeDate) WHERE IsCurrent = 1;

CREATE TABLE app.DailyWordQuestion (
    Id           int IDENTITY  NOT NULL CONSTRAINT PK_DailyWordQuestion PRIMARY KEY,
    TenantId     int           NULL,
    DailyWordId  int           NOT NULL CONSTRAINT FK_DailyWordQuestion_Word REFERENCES app.DailyWord(Id),
    SortOrder    tinyint       NOT NULL,
    Text         nvarchar(500) NOT NULL,
    CONSTRAINT UQ_DailyWordQuestion UNIQUE (DailyWordId, SortOrder)
);

CREATE TABLE app.DailyWordVideo (                                               -- R-117, X-19
    Id              int IDENTITY NOT NULL CONSTRAINT PK_DailyWordVideo PRIMARY KEY,
    TenantId        int          NULL,
    DailyWordId     int          NOT NULL CONSTRAINT FK_DailyWordVideo_Word REFERENCES app.DailyWord(Id),
    SortOrder       tinyint      NOT NULL CONSTRAINT CK_DailyWordVideo_Order CHECK (SortOrder BETWEEN 1 AND 5),
    YoutubeVideoId  varchar(20)  NOT NULL,
    StartSec        int          NOT NULL CONSTRAINT CK_DailyWordVideo_Start CHECK (StartSec >= 0),
    EndSec          int          NOT NULL,
    CONSTRAINT UQ_DailyWordVideo UNIQUE (DailyWordId, SortOrder),
    CONSTRAINT CK_DailyWordVideo_Range CHECK (EndSec > StartSec)
);

CREATE TABLE app.DailyPrayer (
    Id                  int IDENTITY  NOT NULL CONSTRAINT PK_DailyPrayer PRIMARY KEY,
    TenantId            int           NULL CONSTRAINT FK_DailyPrayer_Tenant REFERENCES app.Tenant(Id),
    DayNo               tinyint       NULL,          -- 공용만: 매달 몇 일(1 ~ 31, 0053)
    ExposeDate          date          NULL,          -- 부서만: 그날
    Revision            int           NOT NULL,
    IsCurrent           bit           NOT NULL,
    Status              varchar(20)   NOT NULL CONSTRAINT CK_DailyPrayer_Status CHECK (Status IN ('Draft','Published','Archived')),
    ReplacesId          int           NULL CONSTRAINT FK_DailyPrayer_Replaces REFERENCES app.DailyPrayer(Id),
    TraitName           nvarchar(30)  NOT NULL,      -- 성품 이름(예: 신뢰) — 0047
    TraitNameEn         nvarchar(40)  NULL,          -- 예: Trust
    Paragraphs          nvarchar(max) NOT NULL CONSTRAINT CK_DailyPrayer_Paragraphs CHECK (ISJSON(Paragraphs) = 1),  -- 문단 목록, {이름} 자리 표시
    BookId              tinyint       NOT NULL CONSTRAINT FK_DailyPrayer_Book REFERENCES app.BibleBook(Id),
    StartChapter        smallint      NOT NULL,
    StartVerse          smallint      NOT NULL,
    EndChapter          smallint      NOT NULL,
    EndVerse            smallint      NOT NULL,
    CreatedUtc          datetime2(3)  NOT NULL,
    CreatedByUserName  nvarchar(256)           NOT NULL,
    CONSTRAINT CK_DailyPrayer_Range CHECK (EndChapter > StartChapter OR (EndChapter = StartChapter AND EndVerse >= StartVerse)),
    CONSTRAINT CK_DailyPrayer_Slot CHECK (
        (TenantId IS NULL AND DayNo BETWEEN 1 AND 31 AND ExposeDate IS NULL)
        OR (TenantId IS NOT NULL AND DayNo IS NULL AND ExposeDate IS NOT NULL))
);
CREATE UNIQUE INDEX UX_DailyPrayer_Day  ON app.DailyPrayer (DayNo) WHERE IsCurrent = 1 AND TenantId IS NULL;
CREATE UNIQUE INDEX UX_DailyPrayer_Date ON app.DailyPrayer (TenantId, ExposeDate) WHERE IsCurrent = 1 AND ExposeDate IS NOT NULL;

CREATE TABLE app.DailyQuiz (
    Id                  int IDENTITY   NOT NULL CONSTRAINT PK_DailyQuiz PRIMARY KEY,
    TenantId            int            NULL CONSTRAINT FK_DailyQuiz_Tenant REFERENCES app.Tenant(Id),
    ExposeDate          date           NOT NULL,
    Revision            int            NOT NULL,
    IsCurrent           bit            NOT NULL,
    Status              varchar(20)    NOT NULL CONSTRAINT CK_DailyQuiz_Status CHECK (Status IN ('Draft','Published','Archived')),
    ReplacesId          int            NULL CONSTRAINT FK_DailyQuiz_Replaces REFERENCES app.DailyQuiz(Id),
    Question            nvarchar(1000) NOT NULL,
    Background          nvarchar(max)  NULL,
    Format              varchar(10)    NOT NULL CONSTRAINT CK_DailyQuiz_Format CHECK (Format IN ('Choice','Text')),
    CorrectChoiceId     int            NULL,
    Explanation         nvarchar(max)  NULL,
    CreatedUtc          datetime2(3)   NOT NULL,
    CreatedByUserName  nvarchar(256)            NOT NULL,
    -- 공개된 객관식은 정답 보기가 있어야 한다. 주관식 허용 답 1개 이상은 앱 검사.
    CONSTRAINT CK_DailyQuiz_Correct CHECK (Status <> 'Published' OR Format = 'Text' OR CorrectChoiceId IS NOT NULL)
);
CREATE UNIQUE INDEX UX_DailyQuiz_Date ON app.DailyQuiz (TenantId, ExposeDate) WHERE IsCurrent = 1;

CREATE TABLE app.DailyQuizChoice (
    Id           int IDENTITY  NOT NULL CONSTRAINT PK_DailyQuizChoice PRIMARY KEY,
    TenantId     int           NULL,
    DailyQuizId  int           NOT NULL CONSTRAINT FK_DailyQuizChoice_Quiz REFERENCES app.DailyQuiz(Id),
    SortOrder    tinyint       NOT NULL,
    Text         nvarchar(300) NOT NULL,
    CONSTRAINT UQ_DailyQuizChoice UNIQUE (DailyQuizId, SortOrder),
    CONSTRAINT UQ_DailyQuizChoice_Id_Quiz UNIQUE (Id, DailyQuizId)
);
-- 정답 보기는 반드시 그 퀴즈의 보기
ALTER TABLE app.DailyQuiz ADD CONSTRAINT FK_DailyQuiz_CorrectChoice
    FOREIGN KEY (CorrectChoiceId, Id) REFERENCES app.DailyQuizChoice (Id, DailyQuizId);

CREATE TABLE app.DailyQuizAcceptedAnswer (                                      -- X-08
    TenantId        int           NULL,
    DailyQuizId     int           NOT NULL CONSTRAINT FK_DailyQuizAcceptedAnswer_Quiz REFERENCES app.DailyQuiz(Id),
    NormalizedText  nvarchar(200) NOT NULL,
    CONSTRAINT PK_DailyQuizAcceptedAnswer PRIMARY KEY (DailyQuizId, NormalizedText)
);

CREATE TABLE app.WeeklyVerse (
    Id                  int IDENTITY  NOT NULL CONSTRAINT PK_WeeklyVerse PRIMARY KEY,
    TenantId            int           NULL CONSTRAINT FK_WeeklyVerse_Tenant REFERENCES app.Tenant(Id),
    WeekNo              tinyint       NULL,          -- 공용만: 그해 몇째 주(1 ~ 55, 0052)
    StartDate           date          NULL,          -- 부서만: 그해 그 주(일요일) — 해가 바뀌면 공용으로 돌아감
    EndDate             date          NULL,
    Revision            int           NOT NULL,
    IsCurrent           bit           NOT NULL,
    Status              varchar(20)   NOT NULL CONSTRAINT CK_WeeklyVerse_Status CHECK (Status IN ('Draft','Published','Archived')),
    ReplacesId          int           NULL CONSTRAINT FK_WeeklyVerse_Replaces REFERENCES app.WeeklyVerse(Id),
    BookId              tinyint       NOT NULL CONSTRAINT FK_WeeklyVerse_Book REFERENCES app.BibleBook(Id),
    StartChapter        smallint      NOT NULL,
    StartVerse          smallint      NOT NULL,
    EndChapter          smallint      NOT NULL,
    EndVerse            smallint      NOT NULL,
    Title               nvarchar(200) NOT NULL,      -- 질문형 제목
    LessonNo            smallint      NULL,          -- 공과 번호(예: 36과)
    LessonSummary       nvarchar(200) NULL,          -- 한 줄 요점
    LessonPoint         nvarchar(max) NULL,          -- 학습 요점
    LessonApply         nvarchar(max) NULL,          -- 적용
    CreatedUtc          datetime2(3)  NOT NULL,
    CreatedByUserName  nvarchar(256)           NOT NULL,
    CONSTRAINT CK_WeeklyVerse_Range CHECK (EndChapter > StartChapter OR (EndChapter = StartChapter AND EndVerse >= StartVerse)),
    CONSTRAINT CK_WeeklyVerse_Slot CHECK (
        (TenantId IS NULL AND WeekNo BETWEEN 1 AND 55 AND StartDate IS NULL AND EndDate IS NULL)
        OR (TenantId IS NOT NULL AND WeekNo IS NULL AND DATEDIFF(DAY, '19000107', StartDate) % 7 = 0 AND EndDate = DATEADD(DAY, 6, StartDate)))
);
CREATE UNIQUE INDEX UX_WeeklyVerse_Week  ON app.WeeklyVerse (WeekNo) WHERE IsCurrent = 1 AND TenantId IS NULL;
CREATE UNIQUE INDEX UX_WeeklyVerse_Start ON app.WeeklyVerse (TenantId, StartDate) WHERE IsCurrent = 1 AND StartDate IS NOT NULL;
GO

-- 같은 소유의 현재 개정끼리 기간이 겹치면 거절(R-121)
CREATE TRIGGER app.trg_WeeklyVerse_NoOverlap ON app.WeeklyVerse
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM inserted i
        JOIN app.WeeklyVerse w
          ON w.Id <> i.Id
         AND w.IsCurrent = 1
         AND i.IsCurrent = 1
         AND (w.TenantId = i.TenantId OR (w.TenantId IS NULL AND i.TenantId IS NULL))
         AND w.StartDate <= i.EndDate
         AND i.StartDate <= w.EndDate
    )
    BEGIN
        THROW 50001, N'암송 기간이 겹칩니다.', 1;
    END
END;
GO

-- 가족팀 합류 신청(R-206 · R-207, ADR 0083): 참여자당 한 줄, 처리되면 지운다

-- 가족팀 초대(R-211 ~ R-213, ADR 0084): 팀 · 사람마다 한 줄, 처리되면 지운다



-- =====================================================================
-- 6. 성경 · 통독 참조와 검색 (공유 표 밖)
-- =====================================================================



-- 0041: 절 넘김 읽기 · 듣기 해설 · 인기 검색어
CREATE TABLE app.BibleChapter (
    BookId      tinyint  NOT NULL CONSTRAINT FK_BibleChapter_Book REFERENCES app.BibleBook(Id),
    Chapter     smallint NOT NULL,
    VerseCount  smallint NOT NULL CONSTRAINT CK_BibleChapter_Verses CHECK (VerseCount >= 1),   -- 기본 번역(개역개정) 기준
    CONSTRAINT PK_BibleChapter PRIMARY KEY (BookId, Chapter)
);



CREATE TABLE app.DailyWordClip (                                                -- 플랫폼(R-127, ADR 0081)
    Id              int IDENTITY  NOT NULL CONSTRAINT PK_DailyWordClip PRIMARY KEY,
    WordDate        date          NOT NULL,
    Seq             tinyint       NOT NULL,
    Chapter         smallint      NOT NULL,
    YoutubeVideoId  varchar(20)   NULL,
    StartSec        int           NULL,
    EndSec          int           NULL,
    UpdatedUtc      datetime2     NOT NULL,
    CONSTRAINT UQ_DailyWordClip_Slot UNIQUE (WordDate, Seq)
);

CREATE TABLE app.ChapterMedia (                                                 -- 플랫폼(R-166)
    Id              int IDENTITY  NOT NULL CONSTRAINT PK_ChapterMedia PRIMARY KEY,
    BookId          tinyint       NOT NULL,
    Chapter         smallint      NOT NULL,
    Kind            varchar(20)   NOT NULL CONSTRAINT CK_ChapterMedia_Kind CHECK (Kind IN ('Listen','Overview')),
    SortOrder       tinyint       NOT NULL,
    YoutubeVideoId  varchar(20)   NOT NULL,
    Title           nvarchar(100) NULL,
    CONSTRAINT FK_ChapterMedia_Chapter FOREIGN KEY (BookId, Chapter) REFERENCES app.BibleChapter (BookId, Chapter),
    CONSTRAINT UQ_ChapterMedia UNIQUE (BookId, Chapter, Kind, SortOrder)
);

CREATE TABLE app.SearchLog (                                                    -- 영역, 계정 열 없음(R-164)
    Id           bigint IDENTITY NOT NULL,
    TenantId     int             NOT NULL CONSTRAINT FK_SearchLog_Tenant REFERENCES app.Tenant(Id),
    Term         nvarchar(50)    NOT NULL,
    SearchedUtc  datetime2(3)    NOT NULL,
    CONSTRAINT PK_SearchLog PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_SearchLog_TenantId_Id UNIQUE CLUSTERED (TenantId, Id)
);
CREATE INDEX IX_SearchLog_Recent ON app.SearchLog (TenantId, SearchedUtc) INCLUDE (Term);
GO

-- 사람 칸을 아이디로 바꾼 표 (0090) -----------------------------------

CREATE TABLE app.AuditLog (
    Id             bigint IDENTITY NOT NULL CONSTRAINT PK_AuditLog PRIMARY KEY,
    OccurredUtc    datetime2(3)    NOT NULL,
    ActorUserName  nvarchar(256)   NOT NULL,
    TenantId       int             NULL,
    Action         varchar(40)     NOT NULL,
    TargetType     varchar(40)     NULL,
    TargetId       bigint          NULL,
    Detail         nvarchar(max)   NULL CONSTRAINT CK_AuditLog_Detail CHECK (Detail IS NULL OR ISJSON(Detail) = 1)
);
CREATE INDEX IX_AuditLog_Tenant ON app.AuditLog (TenantId, OccurredUtc);

CREATE TABLE app.GuardianLink (
    Id                int IDENTITY  NOT NULL,
    TenantId          int           NOT NULL CONSTRAINT FK_GuardianLink_Tenant REFERENCES app.Tenant(Id),
    GuardianUserName  nvarchar(256) NOT NULL,
    StudentUserName   nvarchar(256) NOT NULL,
    CreatedUtc        datetime2(3)  NOT NULL,
    CreatedByUserName nvarchar(256) NOT NULL,
    CONSTRAINT PK_GuardianLink PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_GuardianLink_TenantId_Id UNIQUE CLUSTERED (TenantId, Id),
    CONSTRAINT UQ_GuardianLink_Pair UNIQUE (TenantId, GuardianUserName, StudentUserName)
);
CREATE INDEX IX_GuardianLink_Student ON app.GuardianLink (TenantId, StudentUserName);

CREATE TABLE app.PromotionRequest (                                             -- 0026, 0030
    Id                int IDENTITY  NOT NULL CONSTRAINT PK_PromotionRequest PRIMARY KEY,
    UserName          nvarchar(256) NOT NULL,
    SourceTenantId    int           NOT NULL CONSTRAINT FK_PromotionRequest_Source REFERENCES app.Tenant(Id),
    TargetTenantId    int           NOT NULL CONSTRAINT FK_PromotionRequest_Target REFERENCES app.Tenant(Id),
    Status            varchar(20)   NOT NULL CONSTRAINT CK_PromotionRequest_Status CHECK (Status IN ('Pending','Approved','Rejected')),
    RequestedUtc      datetime2(3)  NOT NULL,
    RequestedByUserName nvarchar(256) NOT NULL,
    DecidedUtc        datetime2(3)  NULL,
    DecidedByUserName nvarchar(256) NULL,
    CONSTRAINT CK_PromotionRequest_DifferentTenant CHECK (SourceTenantId <> TargetTenantId)
);
CREATE UNIQUE INDEX UX_PromotionRequest_Pending ON app.PromotionRequest (UserName, SourceTenantId) WHERE Status = 'Pending';
CREATE INDEX IX_PromotionRequest_Target ON app.PromotionRequest (TargetTenantId, Status);

CREATE TABLE app.Outbox (
    Id           bigint IDENTITY NOT NULL,
    TenantId     int             NOT NULL,
    UserName     nvarchar(256)   NULL,
    EventType    varchar(50)     NOT NULL,
    Payload      nvarchar(max)   NOT NULL CONSTRAINT CK_Outbox_Payload CHECK (ISJSON(Payload) = 1),
    OccurredUtc  datetime2(3)    NOT NULL,
    SentUtc      datetime2(3)    NULL,
    CONSTRAINT PK_Outbox PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_Outbox_TenantId_Id UNIQUE CLUSTERED (TenantId, Id)
);
CREATE INDEX IX_Outbox_Unsent ON app.Outbox (OccurredUtc) WHERE SentUtc IS NULL;
GO

-- =====================================================================
-- 9. 유년부와 공유하는 표 (ADR 0090, 부록 08)
--    칸 이름 · 형식 · 빈 값 · 검사는 08 과 같다. B · C · D 에만 TenantId 를 더한다.
--    08 의 현재 시각 기본값(getdate · sysdatetime)은 두지 않는다 — 앱이 값을 넣는다.
-- =====================================================================

-- A. 회원 · 로그인 (TenantId 없음) -------------------------------------

CREATE TABLE [Identity].[User] (
    Id                   nvarchar(450)  NOT NULL CONSTRAINT PK_User PRIMARY KEY,
    UserName             nvarchar(256)  NULL,
    NormalizedUserName   nvarchar(256)  NULL,
    Email                nvarchar(256)  NULL,
    NormalizedEmail      nvarchar(256)  NULL,
    EmailConfirmed       bit            NOT NULL,
    PasswordHash         nvarchar(max)  NULL,
    SecurityStamp        nvarchar(max)  NULL,
    ConcurrencyStamp     nvarchar(max)  NULL,
    PhoneNumber          nvarchar(max)  NULL,
    PhoneNumberConfirmed bit            NOT NULL,
    TwoFactorEnabled     bit            NOT NULL,
    LockoutEnd           datetimeoffset NULL,
    LockoutEnabled       bit            NOT NULL,
    AccessFailedCount    int            NOT NULL,
    FirstName            nvarchar(max)  NULL,
    LastName             nvarchar(max)  NULL,
    ProfilePicture       varbinary(max) NULL,
    UsernameChangeLimit  int            NOT NULL CONSTRAINT DF_User_UsernameChangeLimit DEFAULT ((0)),
    insertDT             datetime       NOT NULL
);
CREATE INDEX EmailIndex ON [Identity].[User] (NormalizedEmail);
CREATE INDEX IX_IdentityUser_UserName ON [Identity].[User] (UserName);
CREATE UNIQUE INDEX UserNameIndex ON [Identity].[User] (NormalizedUserName) WHERE ([NormalizedUserName] IS NOT NULL);

CREATE TABLE [Identity].[Role] (
    Id               nvarchar(450) NOT NULL CONSTRAINT PK_Role PRIMARY KEY,
    Name             nvarchar(256) NULL,
    NormalizedName   nvarchar(256) NULL,
    ConcurrencyStamp nvarchar(max) NULL
);
CREATE UNIQUE INDEX RoleNameIndex ON [Identity].[Role] (NormalizedName) WHERE ([NormalizedName] IS NOT NULL);

CREATE TABLE [Identity].[UserRoles] (
    UserId nvarchar(450) NOT NULL CONSTRAINT FK_UserRoles_User_UserId REFERENCES [Identity].[User](Id),
    RoleId nvarchar(450) NOT NULL CONSTRAINT FK_UserRoles_Role_RoleId REFERENCES [Identity].[Role](Id),
    CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleId)
);
CREATE INDEX IX_UserRoles_RoleId ON [Identity].[UserRoles] (RoleId);

CREATE TABLE [Identity].[UserClaims] (
    Id         int IDENTITY  NOT NULL CONSTRAINT PK_UserClaims PRIMARY KEY,
    UserId     nvarchar(450) NOT NULL CONSTRAINT FK_UserClaims_User_UserId REFERENCES [Identity].[User](Id),
    ClaimType  nvarchar(max) NULL,
    ClaimValue nvarchar(max) NULL
);
CREATE INDEX IX_UserClaims_UserId ON [Identity].[UserClaims] (UserId);

CREATE TABLE [Identity].[RoleClaims] (
    Id         int IDENTITY  NOT NULL CONSTRAINT PK_RoleClaims PRIMARY KEY,
    RoleId     nvarchar(450) NOT NULL CONSTRAINT FK_RoleClaims_Role_RoleId REFERENCES [Identity].[Role](Id),
    ClaimType  nvarchar(max) NULL,
    ClaimValue nvarchar(max) NULL
);
CREATE INDEX IX_RoleClaims_RoleId ON [Identity].[RoleClaims] (RoleId);

CREATE TABLE [Identity].[UserLogins] (
    LoginProvider       nvarchar(450) NOT NULL,
    ProviderKey         nvarchar(450) NOT NULL,
    ProviderDisplayName nvarchar(max) NULL,
    UserId              nvarchar(450) NOT NULL CONSTRAINT FK_UserLogins_User_UserId REFERENCES [Identity].[User](Id),
    CONSTRAINT PK_UserLogins PRIMARY KEY (LoginProvider, ProviderKey)
);
CREATE INDEX IX_UserLogins_UserId ON [Identity].[UserLogins] (UserId);

CREATE TABLE [Identity].[UserTokens] (
    UserId        nvarchar(450) NOT NULL CONSTRAINT FK_UserTokens_User_UserId REFERENCES [Identity].[User](Id),
    LoginProvider nvarchar(450) NOT NULL,
    Name          nvarchar(450) NOT NULL,
    Value         nvarchar(max) NULL,
    CONSTRAINT PK_UserTokens PRIMARY KEY (UserId, LoginProvider, Name)
);

-- Identity 칸을 바꿀 수 없어 따로 둔다. 전송 대상이 아니다 (0090)
CREATE TABLE app.UserSetting (
    UserName           nvarchar(256) NOT NULL CONSTRAINT PK_UserSetting PRIMARY KEY,
    MustChangePassword bit           NOT NULL,   -- 임시 비밀번호 초기화 후 참(0035)
    LastTenantId       int           NULL CONSTRAINT FK_UserSetting_LastTenant REFERENCES app.Tenant(Id),
    ReadingTheme       varchar(20)   NULL        -- 통독 색 테마(0073)
);

-- B. 가입 신청 · 역할 ---------------------------------------------------

CREATE TABLE dbo.SignupApplication (
    TenantId     int            NOT NULL CONSTRAINT FK_SignupApplication_Tenant REFERENCES app.Tenant(Id),
    Id           int IDENTITY   NOT NULL,
    UserId       nvarchar(450)  NOT NULL,
    UserName     nvarchar(256)  NOT NULL,
    FullName     nvarchar(100)  NOT NULL,
    RoleKind     char(1)        NOT NULL,
    PersonId     int            NOT NULL,
    ClassYear    int            NOT NULL,
    ClassBan     int            NOT NULL,
    BMonth       int            NOT NULL,
    BDay         int            NOT NULL,
    Sex          nvarchar(1)    NULL,
    School       nvarchar(200)  NULL,
    Phone        nvarchar(50)   NULL,
    Kids         nvarchar(1000) NULL,
    ReqType      nvarchar(10)   NOT NULL,
    PrevRole     nvarchar(100)  NULL,
    Status       nvarchar(10)   NOT NULL,
    RejectReason nvarchar(300)  NULL,
    InsertDT     datetime       NOT NULL,
    DecidedDT    datetime       NULL,
    DecidedBy    nvarchar(256)  NULL,
    CONSTRAINT PK_SignupApplication PRIMARY KEY (TenantId, Id)
);

-- TenantId 가 곧 소속. 사람당 한 줄은 앱이 지킨다(0090: 유년부 241줄 / 회원 190명이라 유일 색인 불가)
CREATE TABLE dbo.DeptRoleUserAssoc (
    TenantId int           NOT NULL CONSTRAINT FK_DeptRoleUserAssoc_Tenant REFERENCES app.Tenant(Id),
    Id       int IDENTITY  NOT NULL,
    RoleCode nvarchar(50)  NULL,
    UserId   nvarchar(450) NULL,
    InsertDT datetime      NOT NULL,
    updateDT datetime      NOT NULL,
    CONSTRAINT PK_DeptRoleUserAssoc PRIMARY KEY (TenantId, Id)
);
CREATE INDEX IX_DeptRoleUserAssoc_UserId ON dbo.DeptRoleUserAssoc (UserId);

-- 역할 코드(MajorCD T001) 18개. 부서를 만들 때 함께 넣는다
CREATE TABLE dbo.CodeData (
    TenantId     int           NOT NULL CONSTRAINT FK_CodeData_Tenant REFERENCES app.Tenant(Id),
    Id           int IDENTITY  NOT NULL,
    MajorCD      nvarchar(50)  NULL,
    MinorCD      nvarchar(50)  NULL,
    MinorNM      nvarchar(250) NULL,
    InsertDT     datetime      NOT NULL,
    updateDT     datetime      NOT NULL,
    displayOrder int           NULL,
    CONSTRAINT PK_CodeData PRIMARY KEY (TenantId, Id)
);

-- C. 가족팀 · 친구팀 · 시상 ----------------------------------------------

CREATE TABLE dbo.DM2_FamilyTeam (
    TenantId        int           NOT NULL CONSTRAINT FK_DM2_FamilyTeam_Tenant REFERENCES app.Tenant(Id),
    FamilyTeamId    int IDENTITY  NOT NULL,
    TeamName        nvarchar(50)  NOT NULL,
    IsActive        bit           NOT NULL CONSTRAINT DF_DM2_FamilyTeam_IsActive DEFAULT ((1)),
    Memo            nvarchar(200) NULL,
    InsertDT        datetime      NOT NULL,
    UpdateDT        datetime      NOT NULL,
    AwardKey        nvarchar(100) NULL,
    CreatorUserName nvarchar(200) NULL,
    TeamType        varchar(10)   NOT NULL CONSTRAINT DF_DM2_FamilyTeam_TeamType DEFAULT ('FAMILY'),
    CONSTRAINT PK_DM2_FamilyTeam PRIMARY KEY (TenantId, FamilyTeamId),
    CONSTRAINT CK_DM2_FamilyTeam_TeamType CHECK ([TeamType]='FRIEND' OR [TeamType]='FAMILY')
);
CREATE INDEX IX_DM2_FamilyTeam_IsActive ON dbo.DM2_FamilyTeam (TenantId, IsActive);
CREATE UNIQUE INDEX UX_DM2_FamilyTeam_AwardKey ON dbo.DM2_FamilyTeam (TenantId, AwardKey) WHERE ([AwardKey] IS NOT NULL);

CREATE TABLE dbo.DM2_FamilyMember (
    TenantId      int           NOT NULL,
    Id            int IDENTITY  NOT NULL,
    FamilyTeamId  int           NOT NULL,
    UserName      nvarchar(100) NOT NULL,
    RoleCode      tinyint       NOT NULL,
    SortNo        int           NOT NULL CONSTRAINT DF_DM2_FamilyMember_SortNo DEFAULT ((0)),
    IsActive      bit           NOT NULL CONSTRAINT DF_DM2_FamilyMember_IsActive DEFAULT ((1)),
    InsertDT      datetime      NOT NULL,
    UpdateDT      datetime      NOT NULL,
    RelationCode  tinyint       NOT NULL CONSTRAINT DF_DM2_FamilyMember_RelationCode DEFAULT ((9)),
    RelationLabel nvarchar(20)  NULL,
    CONSTRAINT PK_DM2_FamilyMember PRIMARY KEY (TenantId, Id),
    CONSTRAINT FK_DM2_FamilyMember_Team FOREIGN KEY (TenantId, FamilyTeamId) REFERENCES dbo.DM2_FamilyTeam (TenantId, FamilyTeamId)
);
CREATE INDEX IX_DM2_FamilyMember_FamilyTeamId_IsActive ON dbo.DM2_FamilyMember (TenantId, FamilyTeamId, IsActive);
CREATE INDEX IX_DM2_FamilyMember_Team ON dbo.DM2_FamilyMember (TenantId, FamilyTeamId, RoleCode, IsActive);
CREATE INDEX IX_DM2_FamilyMember_UserName_IsActive ON dbo.DM2_FamilyMember (TenantId, UserName, IsActive);
CREATE UNIQUE INDEX UX_DM2_FamilyMember_Team_Self ON dbo.DM2_FamilyMember (TenantId, FamilyTeamId, RelationCode) WHERE ([IsActive]=(1) AND [RoleCode]=(0) AND [RelationCode]=(0));
CREATE UNIQUE INDEX UX_DM2_FamilyMember_Team_User ON dbo.DM2_FamilyMember (TenantId, FamilyTeamId, UserName);
CREATE UNIQUE INDEX UX_DM2_FamilyMember_UserName ON dbo.DM2_FamilyMember (UserName);   -- 07 의 예외: 전역 유일

CREATE TABLE dbo.DM2_FamilyRequest (
    TenantId       int           NOT NULL,
    Id             int IDENTITY  NOT NULL,
    FamilyTeamId   int           NOT NULL,
    Kind           char(1)       NOT NULL,
    TargetUserName nvarchar(200) NOT NULL,
    FromUserName   nvarchar(200) NOT NULL,
    RelationKey    varchar(10)   NOT NULL,
    CreatedAt      datetime      NOT NULL,
    CONSTRAINT PK_DM2_FamilyRequest PRIMARY KEY (TenantId, Id),
    CONSTRAINT CK_DM2_FamilyRequest_Kind CHECK ([Kind]='I' OR [Kind]='R'),
    CONSTRAINT FK_DM2_FamilyRequest_Team FOREIGN KEY (TenantId, FamilyTeamId) REFERENCES dbo.DM2_FamilyTeam (TenantId, FamilyTeamId)
);
CREATE INDEX IX_DM2_FamilyRequest_From ON dbo.DM2_FamilyRequest (TenantId, FromUserName, Kind);
CREATE INDEX IX_DM2_FamilyRequest_Target ON dbo.DM2_FamilyRequest (TenantId, TargetUserName, Kind);
CREATE UNIQUE INDEX UX_DM2_FamilyRequest_Team_Kind_Target ON dbo.DM2_FamilyRequest (TenantId, FamilyTeamId, Kind, TargetUserName);

CREATE TABLE dbo.DM2_FamilyNotice (
    TenantId     int           NOT NULL CONSTRAINT FK_DM2_FamilyNotice_Tenant REFERENCES app.Tenant(Id),
    Id           int IDENTITY  NOT NULL,
    UserName     nvarchar(200) NOT NULL,
    FamilyTeamId int           NOT NULL,
    TeamName     nvarchar(100) NOT NULL,
    Kind         varchar(10)   NOT NULL CONSTRAINT DF_DM2_FamilyNotice_Kind DEFAULT ('JOINED'),
    CreatedAt    datetime      NOT NULL,
    CONSTRAINT PK_DM2_FamilyNotice PRIMARY KEY (TenantId, Id)
);
CREATE INDEX IX_DM2_FamilyNotice_User ON dbo.DM2_FamilyNotice (TenantId, UserName);

CREATE TABLE dbo.DM2_FamilyMilestoneConfig (
    TenantId       int           NOT NULL CONSTRAINT FK_DM2_FamilyMilestoneConfig_Tenant REFERENCES app.Tenant(Id),
    Id             int IDENTITY  NOT NULL,
    [Year]         int           NOT NULL,
    PointThreshold int           NOT NULL,
    MilestoneType  nvarchar(20)  NOT NULL,
    DisplayTitle   nvarchar(200) NOT NULL,
    DisplayEmoji   nvarchar(10)  NULL,
    IsActive       bit           NOT NULL CONSTRAINT DF_DM2FMC_IsActive DEFAULT ((1)),
    SortOrder      int           NOT NULL CONSTRAINT DF_DM2FMC_SortOrder DEFAULT ((0)),
    CreatedAt      datetime2(7)  NOT NULL,
    CONSTRAINT PK_DM2_FamilyMilestoneConfig PRIMARY KEY (TenantId, Id),
    CONSTRAINT CK_DM2FMC_Type CHECK ([MilestoneType]='APPLAUSE' OR [MilestoneType]='AWARD')
);
CREATE INDEX IX_DM2_FamilyMilestoneConfig_Year_IsActive ON dbo.DM2_FamilyMilestoneConfig (TenantId, [Year], IsActive);
CREATE INDEX IX_DM2FMC_Year_Active ON dbo.DM2_FamilyMilestoneConfig (TenantId, [Year], IsActive);
CREATE UNIQUE INDEX UQ_DM2FMC_YearPoint ON dbo.DM2_FamilyMilestoneConfig (TenantId, [Year], PointThreshold);

CREATE TABLE dbo.DM2_FamilyMilestoneHidden (
    TenantId          int          NOT NULL,
    Id                int IDENTITY NOT NULL,
    MilestoneConfigId int          NOT NULL,
    FamilyTeamId      int          NOT NULL,
    HiddenAt          datetime2(7) NOT NULL,
    CONSTRAINT PK_DM2_FamilyMilestoneHidden PRIMARY KEY (TenantId, Id),
    CONSTRAINT FK_DM2FMH_Config FOREIGN KEY (TenantId, MilestoneConfigId) REFERENCES dbo.DM2_FamilyMilestoneConfig (TenantId, Id)
);
CREATE INDEX IX_DM2_FamilyMilestoneHidden_ConfigId_TeamId ON dbo.DM2_FamilyMilestoneHidden (TenantId, MilestoneConfigId, FamilyTeamId);
CREATE INDEX IX_DM2FMH_ConfigId ON dbo.DM2_FamilyMilestoneHidden (TenantId, MilestoneConfigId);
CREATE UNIQUE INDEX UQ_DM2FMH_ConfigTeam ON dbo.DM2_FamilyMilestoneHidden (TenantId, MilestoneConfigId, FamilyTeamId);

CREATE TABLE dbo.DM2_TeamAward (
    TenantId  int           NOT NULL CONSTRAINT FK_DM2_TeamAward_Tenant REFERENCES app.Tenant(Id),
    AwardId   int IDENTITY  NOT NULL,
    Kind      nvarchar(10)  NOT NULL,
    AwardKey  nvarchar(200) NOT NULL,
    Threshold int           NOT NULL,
    AwardedAt datetime      NOT NULL,
    CONSTRAINT PK_DM2_TeamAward PRIMARY KEY (TenantId, AwardId),
    CONSTRAINT CK_DM2_TeamAward_Kind CHECK ([Kind]=N'FRIEND' OR [Kind]=N'FAMILY')
);
CREATE INDEX IX_DM2_TeamAward_Key ON dbo.DM2_TeamAward (TenantId, AwardKey);
CREATE UNIQUE INDEX UQ_DM2_TeamAward ON dbo.DM2_TeamAward (TenantId, Kind, AwardKey, Threshold);

-- D. 미션 기록 ----------------------------------------------------------

CREATE TABLE dbo.DM2_MissionActivity (
    TenantId        int             NOT NULL CONSTRAINT FK_DM2_MissionActivity_Tenant REFERENCES app.Tenant(Id),
    ActivityId      bigint IDENTITY NOT NULL,
    UserName        nvarchar(256)   NOT NULL,
    MissionDate     date            NOT NULL,
    MissionItem     varchar(20)     NOT NULL,
    Points          int             NOT NULL,
    ReciteWeekStart date            NULL,
    PerformedAt     datetime2(3)    NOT NULL,
    CONSTRAINT PK_DM2_MissionActivity PRIMARY KEY (TenantId, ActivityId),
    CONSTRAINT CK_DM2_MissionActivity_Item CHECK ([MissionItem]='Recite' OR [MissionItem]='Quiz.Correct' OR [MissionItem]='Prayer.Others' OR [MissionItem]='Prayer.Me' OR [MissionItem]='Word.Parent' OR [MissionItem]='Word.Me'),
    CONSTRAINT CK_DM2_MissionActivity_Points CHECK ([Points]>=(0)),
    CONSTRAINT CK_DM2_MissionActivity_Week CHECK ([MissionItem]='Recite' AND [ReciteWeekStart] IS NOT NULL OR [MissionItem]<>'Recite' AND [ReciteWeekStart] IS NULL)
);
CREATE INDEX IX_DM2_MissionActivity_Date ON dbo.DM2_MissionActivity (TenantId, MissionDate);
CREATE UNIQUE INDEX UX_DM2_MissionActivity_Daily ON dbo.DM2_MissionActivity (TenantId, UserName, MissionDate, MissionItem) WHERE ([MissionItem]<>'Recite');
CREATE UNIQUE INDEX UX_DM2_MissionActivity_Recite ON dbo.DM2_MissionActivity (TenantId, UserName, ReciteWeekStart) WHERE ([MissionItem]='Recite');

CREATE TABLE dbo.DM2_QuizAttempt (
    TenantId          int           NOT NULL CONSTRAINT FK_DM2_QuizAttempt_Tenant REFERENCES app.Tenant(Id),
    UserName          nvarchar(256) NOT NULL,
    MissionDate       date          NOT NULL,
    QuestionId        int           NOT NULL,
    AttemptNo         tinyint       NOT NULL,
    AnswerText        nvarchar(400) NULL,
    CorrectAnswerText nvarchar(400) NULL,
    IsCorrect         bit           NOT NULL,
    AttemptedAt       datetime2(3)  NOT NULL,
    CONSTRAINT PK_DM2_QuizAttempt PRIMARY KEY (TenantId, UserName, MissionDate, QuestionId, AttemptNo),
    CONSTRAINT CK_DM2_QuizAttempt_No CHECK ([AttemptNo]>=(1))
);

-- E. 통독 기록 (TenantId 없음) -------------------------------------------

CREATE TABLE dbo.DM2_ReadingRound (
    UserName    nvarchar(256) NOT NULL,
    RoundNo     int           NOT NULL,
    StartedAt   datetime2(3)  NOT NULL,
    CompletedAt datetime2(3)  NULL,
    ClosedAt    datetime2(3)  NULL,
    CONSTRAINT PK_DM2_ReadingRound PRIMARY KEY (UserName, RoundNo),
    CONSTRAINT CK_DM2_ReadingRound_End CHECK ([CompletedAt] IS NULL OR [ClosedAt] IS NULL),
    CONSTRAINT CK_DM2_ReadingRound_No CHECK ([RoundNo]>=(1))
);
CREATE UNIQUE INDEX UX_DM2_ReadingRound_Open ON dbo.DM2_ReadingRound (UserName) WHERE ([CompletedAt] IS NULL AND [ClosedAt] IS NULL);

CREATE TABLE dbo.DM2_ReadingVerse (
    UserName nvarchar(256) NOT NULL,
    RoundNo  int           NOT NULL,
    BookId   tinyint       NOT NULL,
    Chapter  smallint      NOT NULL,
    Verse    smallint      NOT NULL,
    ReadAt   datetime2(3)  NOT NULL,
    CONSTRAINT PK_DM2_ReadingVerse PRIMARY KEY (UserName, RoundNo, BookId, Chapter, Verse),
    CONSTRAINT FK_DM2_ReadingVerse_Round FOREIGN KEY (UserName, RoundNo) REFERENCES dbo.DM2_ReadingRound (UserName, RoundNo)
);
CREATE INDEX IX_DM2_ReadingVerse_ReadAt ON dbo.DM2_ReadingVerse (ReadAt);

CREATE TABLE dbo.DM2_ReadingBookmark (
    UserName  nvarchar(256) NOT NULL,
    BookId    tinyint       NOT NULL,
    Chapter   smallint      NOT NULL,
    Verse     smallint      NOT NULL,
    CreatedAt datetime2(3)  NOT NULL,
    CONSTRAINT PK_DM2_ReadingBookmark PRIMARY KEY (UserName, BookId, Chapter, Verse)
);

CREATE TABLE dbo.DM2_ReadingChapterRead (                                       -- 07 의 3-1
    UserName nvarchar(256) NOT NULL,
    RoundNo  int           NOT NULL,
    BookId   tinyint       NOT NULL,
    Chapter  smallint      NOT NULL,
    ReadAt   datetime2(3)  NOT NULL,
    CONSTRAINT PK_DM2_ReadingChapterRead PRIMARY KEY (UserName, RoundNo, BookId, Chapter),
    CONSTRAINT FK_DM2_ReadingChapterRead_Round FOREIGN KEY (UserName, RoundNo) REFERENCES dbo.DM2_ReadingRound (UserName, RoundNo)
);
CREATE INDEX IX_DM2_ReadingChapterRead_ReadAt ON dbo.DM2_ReadingChapterRead (UserName, ReadAt);

CREATE TABLE dbo.DM2_ReadingChapterProgress (                                   -- 07 의 3-1
    UserName        nvarchar(256) NOT NULL,
    RoundNo         int           NOT NULL,
    BookId          tinyint       NOT NULL,
    Chapter         smallint      NOT NULL,
    LastViewedVerse smallint      NOT NULL,
    UpdatedAt       datetime2(3)  NOT NULL,
    CONSTRAINT PK_DM2_ReadingChapterProgress PRIMARY KEY (UserName, RoundNo, BookId, Chapter),
    CONSTRAINT CK_DM2_ReadingChapterProgress_Verse CHECK ([LastViewedVerse]>=(1)),
    CONSTRAINT FK_DM2_ReadingChapterProgress_Round FOREIGN KEY (UserName, RoundNo) REFERENCES dbo.DM2_ReadingRound (UserName, RoundNo)
);
CREATE INDEX IX_DM2_ReadingChapterProgress_Recent ON dbo.DM2_ReadingChapterProgress (UserName, UpdatedAt DESC);
GO

-- =====================================================================
-- 10. RLS — 조건 함수 (세션 값: TenantId, UserName, UserId, PlatformScope — 읽기 전용으로 설정)
-- =====================================================================

CREATE FUNCTION sec.fn_Tenant (@TenantId int)
RETURNS TABLE WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS Allowed
    WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
       OR CAST(SESSION_CONTEXT(N'PlatformScope') AS bit) = 1;
GO

-- 소속 표: 현재 영역, 또는 내 줄(내 소속 찾기, R-135)
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

-- 통독 읽기: 본인, 플랫폼, 현재 영역의 관리자 · 교사 · 임원이 그 영역 사람을 볼 때(R-159),
-- 또는 영역이 참여자 현황을 공개하면 같은 영역 사람끼리(0041). 학생 T1001 · 기타 T1099 · 학부모(코드 없음)는 관리 권한이 없다
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

-- =====================================================================
-- 11. RLS — 정책 (FILTER = 읽기에서 안 보임, BLOCK = 다른 영역 값으로 쓰기 거절). 통독 · Identity 는 부서 필터 없음(0090)
-- =====================================================================

CREATE SECURITY POLICY sec.TenantIsolation
    -- 소속: 내 줄은 어느 영역에서든 보인다
    ADD FILTER PREDICATE sec.fn_Membership(TenantId, UserId) ON dbo.DeptRoleUserAssoc,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.DeptRoleUserAssoc AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.DeptRoleUserAssoc AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Membership(TenantId, UserId) ON dbo.SignupApplication,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.SignupApplication AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.SignupApplication AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON dbo.CodeData,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.CodeData AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.CodeData AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.GuardianLink,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.GuardianLink AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.GuardianLink AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.TenantSeason,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.TenantSeason AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.TenantSeason AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.Outbox,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.Outbox AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.Outbox AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.SearchLog,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.SearchLog AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.SearchLog AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyTeam,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyTeam AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyTeam AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyMember,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyMember AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyMember AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyRequest,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyRequest AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyRequest AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyNotice,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyNotice AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyNotice AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyMilestoneConfig,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyMilestoneConfig AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyMilestoneConfig AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyMilestoneHidden,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyMilestoneHidden AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_FamilyMilestoneHidden AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_TeamAward,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_TeamAward AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_TeamAward AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_MissionActivity,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_MissionActivity AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_MissionActivity AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_QuizAttempt,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_QuizAttempt AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON dbo.DM2_QuizAttempt AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Promotion(SourceTenantId, TargetTenantId) ON app.PromotionRequest,
    ADD BLOCK  PREDICATE sec.fn_Promotion(SourceTenantId, TargetTenantId) ON app.PromotionRequest AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Promotion(SourceTenantId, TargetTenantId) ON app.PromotionRequest AFTER UPDATE,
    -- 콘텐츠 (공용 + 영역)
    ADD FILTER PREDICATE sec.fn_SharedRead(TenantId)  ON app.DailyWord,
    ADD BLOCK  PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyWord AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyWord AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_SharedRead(TenantId)  ON app.DailyWordQuestion,
    ADD BLOCK  PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyWordQuestion AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyWordQuestion AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_SharedRead(TenantId)  ON app.DailyWordVideo,
    ADD BLOCK  PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyWordVideo AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyWordVideo AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_SharedRead(TenantId)  ON app.DailyPrayer,
    ADD BLOCK  PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyPrayer AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyPrayer AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_SharedRead(TenantId)  ON app.DailyQuiz,
    ADD BLOCK  PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyQuiz AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyQuiz AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_SharedRead(TenantId)  ON app.DailyQuizChoice,
    ADD BLOCK  PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyQuizChoice AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyQuizChoice AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_SharedRead(TenantId)  ON app.DailyQuizAcceptedAnswer,
    ADD BLOCK  PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyQuizAcceptedAnswer AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_SharedWrite(TenantId) ON app.DailyQuizAcceptedAnswer AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_SharedRead(TenantId)  ON app.WeeklyVerse,
    ADD BLOCK  PREDICATE sec.fn_SharedWrite(TenantId) ON app.WeeklyVerse AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_SharedWrite(TenantId) ON app.WeeklyVerse AFTER UPDATE,
    -- 책갈피는 본인만, 통독 기록은 볼 권한이 넓지만 쓰기 · 지우기는 본인만
    ADD FILTER PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingBookmark,
    ADD BLOCK  PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingBookmark AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingBookmark AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_ReadingRead(UserName) ON dbo.DM2_ReadingRound,
    ADD BLOCK  PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingRound AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingRound AFTER UPDATE,
    ADD BLOCK  PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingRound BEFORE UPDATE,
    ADD BLOCK  PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingRound BEFORE DELETE,
    ADD FILTER PREDICATE sec.fn_ReadingRead(UserName) ON dbo.DM2_ReadingVerse,
    ADD BLOCK  PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingVerse AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingVerse AFTER UPDATE,
    ADD BLOCK  PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingVerse BEFORE UPDATE,
    ADD BLOCK  PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingVerse BEFORE DELETE,
    ADD FILTER PREDICATE sec.fn_ReadingRead(UserName) ON dbo.DM2_ReadingChapterRead,
    ADD BLOCK  PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingChapterRead AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingChapterRead AFTER UPDATE,
    ADD BLOCK  PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingChapterRead BEFORE UPDATE,
    ADD BLOCK  PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingChapterRead BEFORE DELETE,
    ADD FILTER PREDICATE sec.fn_ReadingRead(UserName) ON dbo.DM2_ReadingChapterProgress,
    ADD BLOCK  PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingChapterProgress AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingChapterProgress AFTER UPDATE,
    ADD BLOCK  PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingChapterProgress BEFORE UPDATE,
    ADD BLOCK  PREDICATE sec.fn_Person(UserName) ON dbo.DM2_ReadingChapterProgress BEFORE DELETE
WITH (STATE = ON, SCHEMABINDING = ON);
GO
