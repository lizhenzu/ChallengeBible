BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112211_FamilyJoinRequests'
)
BEGIN
    CREATE TABLE [app].[FamilyJoinRequest] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] int NOT NULL,
        [FamilyTeamId] int NOT NULL,
        [ParticipantId] int NOT NULL,
        [Relation] varchar(20) NOT NULL,
        [CreatedUtc] datetime2(3) NOT NULL,
        CONSTRAINT [PK_FamilyJoinRequest] PRIMARY KEY NONCLUSTERED ([Id]),
        CONSTRAINT [UQ_FamilyJoinRequest_TenantId_Id] UNIQUE CLUSTERED ([TenantId], [Id]),
        CONSTRAINT [CK_FamilyJoinRequest_Relation] CHECK (Relation IN ('Student','Mother','Father','Grandmother','Grandfather','Other')),
        CONSTRAINT [FK_FamilyJoinRequest_Participant] FOREIGN KEY ([TenantId], [ParticipantId]) REFERENCES [app].[Participant] ([TenantId], [Id]),
        CONSTRAINT [FK_FamilyJoinRequest_Team] FOREIGN KEY ([TenantId], [FamilyTeamId]) REFERENCES [app].[FamilyTeam] ([TenantId], [Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112211_FamilyJoinRequests'
)
BEGIN
    CREATE INDEX [IX_FamilyJoinRequest_Team] ON [app].[FamilyJoinRequest] ([TenantId], [FamilyTeamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112211_FamilyJoinRequests'
)
BEGIN
    CREATE UNIQUE INDEX [UX_FamilyJoinRequest_OneTeam] ON [app].[FamilyJoinRequest] ([TenantId], [ParticipantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112211_FamilyJoinRequests'
)
BEGIN
    EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyJoinRequest, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyJoinRequest AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyJoinRequest AFTER UPDATE;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106112211_FamilyJoinRequests'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261106112211_FamilyJoinRequests', N'9.0.10');
END;

COMMIT;

