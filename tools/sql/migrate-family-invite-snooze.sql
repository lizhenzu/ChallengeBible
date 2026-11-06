BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106224302_FamilyInviteSnooze'
)
BEGIN
    ALTER TABLE [app].[FamilyInvite] ADD [SnoozedOn] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106224302_FamilyInviteSnooze'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261106224302_FamilyInviteSnooze', N'9.0.10');
END;

COMMIT;

