BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113034723_ReportOrg'
)
BEGIN
    ALTER TABLE [app].[Tenant] ADD [ReportOrg] nvarchar(40) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113034723_ReportOrg'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261113034723_ReportOrg', N'9.0.10');
END;

COMMIT;
