BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031003613_ReadingTheme'
)
BEGIN
    ALTER TABLE [app].[Account] ADD [ReadingTheme] varchar(16) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031003613_ReadingTheme'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261031003613_ReadingTheme', N'9.0.10');
END;

COMMIT;

