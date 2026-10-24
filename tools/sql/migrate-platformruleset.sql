BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065209_PlatformRuleSet'
)
BEGIN
    CREATE TABLE [app].[PlatformRuleSetVersion] (
        [Id] int NOT NULL IDENTITY,
        [VersionNo] int NOT NULL,
        [Values] nvarchar(max) NOT NULL,
        [EffectiveFrom] date NOT NULL,
        [ChangedUtc] datetime2(3) NOT NULL,
        [ChangedByAccountId] int NOT NULL,
        CONSTRAINT [PK_PlatformRuleSetVersion] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PlatformRuleSetVersion_Values] CHECK (ISJSON([Values]) = 1),
        CONSTRAINT [FK_PlatformRuleSetVersion_Account] FOREIGN KEY ([ChangedByAccountId]) REFERENCES [app].[Account] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065209_PlatformRuleSet'
)
BEGIN
    CREATE INDEX [IX_PlatformRuleSetVersion_ChangedByAccountId] ON [app].[PlatformRuleSetVersion] ([ChangedByAccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065209_PlatformRuleSet'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_PlatformRuleSetVersion_No] ON [app].[PlatformRuleSetVersion] ([VersionNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065209_PlatformRuleSet'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261024065209_PlatformRuleSet', N'9.0.10');
END;

COMMIT;

