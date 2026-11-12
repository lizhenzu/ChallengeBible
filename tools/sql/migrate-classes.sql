SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112015215_Classes'
)
BEGIN
    CREATE TABLE [app].[ClassTerm] (
        [TenantId] int NOT NULL,
        [Id] int NOT NULL IDENTITY,
        [Year] int NOT NULL,
        [StartDate] date NOT NULL,
        [CreatedUtc] datetime2(3) NOT NULL,
        CONSTRAINT [PK_ClassTerm] PRIMARY KEY ([TenantId], [Id]),
        CONSTRAINT [FK_ClassTerm_Tenant_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112015215_Classes'
)
BEGIN
    CREATE TABLE [app].[Class] (
        [TenantId] int NOT NULL,
        [Id] int NOT NULL IDENTITY,
        [TermId] int NOT NULL,
        [Name] nvarchar(50) NOT NULL,
        [IsNewcomer] bit NOT NULL,
        [CreatedUtc] datetime2(3) NOT NULL,
        CONSTRAINT [PK_Class] PRIMARY KEY ([TenantId], [Id]),
        CONSTRAINT [FK_Class_ClassTerm_TenantId_TermId] FOREIGN KEY ([TenantId], [TermId]) REFERENCES [app].[ClassTerm] ([TenantId], [Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112015215_Classes'
)
BEGIN
    CREATE TABLE [app].[ClassMember] (
        [TenantId] int NOT NULL,
        [Id] int NOT NULL IDENTITY,
        [TermId] int NOT NULL,
        [ClassId] int NOT NULL,
        [PersonId] int NOT NULL,
        [IsTeacher] bit NOT NULL,
        [CreatedUtc] datetime2(3) NOT NULL,
        CONSTRAINT [PK_ClassMember] PRIMARY KEY ([TenantId], [Id]),
        CONSTRAINT [FK_ClassMember_Class_TenantId_ClassId] FOREIGN KEY ([TenantId], [ClassId]) REFERENCES [app].[Class] ([TenantId], [Id]),
        CONSTRAINT [FK_ClassMember_Person_TenantId_PersonId] FOREIGN KEY ([TenantId], [PersonId]) REFERENCES [app].[Person] ([TenantId], [Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112015215_Classes'
)
BEGIN
    CREATE INDEX [IX_Class_TenantId_TermId] ON [app].[Class] ([TenantId], [TermId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112015215_Classes'
)
BEGIN
    CREATE INDEX [IX_ClassMember_TenantId_ClassId] ON [app].[ClassMember] ([TenantId], [ClassId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112015215_Classes'
)
BEGIN
    CREATE INDEX [IX_ClassMember_TenantId_PersonId] ON [app].[ClassMember] ([TenantId], [PersonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112015215_Classes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ClassMember_TenantId_TermId_PersonId] ON [app].[ClassMember] ([TenantId], [TermId], [PersonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112015215_Classes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ClassTerm_TenantId_Year] ON [app].[ClassTerm] ([TenantId], [Year]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112015215_Classes'
)
BEGIN
    EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.ClassTerm, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.ClassTerm AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.ClassTerm AFTER UPDATE;')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112015215_Classes'
)
BEGIN
    EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.Class, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Class AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Class AFTER UPDATE;')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112015215_Classes'
)
BEGIN
    EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.ClassMember, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.ClassMember AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.ClassMember AFTER UPDATE;')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112015215_Classes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261112015215_Classes', N'9.0.10');
END;

COMMIT;
GO

