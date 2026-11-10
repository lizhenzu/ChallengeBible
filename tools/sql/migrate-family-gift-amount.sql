BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110101916_FamilyGiftAmount'
)
BEGIN
    ALTER TABLE [dbo].[DM2_FamilyMilestoneConfig] ADD [GiftAmount] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110101916_FamilyGiftAmount'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261110101916_FamilyGiftAmount', N'9.0.10');
END;

COMMIT;

