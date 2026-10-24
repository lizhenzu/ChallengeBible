BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024031017_Promotion'
)
BEGIN
    CREATE TABLE [app].[AuditLog] (
        [Id] bigint NOT NULL IDENTITY,
        [OccurredUtc] datetime2(3) NOT NULL,
        [ActorAccountId] int NOT NULL,
        [Action] varchar(40) NOT NULL,
        [TenantId] int NULL,
        [Detail] nvarchar(400) NULL,
        CONSTRAINT [PK_AuditLog] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AuditLog_Actor] FOREIGN KEY ([ActorAccountId]) REFERENCES [app].[Account] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024031017_Promotion'
)
BEGIN
    CREATE TABLE [app].[PromotionRequest] (
        [Id] int NOT NULL IDENTITY,
        [AccountId] int NOT NULL,
        [SourceTenantId] int NOT NULL,
        [TargetTenantId] int NOT NULL,
        [Status] varchar(20) NOT NULL,
        [RequestedUtc] datetime2(3) NOT NULL,
        [RequestedByAccountId] int NOT NULL,
        [DecidedUtc] datetime2(3) NULL,
        [DecidedByAccountId] int NULL,
        CONSTRAINT [PK_PromotionRequest] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PromotionRequest_Status] CHECK (Status IN ('Pending','Approved','Rejected','Cancelled')),
        CONSTRAINT [CK_PromotionRequest_Tenants] CHECK (SourceTenantId <> TargetTenantId),
        CONSTRAINT [FK_PromotionRequest_Account] FOREIGN KEY ([AccountId]) REFERENCES [app].[Account] ([Id]),
        CONSTRAINT [FK_PromotionRequest_DecidedBy] FOREIGN KEY ([DecidedByAccountId]) REFERENCES [app].[Account] ([Id]),
        CONSTRAINT [FK_PromotionRequest_RequestedBy] FOREIGN KEY ([RequestedByAccountId]) REFERENCES [app].[Account] ([Id]),
        CONSTRAINT [FK_PromotionRequest_Source] FOREIGN KEY ([SourceTenantId]) REFERENCES [app].[Tenant] ([Id]),
        CONSTRAINT [FK_PromotionRequest_Target] FOREIGN KEY ([TargetTenantId]) REFERENCES [app].[Tenant] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024031017_Promotion'
)
BEGIN
    CREATE INDEX [IX_AuditLog_ActorAccountId] ON [app].[AuditLog] ([ActorAccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024031017_Promotion'
)
BEGIN
    CREATE INDEX [IX_AuditLog_Occurred] ON [app].[AuditLog] ([OccurredUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024031017_Promotion'
)
BEGIN
    CREATE INDEX [IX_PromotionRequest_DecidedByAccountId] ON [app].[PromotionRequest] ([DecidedByAccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024031017_Promotion'
)
BEGIN
    CREATE INDEX [IX_PromotionRequest_RequestedByAccountId] ON [app].[PromotionRequest] ([RequestedByAccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024031017_Promotion'
)
BEGIN
    CREATE INDEX [IX_PromotionRequest_Source] ON [app].[PromotionRequest] ([SourceTenantId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024031017_Promotion'
)
BEGIN
    CREATE INDEX [IX_PromotionRequest_Target] ON [app].[PromotionRequest] ([TargetTenantId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024031017_Promotion'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UQ_PromotionRequest_Pending] ON [app].[PromotionRequest] ([AccountId], [SourceTenantId]) WHERE [Status] = ''Pending''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024031017_Promotion'
)
BEGIN
    EXEC(N'CREATE FUNCTION sec.fn_Promotion(@SourceTenantId int, @TargetTenantId int)
    RETURNS TABLE WITH SCHEMABINDING AS
    RETURN SELECT 1 AS ok
    WHERE @SourceTenantId = CAST(SESSION_CONTEXT(N''TenantId'') AS int)
       OR @TargetTenantId = CAST(SESSION_CONTEXT(N''TenantId'') AS int)
       OR CAST(SESSION_CONTEXT(N''PlatformScope'') AS int) = 1;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024031017_Promotion'
)
BEGIN
    EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation
        ADD FILTER PREDICATE sec.fn_Promotion(SourceTenantId, TargetTenantId) ON app.PromotionRequest,
        ADD BLOCK PREDICATE sec.fn_Promotion(SourceTenantId, TargetTenantId) ON app.PromotionRequest AFTER INSERT,
        ADD BLOCK PREDICATE sec.fn_Promotion(SourceTenantId, TargetTenantId) ON app.PromotionRequest AFTER UPDATE;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024031017_Promotion'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261024031017_Promotion', N'9.0.10');
END;

COMMIT;

