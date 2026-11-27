-- migrate-birthday-popup-site.sql : 생일 선물 팝업 미루기 날짜 (ADR 0123, 2026-11-20)
-- 사이트 SQL 화면용: [저장]을 고르고 확인 글자 SAVE. 여러 번 실행해도 한 번만 만든다.
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



