BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102125312_TenantMilestones'
)
BEGIN
    ALTER TABLE [app].[Tenant] ADD [Milestones] varchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102125312_TenantMilestones'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261102125312_TenantMilestones', N'9.0.10');
END;

COMMIT;

