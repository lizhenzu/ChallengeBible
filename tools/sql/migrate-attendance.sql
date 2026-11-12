SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112043940_Attendance'
)
BEGIN
    ALTER TABLE [app].[Person] ADD [NewcomerEvangelist] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112043940_Attendance'
)
BEGIN
    ALTER TABLE [app].[Person] ADD [NewcomerFirstVisit] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112043940_Attendance'
)
BEGIN
    ALTER TABLE [app].[Person] ADD [NewcomerVoluntary] bit NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112043940_Attendance'
)
BEGIN
    CREATE TABLE [app].[AttendanceCheck] (
        [TenantId] int NOT NULL,
        [Id] int NOT NULL IDENTITY,
        [PersonId] int NOT NULL,
        [Sunday] date NOT NULL,
        [CreatedUtc] datetime2(3) NOT NULL,
        [CreatedByUserName] nvarchar(256) NULL,
        CONSTRAINT [PK_AttendanceCheck] PRIMARY KEY ([TenantId], [Id]),
        CONSTRAINT [FK_AttendanceCheck_Person_TenantId_PersonId] FOREIGN KEY ([TenantId], [PersonId]) REFERENCES [app].[Person] ([TenantId], [Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112043940_Attendance'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AttendanceCheck_TenantId_PersonId_Sunday] ON [app].[AttendanceCheck] ([TenantId], [PersonId], [Sunday]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112043940_Attendance'
)
BEGIN
    CREATE INDEX [IX_AttendanceCheck_TenantId_Sunday] ON [app].[AttendanceCheck] ([TenantId], [Sunday]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112043940_Attendance'
)
BEGIN
    EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.AttendanceCheck, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.AttendanceCheck AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.AttendanceCheck AFTER UPDATE;')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112043940_Attendance'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261112043940_Attendance', N'9.0.10');
END;

COMMIT;
GO

