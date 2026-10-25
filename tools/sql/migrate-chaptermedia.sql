BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024235242_ChapterMedia'
)
BEGIN
    CREATE TABLE [app].[ChapterMedia] (
        [Id] int NOT NULL IDENTITY,
        [BookId] tinyint NOT NULL,
        [Chapter] smallint NOT NULL,
        [Kind] varchar(20) NOT NULL,
        [SortOrder] tinyint NOT NULL,
        [YoutubeVideoId] varchar(20) NOT NULL,
        [Title] nvarchar(200) NULL,
        [DurationSec] int NULL,
        [UpdatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ChapterMedia] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ChapterMedia_Kind] CHECK (Kind IN ('Listen','Overview')),
        CONSTRAINT [FK_ChapterMedia_Book] FOREIGN KEY ([BookId]) REFERENCES [app].[BibleBook] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024235242_ChapterMedia'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_ChapterMedia_Slot] ON [app].[ChapterMedia] ([BookId], [Chapter], [Kind], [SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024235242_ChapterMedia'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261024235242_ChapterMedia', N'9.0.10');
END;

COMMIT;

