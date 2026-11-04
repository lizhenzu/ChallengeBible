BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104072900_WordClips'
)
BEGIN
    CREATE TABLE [app].[DailyWordClip] (
        [Id] int NOT NULL IDENTITY,
        [WordDate] date NOT NULL,
        [Seq] tinyint NOT NULL,
        [Chapter] smallint NOT NULL,
        [YoutubeVideoId] varchar(20) NULL,
        [StartSec] int NULL,
        [EndSec] int NULL,
        [UpdatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_DailyWordClip] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104072900_WordClips'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_DailyWordClip_Slot] ON [app].[DailyWordClip] ([WordDate], [Seq]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104072900_WordClips'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261104072900_WordClips', N'9.0.10');
END;

COMMIT;

