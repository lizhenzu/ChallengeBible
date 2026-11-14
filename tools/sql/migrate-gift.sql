BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    CREATE TABLE [app].[GiftCreditRate] (
        [TenantId] int NOT NULL,
        [Credits] int NOT NULL,
        [Amount] int NOT NULL,
        CONSTRAINT [PK_GiftCreditRate] PRIMARY KEY ([TenantId], [Credits]),
        CONSTRAINT [CK_GiftCreditRate_Values] CHECK ([Credits] > 0 AND [Credits] % 1000 = 0 AND [Amount] >= 0),
        CONSTRAINT [FK_GiftCreditRate_Tenant_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    CREATE TABLE [app].[GiftItem] (
        [Id] int NOT NULL IDENTITY(1000, 1),
        [TenantId] int NULL,
        [SharedByTenantId] int NULL,
        [Name] nvarchar(100) NOT NULL,
        [Description] nvarchar(500) NULL,
        [Price] int NOT NULL,
        [CoupangUrl] nvarchar(1000) NULL,
        [Status] varchar(10) NOT NULL,
        [ReviewReason] nvarchar(100) NULL,
        [HasImage] bit NOT NULL,
        [CreatedUtc] datetime2(3) NOT NULL,
        [UpdatedUtc] datetime2(3) NOT NULL,
        [UpdatedByUserName] nvarchar(256) NULL,
        CONSTRAINT [PK_GiftItem] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_GiftItem_Price] CHECK ([Price] >= 0),
        CONSTRAINT [CK_GiftItem_Status] CHECK ([Status] IN ('Active','Review','Retired')),
        CONSTRAINT [FK_GiftItem_Tenant_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    CREATE TABLE [app].[GiftReadingAmount] (
        [TenantId] int NOT NULL,
        [RoundNo] int NOT NULL,
        [Amount] int NOT NULL,
        CONSTRAINT [PK_GiftReadingAmount] PRIMARY KEY ([TenantId], [RoundNo]),
        CONSTRAINT [CK_GiftReadingAmount_Values] CHECK ([RoundNo] >= 1 AND [Amount] >= 0),
        CONSTRAINT [FK_GiftReadingAmount_Tenant_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    CREATE TABLE [app].[GiftSetting] (
        [TenantId] int NOT NULL,
        [PersonalCap] int NULL,
        [NewcomerMin] int NULL,
        [NewcomerMax] int NULL,
        [InviteMin] int NULL,
        [InviteMax] int NULL,
        [BirthdayMin] int NULL,
        [BirthdayMax] int NULL,
        [UpdatedUtc] datetime2(3) NOT NULL,
        [UpdatedByUserName] nvarchar(256) NULL,
        CONSTRAINT [PK_GiftSetting] PRIMARY KEY ([TenantId]),
        CONSTRAINT [FK_GiftSetting_Tenant_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    CREATE TABLE [app].[GiftYearClose] (
        [Year] smallint NOT NULL,
        [ClosedUtc] datetime2(3) NOT NULL,
        [ClosedByUserName] nvarchar(256) NOT NULL,
        CONSTRAINT [PK_GiftYearClose] PRIMARY KEY ([Year])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    CREATE TABLE [app].[TeamAwardArchive] (
        [TenantId] int NOT NULL,
        [Id] int NOT NULL IDENTITY,
        [Year] smallint NOT NULL,
        [AwardId] int NOT NULL,
        [Kind] nvarchar(10) NOT NULL,
        [AwardKey] nvarchar(200) NOT NULL,
        [Threshold] int NOT NULL,
        [AwardedAt] datetime NOT NULL,
        CONSTRAINT [PK_TeamAwardArchive] PRIMARY KEY ([TenantId], [Id]),
        CONSTRAINT [FK_TeamAwardArchive_Tenant_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    CREATE TABLE [app].[Gift] (
        [TenantId] int NOT NULL,
        [Id] bigint NOT NULL IDENTITY,
        [Kind] varchar(10) NOT NULL,
        [Year] smallint NOT NULL,
        [UserName] nvarchar(256) NULL,
        [PersonId] int NULL,
        [FamilyTeamId] int NULL,
        [SourceKey] varchar(40) NOT NULL,
        [State] varchar(10) NOT NULL,
        [OpenFrom] date NULL,
        [OpenUntil] date NULL,
        [MinAmount] int NOT NULL,
        [MaxAmount] int NOT NULL,
        [GiftItemId] int NULL,
        [ItemName] nvarchar(100) NULL,
        [ItemPrice] int NULL,
        [ItemHasImage] bit NULL,
        [CreditsUsed] int NULL,
        [PointsUsed] int NULL,
        [PointsBurned] int NULL,
        [LinkToken] char(32) NULL,
        [PickedUtc] datetime2(3) NULL,
        [PurchasedUtc] datetime2(3) NULL,
        [DeliveredUtc] datetime2(3) NULL,
        [CreatedUtc] datetime2(3) NOT NULL,
        CONSTRAINT [PK_Gift] PRIMARY KEY ([TenantId], [Id]),
        CONSTRAINT [CK_Gift_Amount] CHECK ([MinAmount] >= 0 AND [MaxAmount] >= [MinAmount]),
        CONSTRAINT [CK_Gift_Kind] CHECK ([Kind] IN ('Personal','Newcomer','Invite','Birthday','Reading','Family','Friend')),
        CONSTRAINT [CK_Gift_State] CHECK ([State] IN ('Open','Picked','Purchased','Delivered','Cancelled','Skipped','Expired','YearClosed')),
        CONSTRAINT [CK_Gift_Who] CHECK ([UserName] IS NOT NULL OR [PersonId] IS NOT NULL OR [FamilyTeamId] IS NOT NULL),
        CONSTRAINT [FK_Gift_GiftItem_GiftItemId] FOREIGN KEY ([GiftItemId]) REFERENCES [app].[GiftItem] ([Id]),
        CONSTRAINT [FK_Gift_Tenant_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    CREATE TABLE [app].[GiftItemHidden] (
        [TenantId] int NOT NULL,
        [GiftItemId] int NOT NULL,
        [HiddenUtc] datetime2(3) NOT NULL,
        [HiddenByUserName] nvarchar(256) NULL,
        CONSTRAINT [PK_GiftItemHidden] PRIMARY KEY ([TenantId], [GiftItemId]),
        CONSTRAINT [FK_GiftItemHidden_GiftItem_GiftItemId] FOREIGN KEY ([GiftItemId]) REFERENCES [app].[GiftItem] ([Id]),
        CONSTRAINT [FK_GiftItemHidden_Tenant_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    CREATE TABLE [app].[GiftLog] (
        [TenantId] int NOT NULL,
        [Id] bigint NOT NULL IDENTITY,
        [GiftId] bigint NOT NULL,
        [Action] varchar(12) NOT NULL,
        [ActorUserName] nvarchar(256) NULL,
        [AtUtc] datetime2(3) NOT NULL,
        [ItemName] nvarchar(100) NULL,
        [ItemPrice] int NULL,
        [Detail] nvarchar(200) NULL,
        CONSTRAINT [PK_GiftLog] PRIMARY KEY ([TenantId], [Id]),
        CONSTRAINT [FK_GiftLog_Gift_TenantId_GiftId] FOREIGN KEY ([TenantId], [GiftId]) REFERENCES [app].[Gift] ([TenantId], [Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    CREATE TABLE [app].[GiftPointUse] (
        [TenantId] int NOT NULL,
        [Id] bigint NOT NULL IDENTITY,
        [UserName] nvarchar(256) NOT NULL,
        [Year] smallint NOT NULL,
        [GiftId] bigint NOT NULL,
        [PointsUsed] int NOT NULL,
        [PointsBurned] int NOT NULL,
        [FirstBonus] bit NOT NULL,
        [CreatedUtc] datetime2(3) NOT NULL,
        CONSTRAINT [PK_GiftPointUse] PRIMARY KEY ([TenantId], [Id]),
        CONSTRAINT [CK_GiftPointUse_Points] CHECK ([PointsUsed] >= 0 AND [PointsBurned] >= 0),
        CONSTRAINT [FK_GiftPointUse_Gift_TenantId_GiftId] FOREIGN KEY ([TenantId], [GiftId]) REFERENCES [app].[Gift] ([TenantId], [Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    CREATE INDEX [IX_Gift_GiftItemId] ON [app].[Gift] ([GiftItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    CREATE INDEX [IX_Gift_TenantId_UserName] ON [app].[Gift] ([TenantId], [UserName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    CREATE INDEX [IX_Gift_TenantId_Year_State] ON [app].[Gift] ([TenantId], [Year], [State]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Gift_LinkToken] ON [app].[Gift] ([LinkToken]) WHERE [LinkToken] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Gift_Source] ON [app].[Gift] ([TenantId], [Kind], [UserName], [PersonId], [FamilyTeamId], [SourceKey]) WHERE [UserName] IS NOT NULL AND [PersonId] IS NOT NULL AND [FamilyTeamId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    CREATE INDEX [IX_GiftItem_Status_Price] ON [app].[GiftItem] ([Status], [Price]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    CREATE INDEX [IX_GiftItem_TenantId_Status] ON [app].[GiftItem] ([TenantId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    CREATE INDEX [IX_GiftItemHidden_GiftItemId] ON [app].[GiftItemHidden] ([GiftItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    CREATE INDEX [IX_GiftLog_TenantId_GiftId_AtUtc] ON [app].[GiftLog] ([TenantId], [GiftId], [AtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    CREATE INDEX [IX_GiftPointUse_TenantId_GiftId] ON [app].[GiftPointUse] ([TenantId], [GiftId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    CREATE INDEX [IX_GiftPointUse_TenantId_UserName_Year] ON [app].[GiftPointUse] ([TenantId], [UserName], [Year]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    CREATE INDEX [IX_TeamAwardArchive_TenantId_Year] ON [app].[TeamAwardArchive] ([TenantId], [Year]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_SharedRead(TenantId) ON app.GiftItem, ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.GiftItem AFTER INSERT, ADD BLOCK PREDICATE sec.fn_SharedWrite(TenantId) ON app.GiftItem AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.GiftItemHidden, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftItemHidden AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftItemHidden AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.GiftSetting, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftSetting AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftSetting AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.GiftCreditRate, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftCreditRate AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftCreditRate AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.GiftReadingAmount, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftReadingAmount AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftReadingAmount AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.Gift, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Gift AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Gift AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.GiftLog, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftLog AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftLog AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.GiftPointUse, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftPointUse AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.GiftPointUse AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.TeamAwardArchive, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.TeamAwardArchive AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.TeamAwardArchive AFTER UPDATE;')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114042625_Gift'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261114042625_Gift', N'9.0.10');
END;

COMMIT;
GO

