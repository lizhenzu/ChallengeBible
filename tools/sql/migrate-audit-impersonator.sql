BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026054814_AuditImpersonator'
)
BEGIN
    ALTER TABLE [app].[AuditLog] ADD [ImpersonatorAccountId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026054814_AuditImpersonator'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261026054814_AuditImpersonator', N'9.0.10');
END;

COMMIT;

