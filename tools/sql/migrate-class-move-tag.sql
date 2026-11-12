SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112062612_ClassMoveTag'
)
BEGIN
    ALTER TABLE [app].[Person] ADD [PrevClassName] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112062612_ClassMoveTag'
)
BEGIN
    ALTER TABLE [app].[Person] ADD [PrevDeptName] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112062612_ClassMoveTag'
)
BEGIN
    ALTER TABLE [app].[Person] ADD [PrevTeacherName] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112062612_ClassMoveTag'
)
BEGIN
    ALTER TABLE [app].[Person] ADD [PrevTeacherPhone] varchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112062612_ClassMoveTag'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261112062612_ClassMoveTag', N'9.0.10');
END;

COMMIT;
GO

