BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024231443_BibleBookOverview'
)
BEGIN
    CREATE TABLE [app].[BibleBookOverview] (
        [BookId] tinyint NOT NULL,
        [Author] nvarchar(200) NULL,
        [WhenWritten] nvarchar(200) NULL,
        [Summary] nvarchar(max) NULL,
        [Purpose] nvarchar(max) NULL,
        [DetailAuthor] nvarchar(200) NULL,
        [AuthorDescription] nvarchar(max) NULL,
        [WrittenFrom] nvarchar(20) NULL,
        [WrittenTo] nvarchar(20) NULL,
        [AlternativeDate] nvarchar(100) NULL,
        [WrittenPlace] nvarchar(200) NULL,
        [WrittenTarget] nvarchar(max) NULL,
        [HistoricalPeriod] nvarchar(100) NULL,
        [HistoricalBackground] nvarchar(max) NULL,
        [Keywords] nvarchar(300) NULL,
        [ContentSummary] nvarchar(max) NULL,
        [VideoUrl1] nvarchar(200) NULL,
        [VideoUrl2] nvarchar(200) NULL,
        [UpdatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_BibleBookOverview] PRIMARY KEY ([BookId]),
        CONSTRAINT [FK_BibleBookOverview_Book] FOREIGN KEY ([BookId]) REFERENCES [app].[BibleBook] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024231443_BibleBookOverview'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261024231443_BibleBookOverview', N'9.0.10');
END;

COMMIT;

