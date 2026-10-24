-- =====================================================================
-- 도전 성경말씀 — 참조 DDL (SQL Server 2019 이상)
-- 설계 확인용. 실제 DB 는 EF Core 마이그레이션으로 만든다(물리-설계.md 5절).
-- 규칙: 현재 시각 함수 기본값 금지, 영역 표는 TenantId 선두 클러스터, 영역 간 참조는 복합 FK.
-- =====================================================================

CREATE SCHEMA app;
GO
CREATE SCHEMA sec;
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

CREATE TABLE app.Account (
    Id              int IDENTITY   NOT NULL CONSTRAINT PK_Account PRIMARY KEY,
    LoginId         nvarchar(50)   NOT NULL CONSTRAINT UQ_Account_LoginId UNIQUE,
    PasswordHash    varchar(200)   NOT NULL,
    DisplayName     nvarchar(50)   NOT NULL,       -- 이름 또는 별명. 전화 · 생일 · 주소 열 없음(R-137)
    PhotoPath       nvarchar(260)  NULL,
    IsPlatformAdmin bit            NOT NULL,
    MustChangePassword bit         NOT NULL,       -- 임시 비밀번호 초기화 후 참(0035)
    FamilyTenantId  int            NULL,           -- 보호자가 고른 가족과 함께할 부서(0046). FK 는 Tenant 생성 뒤 ALTER 로
    LastTenantId    int            NULL CONSTRAINT FK_Account_LastTenant REFERENCES app.Tenant(Id),
    Status          varchar(20)    NOT NULL CONSTRAINT CK_Account_Status CHECK (Status IN ('Active','Disabled')),
    CreatedUtc      datetime2(3)   NOT NULL
);

CREATE TABLE app.AuditLog (
    Id              bigint IDENTITY NOT NULL CONSTRAINT PK_AuditLog PRIMARY KEY,
    OccurredUtc     datetime2(3)    NOT NULL,
    ActorAccountId  int             NOT NULL,
    TenantId        int             NULL,
    Action          varchar(40)     NOT NULL,
    TargetType      varchar(40)     NULL,
    TargetId        bigint          NULL,
    Detail          nvarchar(max)   NULL CONSTRAINT CK_AuditLog_Detail CHECK (Detail IS NULL OR ISJSON(Detail) = 1)
);
CREATE INDEX IX_AuditLog_Tenant ON app.AuditLog (TenantId, OccurredUtc);

CREATE TABLE app.PlatformRuleSetVersion (
    Id                  int IDENTITY  NOT NULL CONSTRAINT PK_PlatformRuleSetVersion PRIMARY KEY,
    VersionNo           int           NOT NULL CONSTRAINT UQ_PlatformRuleSetVersion_No UNIQUE,
    [Values]            nvarchar(max) NOT NULL CONSTRAINT CK_PlatformRuleSetVersion_Values CHECK (ISJSON([Values]) = 1),
    EffectiveFrom       date          NOT NULL,     -- 0060: 이 날부터 적용(내일 · 다음 주 시작일). 그날 규칙 = 시작일이 지난 버전 중 번호가 가장 큰 것
    ChangedUtc          datetime2(3)  NOT NULL,
    ChangedByAccountId  int           NOT NULL CONSTRAINT FK_PlatformRuleSetVersion_Account REFERENCES app.Account(Id)
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
-- 2. 영역 범위 — 테넌시 · 설정
-- =====================================================================

CREATE TABLE app.Membership (
    Id                  int IDENTITY  NOT NULL,
    TenantId            int           NOT NULL CONSTRAINT FK_Membership_Tenant REFERENCES app.Tenant(Id),
    AccountId           int           NOT NULL CONSTRAINT FK_Membership_Account REFERENCES app.Account(Id),
    Status              varchar(20)   NOT NULL CONSTRAINT CK_Membership_Status
                        CHECK (Status IN ('Requested','Approved','Suspended','Released','Rejected','PromotionPending')),
    GroupLabel          nvarchar(50)  NULL,
    RequestedUtc        datetime2(3)  NOT NULL,
    DecidedUtc          datetime2(3)  NULL,
    DecidedByAccountId  int           NULL CONSTRAINT FK_Membership_DecidedBy REFERENCES app.Account(Id),
    CONSTRAINT PK_Membership PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_Membership_TenantId_Id UNIQUE CLUSTERED (TenantId, Id),
    CONSTRAINT UQ_Membership_TenantAccount UNIQUE (TenantId, AccountId)          -- 0027
);
CREATE INDEX IX_Membership_Account ON app.Membership (AccountId);               -- 내 소속 영역 목록(R-135)

CREATE TABLE app.MembershipRole (
    TenantId      int         NOT NULL,
    MembershipId  int         NOT NULL,
    Role          varchar(20) NOT NULL CONSTRAINT CK_MembershipRole_Role
                  CHECK (Role IN ('Participant','Guardian','Teacher','TenantAdmin')),
    CONSTRAINT PK_MembershipRole PRIMARY KEY (TenantId, MembershipId, Role),
    CONSTRAINT FK_MembershipRole_Membership FOREIGN KEY (TenantId, MembershipId) REFERENCES app.Membership (TenantId, Id)
);

CREATE TABLE app.Participant (
    Id            int IDENTITY NOT NULL,
    TenantId      int          NOT NULL,
    MembershipId  int          NOT NULL,
    IsActive      bit          NOT NULL,
    CONSTRAINT PK_Participant PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_Participant_TenantId_Id UNIQUE CLUSTERED (TenantId, Id),
    CONSTRAINT UQ_Participant_Membership UNIQUE (TenantId, MembershipId),
    CONSTRAINT FK_Participant_Membership FOREIGN KEY (TenantId, MembershipId) REFERENCES app.Membership (TenantId, Id)
);

CREATE TABLE app.GuardianLink (
    Id                    int IDENTITY NOT NULL,
    TenantId              int          NOT NULL,
    GuardianMembershipId  int          NOT NULL,
    ParticipantId         int          NOT NULL,
    CreatedUtc            datetime2(3) NOT NULL,
    CreatedByAccountId    int          NOT NULL CONSTRAINT FK_GuardianLink_CreatedBy REFERENCES app.Account(Id),
    CONSTRAINT PK_GuardianLink PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_GuardianLink_TenantId_Id UNIQUE CLUSTERED (TenantId, Id),
    CONSTRAINT UQ_GuardianLink_Pair UNIQUE (TenantId, GuardianMembershipId, ParticipantId),
    CONSTRAINT FK_GuardianLink_Guardian FOREIGN KEY (TenantId, GuardianMembershipId) REFERENCES app.Membership (TenantId, Id),
    CONSTRAINT FK_GuardianLink_Participant FOREIGN KEY (TenantId, ParticipantId) REFERENCES app.Participant (TenantId, Id)
);
CREATE INDEX IX_GuardianLink_Participant ON app.GuardianLink (TenantId, ParticipantId);

CREATE TABLE app.TenantRuleSetVersion (
    Id                  int IDENTITY  NOT NULL,
    TenantId            int           NOT NULL CONSTRAINT FK_TenantRuleSetVersion_Tenant REFERENCES app.Tenant(Id),
    VersionNo           int           NOT NULL,
    [Values]            nvarchar(max) NOT NULL CONSTRAINT CK_TenantRuleSetVersion_Values CHECK (ISJSON([Values]) = 1),  -- 바꾼 키만
    ChangedUtc          datetime2(3)  NOT NULL,
    ChangedByAccountId  int           NOT NULL CONSTRAINT FK_TenantRuleSetVersion_Account REFERENCES app.Account(Id),
    CONSTRAINT PK_TenantRuleSetVersion PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_TenantRuleSetVersion_No UNIQUE CLUSTERED (TenantId, VersionNo),
    CONSTRAINT UQ_TenantRuleSetVersion_TenantId_Id UNIQUE (TenantId, Id)
);

CREATE TABLE app.TenantSeason (                                                 -- 0028
    TenantId            int          NOT NULL CONSTRAINT FK_TenantSeason_Tenant REFERENCES app.Tenant(Id),
    [Year]              smallint     NOT NULL,
    StartDate           date         NOT NULL,
    ChangedUtc          datetime2(3) NOT NULL,
    ChangedByAccountId  int          NOT NULL CONSTRAINT FK_TenantSeason_Account REFERENCES app.Account(Id),
    CONSTRAINT PK_TenantSeason PRIMARY KEY (TenantId, [Year]),
    CONSTRAINT CK_TenantSeason_StartInYear CHECK (YEAR(StartDate) = [Year])
);

-- 진급 요청 — 예외 범위 (0026, 0030) ------------------------------------

CREATE TABLE app.PromotionRequest (
    Id                    int IDENTITY NOT NULL CONSTRAINT PK_PromotionRequest PRIMARY KEY,
    AccountId             int          NOT NULL CONSTRAINT FK_PromotionRequest_Account REFERENCES app.Account(Id),
    SourceTenantId        int          NOT NULL CONSTRAINT FK_PromotionRequest_Source REFERENCES app.Tenant(Id),
    SourceMembershipId    int          NOT NULL,   -- 승인 시 삭제되므로 FK 없음
    TargetTenantId        int          NOT NULL CONSTRAINT FK_PromotionRequest_Target REFERENCES app.Tenant(Id),
    TargetMembershipId    int          NULL,
    Status                varchar(20)  NOT NULL CONSTRAINT CK_PromotionRequest_Status CHECK (Status IN ('Pending','Approved','Rejected')),
    RequestedUtc          datetime2(3) NOT NULL,
    RequestedByAccountId  int          NOT NULL CONSTRAINT FK_PromotionRequest_RequestedBy REFERENCES app.Account(Id),
    DecidedUtc            datetime2(3) NULL,
    DecidedByAccountId    int          NULL CONSTRAINT FK_PromotionRequest_DecidedBy REFERENCES app.Account(Id),
    CONSTRAINT CK_PromotionRequest_DifferentTenant CHECK (SourceTenantId <> TargetTenantId)
);
CREATE UNIQUE INDEX UX_PromotionRequest_Pending ON app.PromotionRequest (AccountId, SourceTenantId) WHERE Status = 'Pending';
CREATE INDEX IX_PromotionRequest_Target ON app.PromotionRequest (TargetTenantId, Status);

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
    CreatedByAccountId  int            NOT NULL CONSTRAINT FK_DailyWord_CreatedBy REFERENCES app.Account(Id),
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
    CreatedByAccountId  int           NOT NULL CONSTRAINT FK_DailyPrayer_CreatedBy REFERENCES app.Account(Id),
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
    CreatedByAccountId  int            NOT NULL CONSTRAINT FK_DailyQuiz_CreatedBy REFERENCES app.Account(Id),
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
    CreatedByAccountId  int           NOT NULL CONSTRAINT FK_WeeklyVerse_CreatedBy REFERENCES app.Account(Id),
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

-- =====================================================================
-- 4. 영역 범위 — 미션 · 점수
-- =====================================================================

CREATE TABLE app.Activity (
    Id              bigint IDENTITY NOT NULL,
    TenantId        int             NOT NULL,
    ParticipantId   int             NOT NULL,
    MissionItem     varchar(20)     NOT NULL CONSTRAINT CK_Activity_Item
                    CHECK (MissionItem IN ('Word.Me','Word.Parent','Prayer.Me','Prayer.Others','Quiz.Correct','Recite')),   -- 0047
    MissionDate     date            NOT NULL,     -- 암송은 실제 수행일(0024)
    DailyWordId     int             NULL CONSTRAINT FK_Activity_Word   REFERENCES app.DailyWord(Id),
    DailyPrayerId   int             NULL CONSTRAINT FK_Activity_Prayer REFERENCES app.DailyPrayer(Id),
    DailyQuizId     int             NULL CONSTRAINT FK_Activity_Quiz   REFERENCES app.DailyQuiz(Id),
    WeeklyVerseId   int             NULL CONSTRAINT FK_Activity_Verse  REFERENCES app.WeeklyVerse(Id),
    ReciteStartDate date            NULL,         -- 암송: 그 주(콘텐츠 기간)의 시작일
    Points          int             NOT NULL CONSTRAINT CK_Activity_Points CHECK (Points >= 0),
    PerformedUtc    datetime2(3)    NOT NULL,
    CONSTRAINT PK_Activity PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_Activity_TenantId_Id UNIQUE (TenantId, Id),
    CONSTRAINT FK_Activity_Participant FOREIGN KEY (TenantId, ParticipantId) REFERENCES app.Participant (TenantId, Id),
    -- 항목 종류에 맞는 콘텐츠 열 하나만 값이 있어야 한다
    CONSTRAINT CK_Activity_Content CHECK (
        (MissionItem IN ('Word.Me','Word.Parent')
            AND DailyWordId IS NOT NULL AND DailyPrayerId IS NULL AND DailyQuizId IS NULL AND WeeklyVerseId IS NULL AND ReciteStartDate IS NULL)
     OR (MissionItem IN ('Prayer.Me','Prayer.Others')
            AND DailyWordId IS NULL AND DailyPrayerId IS NOT NULL AND DailyQuizId IS NULL AND WeeklyVerseId IS NULL AND ReciteStartDate IS NULL)
     OR (MissionItem = 'Quiz.Correct'
            AND DailyWordId IS NULL AND DailyPrayerId IS NULL AND DailyQuizId IS NOT NULL AND WeeklyVerseId IS NULL AND ReciteStartDate IS NULL)
     OR (MissionItem = 'Recite'
            AND DailyWordId IS NULL AND DailyPrayerId IS NULL AND DailyQuizId IS NULL AND WeeklyVerseId IS NOT NULL AND ReciteStartDate IS NOT NULL)
    )
);
CREATE CLUSTERED INDEX CX_Activity ON app.Activity (TenantId, ParticipantId, MissionDate);
CREATE UNIQUE INDEX UX_Activity_Daily  ON app.Activity (TenantId, ParticipantId, MissionDate, MissionItem) WHERE MissionItem <> 'Recite';  -- R-61
CREATE UNIQUE INDEX UX_Activity_Recite ON app.Activity (TenantId, ParticipantId, ReciteStartDate)          WHERE MissionItem = 'Recite';   -- R-51

CREATE TABLE app.QuizAttempt (
    Id                bigint IDENTITY NOT NULL,
    TenantId          int             NOT NULL,
    ParticipantId     int             NOT NULL,
    MissionDate       date            NOT NULL,
    AttemptNo         tinyint         NOT NULL CONSTRAINT CK_QuizAttempt_No CHECK (AttemptNo >= 1),
    DailyQuizId       int             NOT NULL CONSTRAINT FK_QuizAttempt_Quiz REFERENCES app.DailyQuiz(Id),
    SelectedChoiceId  int             NULL CONSTRAINT FK_QuizAttempt_Choice REFERENCES app.DailyQuizChoice(Id),
    AnswerText        nvarchar(200)   NULL,
    IsCorrect         bit             NOT NULL,
    AttemptedUtc      datetime2(3)    NOT NULL,
    CONSTRAINT PK_QuizAttempt PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_QuizAttempt_No UNIQUE CLUSTERED (TenantId, ParticipantId, MissionDate, AttemptNo),
    CONSTRAINT UQ_QuizAttempt_TenantId_Id UNIQUE (TenantId, Id),
    CONSTRAINT FK_QuizAttempt_Participant FOREIGN KEY (TenantId, ParticipantId) REFERENCES app.Participant (TenantId, Id),
    CONSTRAINT CK_QuizAttempt_Answer CHECK (SelectedChoiceId IS NOT NULL OR AnswerText IS NOT NULL)
);

CREATE TABLE app.PointLedger (                                                  -- 추가만(R-63, R-64)
    Id             bigint IDENTITY NOT NULL,
    TenantId       int             NOT NULL,
    ParticipantId  int             NOT NULL,
    MissionDate    date            NOT NULL,
    MissionItem    varchar(20)     NOT NULL,
    Points         int             NOT NULL CONSTRAINT CK_PointLedger_Points CHECK (Points > 0),
    ActivityId     bigint          NOT NULL,
    RecordedUtc    datetime2(3)    NOT NULL,
    CONSTRAINT PK_PointLedger PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_PointLedger_TenantId_Id UNIQUE (TenantId, Id),
    CONSTRAINT UQ_PointLedger_Activity UNIQUE (TenantId, ActivityId),
    CONSTRAINT FK_PointLedger_Participant FOREIGN KEY (TenantId, ParticipantId) REFERENCES app.Participant (TenantId, Id),
    CONSTRAINT FK_PointLedger_Activity FOREIGN KEY (TenantId, ActivityId) REFERENCES app.Activity (TenantId, Id)
);
CREATE CLUSTERED INDEX CX_PointLedger ON app.PointLedger (TenantId, ParticipantId, MissionDate);

CREATE TABLE app.DailyPoint (                                                   -- 집계(X-03)
    TenantId       int      NOT NULL,
    ParticipantId  int      NOT NULL,
    [Date]         date     NOT NULL,
    WordPoints     smallint NOT NULL,
    PrayerPoints   smallint NOT NULL,
    QuizPoints     smallint NOT NULL,
    RecitePoints   smallint NOT NULL,
    TotalPoints    AS (CAST(WordPoints + PrayerPoints + QuizPoints + RecitePoints AS int)) PERSISTED,
    CONSTRAINT PK_DailyPoint PRIMARY KEY CLUSTERED (TenantId, ParticipantId, [Date]),
    CONSTRAINT FK_DailyPoint_Participant FOREIGN KEY (TenantId, ParticipantId) REFERENCES app.Participant (TenantId, Id)
);

CREATE TABLE app.SeasonPoint (                                                  -- 집계
    TenantId       int       NOT NULL,
    ParticipantId  int       NOT NULL,
    [Year]         smallint  NOT NULL,
    TotalPoints    int       NOT NULL,
    LastPointDate  date      NULL,
    StreakDays     int       NOT NULL,
    CONSTRAINT PK_SeasonPoint PRIMARY KEY CLUSTERED (TenantId, ParticipantId, [Year]),
    CONSTRAINT FK_SeasonPoint_Participant FOREIGN KEY (TenantId, ParticipantId) REFERENCES app.Participant (TenantId, Id)
);
CREATE INDEX IX_SeasonPoint_Rank ON app.SeasonPoint (TenantId, [Year], TotalPoints DESC)
    INCLUDE (LastPointDate, StreakDays);                                        -- 순위(R-80)

CREATE TABLE app.MilestoneReached (
    Id              int IDENTITY NOT NULL,
    TenantId        int          NOT NULL,
    ParticipantId   int          NOT NULL,
    [Year]          smallint     NOT NULL,
    Threshold       int          NOT NULL,
    ReachedUtc      datetime2(3) NOT NULL,
    SnoozeUntilUtc  datetime2(3) NULL,                                          -- R-103
    CONSTRAINT PK_MilestoneReached PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_MilestoneReached_TenantId_Id UNIQUE (TenantId, Id),
    CONSTRAINT UQ_MilestoneReached UNIQUE CLUSTERED (TenantId, ParticipantId, [Year], Threshold),   -- R-101
    CONSTRAINT FK_MilestoneReached_Participant FOREIGN KEY (TenantId, ParticipantId) REFERENCES app.Participant (TenantId, Id)
);

CREATE TABLE app.Outbox (
    Id             bigint IDENTITY NOT NULL,
    TenantId       int             NOT NULL,
    ParticipantId  int             NULL,          -- 진급 삭제 시 미발송분 정리용
    EventType      varchar(50)     NOT NULL,
    Payload        nvarchar(max)   NOT NULL CONSTRAINT CK_Outbox_Payload CHECK (ISJSON(Payload) = 1),
    OccurredUtc    datetime2(3)    NOT NULL,
    SentUtc        datetime2(3)    NULL,
    CONSTRAINT PK_Outbox PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_Outbox_TenantId_Id UNIQUE CLUSTERED (TenantId, Id)
);
CREATE INDEX IX_Outbox_Unsent ON app.Outbox (OccurredUtc) WHERE SentUtc IS NULL;

-- =====================================================================
-- 5. 영역 범위 — 가족팀
-- =====================================================================

CREATE TABLE app.FamilyTeam (
    Id          int IDENTITY  NOT NULL,
    TenantId    int           NOT NULL CONSTRAINT FK_FamilyTeam_Tenant REFERENCES app.Tenant(Id),
    Name        nvarchar(50)  NULL,                -- 비면 "{대표 학생} 가족"(R-93)
    Memo        nvarchar(500) NULL,
    IsActive    bit           NOT NULL,
    CreatedUtc  datetime2(3)  NOT NULL,
    CONSTRAINT PK_FamilyTeam PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_FamilyTeam_TenantId_Id UNIQUE CLUSTERED (TenantId, Id)
);

CREATE TABLE app.FamilyMember (
    Id             int IDENTITY NOT NULL,
    TenantId       int          NOT NULL,
    FamilyTeamId   int          NOT NULL,
    ParticipantId  int          NOT NULL,
    Relation       varchar(20)  NOT NULL CONSTRAINT CK_FamilyMember_Relation
                   CHECK (Relation IN ('Student','Mother','Father','Grandmother','Grandfather','Other')),
    SortOrder      tinyint      NOT NULL,
    IsActive       bit          NOT NULL,
    CONSTRAINT PK_FamilyMember PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_FamilyMember_TenantId_Id UNIQUE CLUSTERED (TenantId, Id),
    CONSTRAINT UQ_FamilyMember_TeamParticipant UNIQUE (TenantId, FamilyTeamId, ParticipantId),
    CONSTRAINT FK_FamilyMember_Team FOREIGN KEY (TenantId, FamilyTeamId) REFERENCES app.FamilyTeam (TenantId, Id),
    CONSTRAINT FK_FamilyMember_Participant FOREIGN KEY (TenantId, ParticipantId) REFERENCES app.Participant (TenantId, Id)  -- 같은 영역만(R-90)
);
CREATE UNIQUE INDEX UX_FamilyMember_OneActiveTeam ON app.FamilyMember (TenantId, ParticipantId) WHERE IsActive = 1;  -- R-91

CREATE TABLE app.FamilyMilestoneReached (
    TenantId      int          NOT NULL,
    FamilyTeamId  int          NOT NULL,
    [Year]        smallint     NOT NULL,
    Threshold     int          NOT NULL,
    ReachedUtc    datetime2(3) NOT NULL,
    CONSTRAINT PK_FamilyMilestoneReached PRIMARY KEY (TenantId, FamilyTeamId, [Year], Threshold),
    CONSTRAINT FK_FamilyMilestoneReached_Team FOREIGN KEY (TenantId, FamilyTeamId) REFERENCES app.FamilyTeam (TenantId, Id)
);

CREATE TABLE app.FamilyMilestoneDismiss (                                       -- R-97
    TenantId              int          NOT NULL,
    FamilyTeamId          int          NOT NULL,
    [Year]                smallint     NOT NULL,
    Threshold             int          NOT NULL,
    DismissedUtc          datetime2(3) NOT NULL,
    DismissedByAccountId  int          NOT NULL CONSTRAINT FK_FamilyMilestoneDismiss_Account REFERENCES app.Account(Id),
    CONSTRAINT PK_FamilyMilestoneDismiss PRIMARY KEY (TenantId, FamilyTeamId, [Year], Threshold),
    CONSTRAINT FK_FamilyMilestoneDismiss_Team FOREIGN KEY (TenantId, FamilyTeamId) REFERENCES app.FamilyTeam (TenantId, Id)
);

-- =====================================================================
-- 6. 계정 범위 — 성경 읽기 (0029)
-- =====================================================================

CREATE TABLE app.VerseReadMark (
    AccountId  int          NOT NULL CONSTRAINT FK_VerseReadMark_Account REFERENCES app.Account(Id),
    BookId     tinyint      NOT NULL CONSTRAINT FK_VerseReadMark_Book REFERENCES app.BibleBook(Id),
    Chapter    smallint     NOT NULL,
    Verse      smallint     NOT NULL,
    ReadUtc    datetime2(3) NOT NULL,
    CONSTRAINT PK_VerseReadMark PRIMARY KEY (AccountId, BookId, Chapter, Verse)
);

CREATE TABLE app.Bookmark (
    AccountId   int          NOT NULL CONSTRAINT FK_Bookmark_Account REFERENCES app.Account(Id),
    BookId      tinyint      NOT NULL CONSTRAINT FK_Bookmark_Book REFERENCES app.BibleBook(Id),
    Chapter     smallint     NOT NULL,
    Verse       smallint     NOT NULL,
    CreatedUtc  datetime2(3) NOT NULL,
    CONSTRAINT PK_Bookmark PRIMARY KEY (AccountId, BookId, Chapter, Verse)
);

-- 성경통독 (0039, 0040) ------------------------------------------------

CREATE TABLE app.ReadingRound (
    Id            int IDENTITY NOT NULL CONSTRAINT PK_ReadingRound PRIMARY KEY,
    AccountId     int          NOT NULL CONSTRAINT FK_ReadingRound_Account REFERENCES app.Account(Id),
    RoundNo       int          NOT NULL CONSTRAINT CK_ReadingRound_No CHECK (RoundNo >= 1),
    StartedUtc    datetime2(3) NOT NULL,
    CompletedUtc  datetime2(3) NULL,           -- 1,189장 모두 읽음
    ClosedUtc     datetime2(3) NULL,           -- 다 읽지 않고 새 회차 시작(R-154)
    CONSTRAINT UQ_ReadingRound UNIQUE (AccountId, RoundNo),
    CONSTRAINT CK_ReadingRound_End CHECK (CompletedUtc IS NULL OR ClosedUtc IS NULL)
);
CREATE UNIQUE INDEX UX_ReadingRound_Open ON app.ReadingRound (AccountId)
    WHERE CompletedUtc IS NULL AND ClosedUtc IS NULL;                            -- 진행 중 1개(R-153)

CREATE TABLE app.ChapterRead (
    AccountId  int          NOT NULL,
    RoundNo    int          NOT NULL,
    BookId     tinyint      NOT NULL CONSTRAINT FK_ChapterRead_Book REFERENCES app.BibleBook(Id),
    Chapter    smallint     NOT NULL CONSTRAINT CK_ChapterRead_Chapter CHECK (Chapter >= 1),
    ReadUtc    datetime2(3) NOT NULL,
    CONSTRAINT PK_ChapterRead PRIMARY KEY (AccountId, RoundNo, BookId, Chapter),
    CONSTRAINT FK_ChapterRead_Round FOREIGN KEY (AccountId, RoundNo) REFERENCES app.ReadingRound (AccountId, RoundNo)
);
CREATE INDEX IX_ChapterRead_Day ON app.ChapterRead (AccountId, ReadUtc);        -- 오늘 읽은 장 · 연속 읽은 날(R-156)

-- 0041: 절 넘김 읽기 · 듣기 해설 · 인기 검색어
CREATE TABLE app.BibleChapter (
    BookId      tinyint  NOT NULL CONSTRAINT FK_BibleChapter_Book REFERENCES app.BibleBook(Id),
    Chapter     smallint NOT NULL,
    VerseCount  smallint NOT NULL CONSTRAINT CK_BibleChapter_Verses CHECK (VerseCount >= 1),   -- 기본 번역(개역개정) 기준
    CONSTRAINT PK_BibleChapter PRIMARY KEY (BookId, Chapter)
);

CREATE TABLE app.ChapterProgress (
    AccountId         int          NOT NULL,
    RoundNo           int          NOT NULL,
    BookId            tinyint      NOT NULL,
    Chapter           smallint     NOT NULL,
    LastViewedVerse   smallint     NOT NULL CONSTRAINT CK_ChapterProgress_Verse CHECK (LastViewedVerse >= 1),    -- 보던 위치(R-161, 0042)
    UpdatedUtc        datetime2(3) NOT NULL,
    CONSTRAINT PK_ChapterProgress PRIMARY KEY (AccountId, RoundNo, BookId, Chapter),
    CONSTRAINT FK_ChapterProgress_Round FOREIGN KEY (AccountId, RoundNo) REFERENCES app.ReadingRound (AccountId, RoundNo),
    CONSTRAINT FK_ChapterProgress_Chapter FOREIGN KEY (BookId, Chapter) REFERENCES app.BibleChapter (BookId, Chapter)
);
CREATE INDEX IX_ChapterProgress_Recent ON app.ChapterProgress (AccountId, UpdatedUtc DESC);   -- 이어 읽기(R-161)

CREATE TABLE app.RoundVerseRead (                                               -- 회차별 절 읽음(R-160, 0042)
    AccountId  int          NOT NULL,
    RoundNo    int          NOT NULL,
    BookId     tinyint      NOT NULL,
    Chapter    smallint     NOT NULL,
    Verse      smallint     NOT NULL CONSTRAINT CK_RoundVerseRead_Verse CHECK (Verse >= 1),
    ReadUtc    datetime2(3) NOT NULL,
    CONSTRAINT PK_RoundVerseRead PRIMARY KEY (AccountId, RoundNo, BookId, Chapter, Verse),
    CONSTRAINT FK_RoundVerseRead_Round FOREIGN KEY (AccountId, RoundNo) REFERENCES app.ReadingRound (AccountId, RoundNo),
    CONSTRAINT FK_RoundVerseRead_Chapter FOREIGN KEY (BookId, Chapter) REFERENCES app.BibleChapter (BookId, Chapter)
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

-- =====================================================================
-- 7. RLS — 조건 함수 (세션 값: TenantId, AccountId, PlatformScope — 읽기 전용으로 설정)
-- =====================================================================

CREATE FUNCTION sec.fn_Tenant (@TenantId int)
RETURNS TABLE WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS Allowed
    WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
       OR CAST(SESSION_CONTEXT(N'PlatformScope') AS bit) = 1;
GO

CREATE FUNCTION sec.fn_Membership (@TenantId int, @AccountId int)
RETURNS TABLE WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS Allowed
    WHERE @TenantId  = CAST(SESSION_CONTEXT(N'TenantId')  AS int)
       OR @AccountId = CAST(SESSION_CONTEXT(N'AccountId') AS int)      -- 내 소속 목록(R-135)
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

CREATE FUNCTION sec.fn_Account (@AccountId int)
RETURNS TABLE WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS Allowed
    WHERE @AccountId = CAST(SESSION_CONTEXT(N'AccountId') AS int)
       OR CAST(SESSION_CONTEXT(N'PlatformScope') AS bit) = 1;
GO

-- 통독 읽기: 본인, 플랫폼, 또는 현재 영역의 관리자 · 교사가 그 영역 소속원을 볼 때(R-159)
CREATE FUNCTION sec.fn_ReadingRead (@AccountId int)
RETURNS TABLE WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS Allowed
    WHERE @AccountId = CAST(SESSION_CONTEXT(N'AccountId') AS int)
       OR CAST(SESSION_CONTEXT(N'PlatformScope') AS bit) = 1
       OR (
            EXISTS (SELECT 1 FROM app.Membership m
                    WHERE m.TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
                      AND m.AccountId = @AccountId
                      AND m.Status IN ('Approved', 'Suspended'))
        AND EXISTS (SELECT 1 FROM app.Membership a
                    JOIN app.MembershipRole r ON r.TenantId = a.TenantId AND r.MembershipId = a.Id
                    WHERE a.TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
                      AND a.AccountId = CAST(SESSION_CONTEXT(N'AccountId') AS int)
                      AND a.Status = 'Approved'
                      AND r.Role IN ('TenantAdmin', 'Teacher'))
       )
       -- 0041: 영역이 참여자 현황을 공개하면 같은 영역 승인 소속원끼리
       OR (
            EXISTS (SELECT 1 FROM app.Tenant t
                    WHERE t.Id = CAST(SESSION_CONTEXT(N'TenantId') AS int) AND t.ReadingBoardPublic = 1)
        AND EXISTS (SELECT 1 FROM app.Membership m
                    WHERE m.TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
                      AND m.AccountId = @AccountId AND m.Status = 'Approved')
        AND EXISTS (SELECT 1 FROM app.Membership s
                    WHERE s.TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int)
                      AND s.AccountId = CAST(SESSION_CONTEXT(N'AccountId') AS int) AND s.Status = 'Approved')
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
-- 8. RLS — 정책 (FILTER = 읽기에서 안 보임, BLOCK = 다른 영역 값으로 쓰기 거절)
-- =====================================================================

CREATE SECURITY POLICY sec.TenantIsolation
    -- 테넌시
    ADD FILTER PREDICATE sec.fn_Membership(TenantId, AccountId) ON app.Membership,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.Membership AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.Membership AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.MembershipRole,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.MembershipRole AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.MembershipRole AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.Participant,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.Participant AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.Participant AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.GuardianLink,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.GuardianLink AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.GuardianLink AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.TenantRuleSetVersion,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.TenantRuleSetVersion AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.TenantRuleSetVersion AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.TenantSeason,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.TenantSeason AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.TenantSeason AFTER UPDATE,
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
    -- 미션 · 점수
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.Activity,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.Activity AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.Activity AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.QuizAttempt,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.QuizAttempt AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.QuizAttempt AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.PointLedger,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.PointLedger AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.PointLedger AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.DailyPoint,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.DailyPoint AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.DailyPoint AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.SeasonPoint,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.SeasonPoint AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.SeasonPoint AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.MilestoneReached,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.MilestoneReached AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.MilestoneReached AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.Outbox,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.Outbox AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.Outbox AFTER UPDATE,
    -- 가족팀
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyTeam,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyTeam AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyTeam AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyMember,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyMember AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyMember AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyMilestoneReached,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyMilestoneReached AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyMilestoneReached AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyMilestoneDismiss,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyMilestoneDismiss AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyMilestoneDismiss AFTER UPDATE,
    -- 계정 범위
    ADD FILTER PREDICATE sec.fn_Account(AccountId) ON app.VerseReadMark,
    ADD BLOCK  PREDICATE sec.fn_Account(AccountId) ON app.VerseReadMark AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Account(AccountId) ON app.VerseReadMark AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Account(AccountId) ON app.Bookmark,
    ADD BLOCK  PREDICATE sec.fn_Account(AccountId) ON app.Bookmark AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Account(AccountId) ON app.Bookmark AFTER UPDATE,
    -- 성경통독: 읽기는 fn_ReadingRead 로 넓지만 쓰기 · 고치기 · 지우기는 본인만(BEFORE UPDATE · DELETE 가 없으면 보이는 남의 기록을 지울 수 있다)
    ADD FILTER PREDICATE sec.fn_ReadingRead(AccountId) ON app.ReadingRound,
    ADD BLOCK  PREDICATE sec.fn_Account(AccountId) ON app.ReadingRound AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Account(AccountId) ON app.ReadingRound AFTER UPDATE,
    ADD BLOCK  PREDICATE sec.fn_Account(AccountId) ON app.ReadingRound BEFORE UPDATE,
    ADD BLOCK  PREDICATE sec.fn_Account(AccountId) ON app.ReadingRound BEFORE DELETE,
    ADD FILTER PREDICATE sec.fn_ReadingRead(AccountId) ON app.ChapterRead,
    ADD BLOCK  PREDICATE sec.fn_Account(AccountId) ON app.ChapterRead AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Account(AccountId) ON app.ChapterRead AFTER UPDATE,
    ADD BLOCK  PREDICATE sec.fn_Account(AccountId) ON app.ChapterRead BEFORE UPDATE,
    ADD BLOCK  PREDICATE sec.fn_Account(AccountId) ON app.ChapterRead BEFORE DELETE,
    ADD FILTER PREDICATE sec.fn_ReadingRead(AccountId) ON app.ChapterProgress,
    ADD BLOCK  PREDICATE sec.fn_Account(AccountId) ON app.ChapterProgress AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Account(AccountId) ON app.ChapterProgress AFTER UPDATE,
    ADD BLOCK  PREDICATE sec.fn_Account(AccountId) ON app.ChapterProgress BEFORE UPDATE,
    ADD BLOCK  PREDICATE sec.fn_Account(AccountId) ON app.ChapterProgress BEFORE DELETE,
    ADD FILTER PREDICATE sec.fn_ReadingRead(AccountId) ON app.RoundVerseRead,
    ADD BLOCK  PREDICATE sec.fn_Account(AccountId) ON app.RoundVerseRead AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Account(AccountId) ON app.RoundVerseRead AFTER UPDATE,
    ADD BLOCK  PREDICATE sec.fn_Account(AccountId) ON app.RoundVerseRead BEFORE UPDATE,
    ADD BLOCK  PREDICATE sec.fn_Account(AccountId) ON app.RoundVerseRead BEFORE DELETE,
    ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.SearchLog,
    ADD BLOCK  PREDICATE sec.fn_Tenant(TenantId) ON app.SearchLog AFTER INSERT
WITH (STATE = ON, SCHEMABINDING = ON);
GO
