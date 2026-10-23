BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023210159_ReciteWeekNo'
)
BEGIN
    DROP INDEX [UX_WeeklyVerse_Start] ON [app].[WeeklyVerse];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023210159_ReciteWeekNo'
)
BEGIN
    ALTER TABLE [app].[WeeklyVerse] DROP CONSTRAINT [CK_WeeklyVerse_Period];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023210159_ReciteWeekNo'
)
BEGIN
    DECLARE @var sysname;
    SELECT @var = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[app].[WeeklyVerse]') AND [c].[name] = N'StartDate');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [app].[WeeklyVerse] DROP CONSTRAINT [' + @var + '];');
    ALTER TABLE [app].[WeeklyVerse] ALTER COLUMN [StartDate] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023210159_ReciteWeekNo'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[app].[WeeklyVerse]') AND [c].[name] = N'EndDate');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [app].[WeeklyVerse] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [app].[WeeklyVerse] ALTER COLUMN [EndDate] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023210159_ReciteWeekNo'
)
BEGIN
    ALTER TABLE [app].[WeeklyVerse] ADD [WeekNo] tinyint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023210159_ReciteWeekNo'
)
BEGIN
    EXEC sp_set_session_context N'PlatformScope', 1;
    EXEC(N'
    UPDATE v SET WeekNo = DATEDIFF(DAY, f.FirstSunday, v.StartDate) / 7 + 1
    FROM app.WeeklyVerse v
    CROSS APPLY (SELECT DATEFROMPARTS(YEAR(v.StartDate), 1, 1) AS Jan1) j
    CROSS APPLY (SELECT DATEADD(DAY, (7 - DATEDIFF(DAY, ''19000107'', j.Jan1) % 7) % 7, j.Jan1) AS FirstSunday) f
    WHERE v.TenantId IS NULL;

    WITH d AS (
        SELECT IsCurrent, Status, ROW_NUMBER() OVER (PARTITION BY WeekNo ORDER BY StartDate DESC) AS n
        FROM app.WeeklyVerse WHERE TenantId IS NULL AND IsCurrent = 1)
    UPDATE d SET IsCurrent = 0, Status = ''Archived'' WHERE n > 1;

    UPDATE app.WeeklyVerse SET StartDate = NULL, EndDate = NULL WHERE TenantId IS NULL;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023210159_ReciteWeekNo'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_WeeklyVerse_Start] ON [app].[WeeklyVerse] ([TenantId], [StartDate]) WHERE [IsCurrent] = 1 AND [StartDate] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023210159_ReciteWeekNo'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_WeeklyVerse_Week] ON [app].[WeeklyVerse] ([WeekNo]) WHERE [IsCurrent] = 1 AND [TenantId] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023210159_ReciteWeekNo'
)
BEGIN
    EXEC(N'ALTER TABLE [app].[WeeklyVerse] ADD CONSTRAINT [CK_WeeklyVerse_Slot] CHECK ((TenantId IS NULL AND WeekNo BETWEEN 1 AND 55 AND StartDate IS NULL AND EndDate IS NULL)
    OR (TenantId IS NOT NULL AND WeekNo IS NULL AND DATEDIFF(DAY, ''19000107'', StartDate) % 7 = 0 AND EndDate = DATEADD(DAY, 6, StartDate)))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023210159_ReciteWeekNo'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261023210159_ReciteWeekNo', N'9.0.10');
END;

COMMIT;

