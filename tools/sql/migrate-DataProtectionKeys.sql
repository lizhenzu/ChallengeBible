BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023224453_DataProtectionKeys'
)
BEGIN
    CREATE TABLE [app].[DataProtectionKey] (
        [Id] int NOT NULL IDENTITY,
        [FriendlyName] nvarchar(100) NULL,
        [Xml] nvarchar(max) NULL,
        CONSTRAINT [PK_DataProtectionKey] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023224453_DataProtectionKeys'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261023224453_DataProtectionKeys', N'9.0.10');
END;

COMMIT;

