BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025081152_OnFamily'
)
BEGIN
    CREATE TABLE [app].[Family] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(30) NULL,
        [CreatedUtc] datetime2(3) NOT NULL,
        [CreatedByAccountId] int NOT NULL,
        CONSTRAINT [PK_Family] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Family_CreatedBy] FOREIGN KEY ([CreatedByAccountId]) REFERENCES [app].[Account] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025081152_OnFamily'
)
BEGIN
    CREATE TABLE [app].[FamilyLink] (
        [FamilyId] int NOT NULL,
        [AccountId] int NOT NULL,
        [Relation] varchar(20) NOT NULL,
        [SortOrder] tinyint NOT NULL,
        [AddedUtc] datetime2(3) NOT NULL,
        [AddedByAccountId] int NOT NULL,
        CONSTRAINT [PK_FamilyLink] PRIMARY KEY ([FamilyId], [AccountId]),
        CONSTRAINT [CK_FamilyLink_Relation] CHECK (Relation IN ('Student','Mother','Father','Grandmother','Grandfather','Other')),
        CONSTRAINT [FK_FamilyLink_Account] FOREIGN KEY ([AccountId]) REFERENCES [app].[Account] ([Id]),
        CONSTRAINT [FK_FamilyLink_AddedBy] FOREIGN KEY ([AddedByAccountId]) REFERENCES [app].[Account] ([Id]),
        CONSTRAINT [FK_FamilyLink_Family] FOREIGN KEY ([FamilyId]) REFERENCES [app].[Family] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025081152_OnFamily'
)
BEGIN
    CREATE TABLE [app].[FamilyRequest] (
        [Id] int NOT NULL IDENTITY,
        [RequesterAccountId] int NOT NULL,
        [Body] nvarchar(500) NOT NULL,
        [Status] varchar(20) NOT NULL,
        [RequestedUtc] datetime2(3) NOT NULL,
        [DecidedUtc] datetime2(3) NULL,
        [DecidedByAccountId] int NULL,
        [FamilyId] int NULL,
        CONSTRAINT [PK_FamilyRequest] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_FamilyRequest_Status] CHECK (Status IN ('Requested','Approved','Rejected')),
        CONSTRAINT [FK_FamilyRequest_DecidedBy] FOREIGN KEY ([DecidedByAccountId]) REFERENCES [app].[Account] ([Id]),
        CONSTRAINT [FK_FamilyRequest_Family] FOREIGN KEY ([FamilyId]) REFERENCES [app].[Family] ([Id]),
        CONSTRAINT [FK_FamilyRequest_Requester] FOREIGN KEY ([RequesterAccountId]) REFERENCES [app].[Account] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025081152_OnFamily'
)
BEGIN
    CREATE INDEX [IX_Family_CreatedByAccountId] ON [app].[Family] ([CreatedByAccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025081152_OnFamily'
)
BEGIN
    CREATE INDEX [IX_FamilyLink_AddedByAccountId] ON [app].[FamilyLink] ([AddedByAccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025081152_OnFamily'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_FamilyLink_Account] ON [app].[FamilyLink] ([AccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025081152_OnFamily'
)
BEGIN
    CREATE INDEX [IX_FamilyRequest_DecidedByAccountId] ON [app].[FamilyRequest] ([DecidedByAccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025081152_OnFamily'
)
BEGIN
    CREATE INDEX [IX_FamilyRequest_FamilyId] ON [app].[FamilyRequest] ([FamilyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025081152_OnFamily'
)
BEGIN
    CREATE INDEX [IX_FamilyRequest_Status] ON [app].[FamilyRequest] ([Status], [RequestedUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025081152_OnFamily'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_FamilyRequest_Open] ON [app].[FamilyRequest] ([RequesterAccountId]) WHERE [Status] = ''Requested''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025081152_OnFamily'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261025081152_OnFamily', N'9.0.10');
END;

COMMIT;

