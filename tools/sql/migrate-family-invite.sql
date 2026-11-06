BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106214508_FamilyInvites'
)
BEGIN
    CREATE TABLE [app].[FamilyInvite] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] int NOT NULL,
        [FamilyTeamId] int NOT NULL,
        [ParticipantId] int NOT NULL,
        [InvitedByParticipantId] int NOT NULL,
        [Relation] varchar(20) NOT NULL,
        [CreatedUtc] datetime2(3) NOT NULL,
        CONSTRAINT [PK_FamilyInvite] PRIMARY KEY NONCLUSTERED ([Id]),
        CONSTRAINT [UQ_FamilyInvite_TenantId_Id] UNIQUE CLUSTERED ([TenantId], [Id]),
        CONSTRAINT [CK_FamilyInvite_Relation] CHECK (Relation IN ('Student','Mother','Father','Grandmother','Grandfather','Other')),
        CONSTRAINT [FK_FamilyInvite_InvitedBy] FOREIGN KEY ([TenantId], [InvitedByParticipantId]) REFERENCES [app].[Participant] ([TenantId], [Id]),
        CONSTRAINT [FK_FamilyInvite_Participant] FOREIGN KEY ([TenantId], [ParticipantId]) REFERENCES [app].[Participant] ([TenantId], [Id]),
        CONSTRAINT [FK_FamilyInvite_Team] FOREIGN KEY ([TenantId], [FamilyTeamId]) REFERENCES [app].[FamilyTeam] ([TenantId], [Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106214508_FamilyInvites'
)
BEGIN
    CREATE INDEX [IX_FamilyInvite_InvitedBy] ON [app].[FamilyInvite] ([TenantId], [InvitedByParticipantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106214508_FamilyInvites'
)
BEGIN
    CREATE INDEX [IX_FamilyInvite_Participant] ON [app].[FamilyInvite] ([TenantId], [ParticipantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106214508_FamilyInvites'
)
BEGIN
    CREATE UNIQUE INDEX [UX_FamilyInvite_TeamParticipant] ON [app].[FamilyInvite] ([TenantId], [FamilyTeamId], [ParticipantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106214508_FamilyInvites'
)
BEGIN
    EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyInvite, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyInvite AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.FamilyInvite AFTER UPDATE;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106214508_FamilyInvites'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261106214508_FamilyInvites', N'9.0.10');
END;

COMMIT;

