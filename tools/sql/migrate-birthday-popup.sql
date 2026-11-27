-- migrate-birthday-popup.sql : 생일 선물 팝업 미루기 날짜 app.UserSetting.BirthdayPopupSnoozedUntil (ADR 0123, 2026-11-20)
-- 여러 번 실행해도 한 번만 만든다.
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120133648_BirthdayPopupSnooze'
)
BEGIN
    ALTER TABLE [app].[UserSetting] ADD [BirthdayPopupSnoozedUntil] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120133648_BirthdayPopupSnooze'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261120133648_BirthdayPopupSnooze', N'9.0.10');
END;

COMMIT;
GO


