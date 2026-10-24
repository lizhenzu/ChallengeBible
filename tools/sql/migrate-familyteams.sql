BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024081107_FamilyTeams'
)
BEGIN
    CREATE TABLE [app].[FamilyTeam] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] int NOT NULL,
        [Name] nvarchar(50) NULL,
        [Memo] nvarchar(500) NULL,
        [IsActive] bit NOT NULL,
        [CreatedUtc] datetime2(3) NOT NULL,
        CONSTRAINT [PK_FamilyTeam] PRIMARY KEY NONCLUSTERED ([Id]),
        CONSTRAINT [UQ_FamilyTeam_TenantId_Id] UNIQUE CLUSTERED ([TenantId], [Id]),
        CONSTRAINT [FK_FamilyTeam_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024081107_FamilyTeams'
)
BEGIN
    CREATE TABLE [app].[FamilyMember] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] int NOT NULL,
        [FamilyTeamId] int NOT NULL,
        [ParticipantId] int NOT NULL,
        [Relation] varchar(20) NOT NULL,
        [SortOrder] tinyint NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_FamilyMember] PRIMARY KEY NONCLUSTERED ([Id]),
        CONSTRAINT [UQ_FamilyMember_TenantId_Id] UNIQUE CLUSTERED ([TenantId], [Id]),
        CONSTRAINT [CK_FamilyMember_Relation] CHECK (Relation IN ('Student','Mother','Father','Grandmother','Grandfather','Other')),
        CONSTRAINT [FK_FamilyMember_Participant] FOREIGN KEY ([TenantId], [ParticipantId]) REFERENCES [app].[Participant] ([TenantId], [Id]),
        CONSTRAINT [FK_FamilyMember_Team] FOREIGN KEY ([TenantId], [FamilyTeamId]) REFERENCES [app].[FamilyTeam] ([TenantId], [Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024081107_FamilyTeams'
)
BEGIN
    CREATE TABLE [app].[FamilyMilestoneDismiss] (
        [TenantId] int NOT NULL,
        [FamilyTeamId] int NOT NULL,
        [Year] smallint NOT NULL,
        [Threshold] int NOT NULL,
        [DismissedUtc] datetime2(3) NOT NULL,
        [DismissedByAccountId] int NOT NULL,
        CONSTRAINT [PK_FamilyMilestoneDismiss] PRIMARY KEY ([TenantId], [FamilyTeamId], [Year], [Threshold]),
        CONSTRAINT [FK_FamilyMilestoneDismiss_Account] FOREIGN KEY ([DismissedByAccountId]) REFERENCES [app].[Account] ([Id]),
        CONSTRAINT [FK_FamilyMilestoneDismiss_Team] FOREIGN KEY ([TenantId], [FamilyTeamId]) REFERENCES [app].[FamilyTeam] ([TenantId], [Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024081107_FamilyTeams'
)
BEGIN
    CREATE TABLE [app].[FamilyMilestoneReached] (
        [TenantId] int NOT NULL,
        [FamilyTeamId] int NOT NULL,
        [Year] smallint NOT NULL,
        [Threshold] int NOT NULL,
        [ReachedUtc] datetime2(3) NOT NULL,
        CONSTRAINT [PK_FamilyMilestoneReached] PRIMARY KEY ([TenantId], [FamilyTeamId], [Year], [Threshold]),
        CONSTRAINT [FK_FamilyMilestoneReached_Team] FOREIGN KEY ([TenantId], [FamilyTeamId]) REFERENCES [app].[FamilyTeam] ([TenantId], [Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024081107_FamilyTeams'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_FamilyMember_TeamParticipant] ON [app].[FamilyMember] ([TenantId], [FamilyTeamId], [ParticipantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024081107_FamilyTeams'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_FamilyMember_OneActiveTeam] ON [app].[FamilyMember] ([TenantId], [ParticipantId]) WHERE [IsActive] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024081107_FamilyTeams'
)
BEGIN
    CREATE INDEX [IX_FamilyMilestoneDismiss_DismissedByAccountId] ON [app].[FamilyMilestoneDismiss] ([DismissedByAccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024081107_FamilyTeams'
)
BEGIN
    EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyTeam, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyTeam AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyTeam AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyMember, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyMember AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyMember AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyMilestoneReached, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyMilestoneReached AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyMilestoneReached AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyMilestoneDismiss, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyMilestoneDismiss AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyMilestoneDismiss AFTER UPDATE;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024081107_FamilyTeams'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261024081107_FamilyTeams', N'9.0.10');
END;

COMMIT;

