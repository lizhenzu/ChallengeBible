-- migrate-weekly-word-site.sql : 이번 주 말씀 표 app.WeeklyWord + 메인화면 미션북 칸 스위치 Tenant.MissionBookEnabled(유년부만 켬) + 부서 격리(RLS) (ADR 0142, 2026-11-27)
-- 사이트 SQL 화면용: [저장]을 고르고 확인 글자 SAVE. 여러 번 실행해도 한 번만 만든다.
-- 게시(배포)하기 **전에** 먼저 실행한다(표 · 컬럼이 없으면 메인화면이 오류).
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127010000_WeeklyWordMissionBook'
)
BEGIN
    ALTER TABLE [app].[Tenant] ADD [MissionBookEnabled] bit NOT NULL CONSTRAINT [DF_Tenant_MissionBookEnabled] DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127010000_WeeklyWordMissionBook'
)
BEGIN
    CREATE TABLE [app].[WeeklyWord] (
        [TenantId] int NOT NULL,
        [SundayDate] date NOT NULL,
        [Title] nvarchar(60) NOT NULL,
        [Body] nvarchar(4000) NOT NULL,
        [CreatedUtc] datetime2(3) NOT NULL,
        [UpdatedUtc] datetime2(3) NOT NULL,
        [UpdatedByUserName] nvarchar(256) NULL,
        CONSTRAINT [PK_WeeklyWord] PRIMARY KEY ([TenantId], [SundayDate]),
        CONSTRAINT [FK_WeeklyWord_Tenant_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127010000_WeeklyWordMissionBook'
)
BEGIN
    EXEC(N'UPDATE [app].[Tenant] SET [MissionBookEnabled] = 1 WHERE [Name] = N''잠실새내교회 유년부'';');
    EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.WeeklyWord, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.WeeklyWord AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.WeeklyWord AFTER UPDATE;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261127010000_WeeklyWordMissionBook'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261127010000_WeeklyWordMissionBook', N'9.0.10');
END;
