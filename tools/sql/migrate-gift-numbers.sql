BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114070122_GiftNumbers5000'
)
BEGIN

    DECLARE @cur numeric(38, 0) = IDENT_CURRENT('app.GiftItem');
    DECLARE @used bit = CASE WHEN EXISTS (SELECT 1 FROM sys.identity_columns WHERE object_id = OBJECT_ID('app.GiftItem') AND last_value IS NOT NULL) THEN 1 ELSE 0 END;
    IF @cur < 5000 AND (SELECT ISNULL(MAX(Id), 0) FROM app.GiftItem) < 5000
    BEGIN
        IF @used = 1 DBCC CHECKIDENT ('app.GiftItem', RESEED, 4999) WITH NO_INFOMSGS;
        ELSE DBCC CHECKIDENT ('app.GiftItem', RESEED, 5000) WITH NO_INFOMSGS;
    END;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114070122_GiftNumbers5000'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261114070122_GiftNumbers5000', N'9.0.10');
END;

COMMIT;
GO

