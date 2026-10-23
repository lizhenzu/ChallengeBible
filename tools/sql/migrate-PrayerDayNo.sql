BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023215627_PrayerDayNo'
)
BEGIN
    DROP INDEX [UX_DailyPrayer_Date] ON [app].[DailyPrayer];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023215627_PrayerDayNo'
)
BEGIN
    DECLARE @var sysname;
    SELECT @var = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[app].[DailyPrayer]') AND [c].[name] = N'ExposeDate');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [app].[DailyPrayer] DROP CONSTRAINT [' + @var + '];');
    ALTER TABLE [app].[DailyPrayer] ALTER COLUMN [ExposeDate] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023215627_PrayerDayNo'
)
BEGIN
    ALTER TABLE [app].[DailyPrayer] ADD [DayNo] tinyint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023215627_PrayerDayNo'
)
BEGIN
    EXEC sp_set_session_context N'PlatformScope', 1;
    EXEC(N'
    UPDATE app.DailyPrayer SET DayNo = DAY(ExposeDate) WHERE TenantId IS NULL;

    WITH d AS (
        SELECT IsCurrent, Status, ROW_NUMBER() OVER (PARTITION BY DayNo ORDER BY ExposeDate DESC) AS n
        FROM app.DailyPrayer WHERE TenantId IS NULL AND IsCurrent = 1)
    UPDATE d SET IsCurrent = 0, Status = ''Archived'' WHERE n > 1;

    UPDATE app.DailyPrayer SET ExposeDate = NULL WHERE TenantId IS NULL;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023215627_PrayerDayNo'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_DailyPrayer_Date] ON [app].[DailyPrayer] ([TenantId], [ExposeDate]) WHERE [IsCurrent] = 1 AND [ExposeDate] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023215627_PrayerDayNo'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_DailyPrayer_Day] ON [app].[DailyPrayer] ([DayNo]) WHERE [IsCurrent] = 1 AND [TenantId] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023215627_PrayerDayNo'
)
BEGIN
    EXEC(N'ALTER TABLE [app].[DailyPrayer] ADD CONSTRAINT [CK_DailyPrayer_Slot] CHECK ((TenantId IS NULL AND DayNo BETWEEN 1 AND 31 AND ExposeDate IS NULL)
    OR (TenantId IS NOT NULL AND DayNo IS NULL AND ExposeDate IS NOT NULL))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023215627_PrayerDayNo'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261023215627_PrayerDayNo', N'9.0.10');
END;

COMMIT;

