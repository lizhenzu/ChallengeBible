-- migrate-worship.sql : 예배 안내 표 app.WorshipSchedule 만들기 + 부서 격리(RLS) (ADR 0116, 2026-11-18)
-- PC 의 SSMS 로 실행(사이트 SQL 화면은 BEGIN TRANSACTION 을 막는다). 여러 번 실행해도 한 번만 만든다.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118133546_Worship'
)
BEGIN
    CREATE TABLE [app].[WorshipSchedule] (
        [TenantId] int NOT NULL,
        [Id] int NOT NULL IDENTITY,
        [SortOrder] int NOT NULL,
        [Title] nvarchar(50) NULL,
        [DayOfWeek] tinyint NOT NULL,
        [StartTime] time(0) NOT NULL,
        [Place] nvarchar(100) NOT NULL,
        [Address] nvarchar(200) NULL,
        [BulletinUrl] varchar(500) NULL,
        [BulletinDate] date NULL,
        [IsActive] bit NOT NULL,
        [UpdatedUtc] datetime2(3) NOT NULL,
        [UpdatedByUserName] nvarchar(256) NULL,
        CONSTRAINT [PK_WorshipSchedule] PRIMARY KEY ([TenantId], [Id]),
        CONSTRAINT [FK_WorshipSchedule_Tenant_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118133546_Worship'
)
BEGIN
    CREATE INDEX [IX_WorshipSchedule_TenantId_SortOrder] ON [app].[WorshipSchedule] ([TenantId], [SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118133546_Worship'
)
BEGIN
    EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.WorshipSchedule, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.WorshipSchedule AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.WorshipSchedule AFTER UPDATE;')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118133546_Worship'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261118133546_Worship', N'9.0.10');
END;

COMMIT;
GO

