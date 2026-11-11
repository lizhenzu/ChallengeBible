SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111024916_People'
)
BEGIN
    CREATE TABLE [app].[Person] (
        [TenantId] int NOT NULL,
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(50) NOT NULL,
        [RoleCode] nvarchar(50) NULL,
        [BirthMonth] tinyint NULL,
        [BirthDay] tinyint NULL,
        [Phone1] varchar(20) NULL,
        [Phone2] varchar(20) NULL,
        [UserId] nvarchar(450) NULL,
        [CreatedUtc] datetime2(3) NOT NULL,
        [CreatedByUserName] nvarchar(256) NULL,
        CONSTRAINT [PK_Person] PRIMARY KEY ([TenantId], [Id]),
        CONSTRAINT [FK_Person_Tenant_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111024916_People'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Person_TenantId_UserId] ON [app].[Person] ([TenantId], [UserId]) WHERE [UserId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111024916_People'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Person_UserId] ON [app].[Person] ([UserId]);');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111024916_People'
)
BEGIN
    EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.Person, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Person AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Person AFTER UPDATE;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111024916_People'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261111024916_People', N'9.0.10');
END;

COMMIT;
GO

