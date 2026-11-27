-- migrate-bulletin.sql : 부서 주보 표 app.Bulletin · app.BulletinFile 만들기 + 부서 격리(RLS) (ADR 0117, 2026-11-20)
-- PC 의 SSMS 로 실행(사이트 SQL 화면은 BEGIN TRANSACTION 을 막는다). 여러 번 실행해도 한 번만 만든다.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120003814_Bulletin'
)
BEGIN
    CREATE TABLE [app].[Bulletin] (
        [TenantId] int NOT NULL,
        [Id] int NOT NULL IDENTITY,
        [Date] date NOT NULL,
        [Title] nvarchar(100) NULL,
        [Body] nvarchar(4000) NULL,
        [CreatedUtc] datetime2(3) NOT NULL,
        [CreatedByUserName] nvarchar(256) NULL,
        [UpdatedUtc] datetime2(3) NOT NULL,
        [UpdatedByUserName] nvarchar(256) NULL,
        CONSTRAINT [PK_Bulletin] PRIMARY KEY ([TenantId], [Id]),
        CONSTRAINT [FK_Bulletin_Tenant_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120003814_Bulletin'
)
BEGIN
    CREATE TABLE [app].[BulletinFile] (
        [TenantId] int NOT NULL,
        [Id] int NOT NULL IDENTITY,
        [BulletinId] int NOT NULL,
        [FileName] nvarchar(200) NOT NULL,
        [StoredName] varchar(60) NOT NULL,
        [ContentType] varchar(50) NOT NULL,
        [Size] bigint NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_BulletinFile] PRIMARY KEY ([TenantId], [Id]),
        CONSTRAINT [FK_BulletinFile_Bulletin_TenantId_BulletinId] FOREIGN KEY ([TenantId], [BulletinId]) REFERENCES [app].[Bulletin] ([TenantId], [Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120003814_Bulletin'
)
BEGIN
    CREATE INDEX [IX_Bulletin_TenantId_Date] ON [app].[Bulletin] ([TenantId], [Date]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120003814_Bulletin'
)
BEGIN
    CREATE INDEX [IX_BulletinFile_TenantId_BulletinId] ON [app].[BulletinFile] ([TenantId], [BulletinId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120003814_Bulletin'
)
BEGIN
    EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.Bulletin, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Bulletin AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Bulletin AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.BulletinFile, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.BulletinFile AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.BulletinFile AFTER UPDATE;')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120003814_Bulletin'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261120003814_Bulletin', N'9.0.10');
END;

COMMIT;
GO


-- 확인: 두 표가 보이면 성공
SELECT name FROM sys.tables WHERE name IN (N'Bulletin', N'BulletinFile');
