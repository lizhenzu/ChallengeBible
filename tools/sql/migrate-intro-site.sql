-- migrate-intro-site.sql : 부서 소개 화면 표 app.TenantIntro · app.TenantIntroSeen + 부서 격리(RLS) + **유년부에만** 처음 글 넣기 (ADR 0140, 2026-11-23)
-- 사이트 SQL 화면용: [저장]을 고르고 확인 글자 SAVE. 여러 번 실행해도 한 번만 만든다.
-- 게시(배포)하기 **전에** 먼저 실행한다(표가 없으면 소개 화면 조회가 오류).
-- 소개 줄(app.TenantIntro)이 있는 부서만 소개 화면이 생긴다 — 아래 마지막 INSERT 는 유년부(이름 '잠실새내교회 유년부')만 만든다.
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123231718_TenantIntro'
)
BEGIN
    CREATE TABLE [app].[TenantIntro] (
        [TenantId] int NOT NULL,
        [Title] nvarchar(60) NOT NULL,
        [Body] nvarchar(1000) NULL,
        [PhotoStoredName] varchar(60) NULL,
        [PhotoContentType] varchar(30) NULL,
        [IsActive] bit NOT NULL,
        [UpdatedUtc] datetime2(3) NOT NULL,
        [UpdatedByUserName] nvarchar(256) NULL,
        CONSTRAINT [PK_TenantIntro] PRIMARY KEY ([TenantId]),
        CONSTRAINT [FK_TenantIntro_Tenant_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123231718_TenantIntro'
)
BEGIN
    CREATE TABLE [app].[TenantIntroSeen] (
        [TenantId] int NOT NULL,
        [UserName] nvarchar(256) NOT NULL,
        [HideUntilUtc] datetime2(3) NULL,
        [Never] bit NOT NULL,
        [UpdatedUtc] datetime2(3) NOT NULL,
        CONSTRAINT [PK_TenantIntroSeen] PRIMARY KEY ([TenantId], [UserName]),
        CONSTRAINT [FK_TenantIntroSeen_Tenant_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123231718_TenantIntro'
)
BEGIN
    EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.TenantIntro, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.TenantIntro AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.TenantIntro AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.TenantIntroSeen, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.TenantIntroSeen AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.TenantIntroSeen AFTER UPDATE;')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123231718_TenantIntro'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261123231718_TenantIntro', N'9.0.10');
END;

-- 유년부에만 처음 글(관리자가 화면에서 고친다). 이미 있으면 건너뛴다. 값은 시안 예시 — 게시 뒤 /{부서}/admin/intro 에서 바꾼다.
INSERT app.TenantIntro (TenantId, Title, Body, PhotoStoredName, PhotoContentType, IsActive, UpdatedUtc, UpdatedByUserName)
SELECT t.Id, N'유년부에 오신 것을 환영해요', N'말씀 안에서 함께 예배하고, 기도하고, 자라 가는 유년부예요. 매주 주일, 친구들과 만나요.', NULL, NULL, 1, '2026-11-23T23:30:00', 'superadmin'
FROM app.Tenant t
WHERE t.Name = N'잠실새내교회 유년부' AND NOT EXISTS (SELECT 1 FROM app.TenantIntro i WHERE i.TenantId = t.Id);

-- 확인: 표 두 개와 유년부 줄 하나가 보이면 성공
SELECT name FROM sys.tables WHERE name IN (N'TenantIntro', N'TenantIntroSeen');
SELECT t.Name AS 부서, i.Title AS 제목, i.IsActive AS 켬 FROM app.TenantIntro i JOIN app.Tenant t ON t.Id = i.TenantId;
