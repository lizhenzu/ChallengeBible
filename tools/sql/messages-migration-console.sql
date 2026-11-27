-- 쪽지(ADR 0112) 마이그레이션 JoinListedAll · Messages 를 SQL 실행 화면(/platform/sql)에서 적용하는 용도. 이미 적용됐으면 아무것도 안 한다(이력 확인).
-- 트랜잭션 · 배치 구분 문장은 뺐다(SQL 실행 화면이 트랜잭션을 다룬다). 시험 실행 → 저장(SAVE).
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117025003_JoinListedAll'
)
BEGIN
    UPDATE app.Tenant SET JoinListed = 1 WHERE IsLobby = 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117025003_JoinListedAll'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261117025003_JoinListedAll', N'9.0.10');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117073303_Messages'
)
BEGIN
    CREATE TABLE [app].[Message] (
        [Id] bigint NOT NULL IDENTITY,
        [Kind] varchar(16) NOT NULL,
        [SenderUserName] nvarchar(256) NULL,
        [SenderTenantId] int NULL,
        [SenderLabel] nvarchar(100) NULL,
        [RecipientUserName] nvarchar(256) NULL,
        [Body] nvarchar(1000) NOT NULL,
        [SentUtc] datetime2(3) NOT NULL,
        [ReplyToId] bigint NULL,
        [ReadUtc] datetime2(3) NULL,
        [SenderDeleted] bit NOT NULL,
        [RecipientDeleted] bit NOT NULL,
        [CanceledUtc] datetime2(3) NULL,
        CONSTRAINT [PK_Message] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117073303_Messages'
)
BEGIN
    CREATE TABLE [app].[MessageReceipt] (
        [MessageId] bigint NOT NULL,
        [UserName] nvarchar(256) NOT NULL,
        [ReadUtc] datetime2(3) NULL,
        [DeletedUtc] datetime2(3) NULL,
        CONSTRAINT [PK_MessageReceipt] PRIMARY KEY ([MessageId], [UserName]),
        CONSTRAINT [FK_MessageReceipt_Message_MessageId] FOREIGN KEY ([MessageId]) REFERENCES [app].[Message] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117073303_Messages'
)
BEGIN
    CREATE TABLE [app].[MessageTarget] (
        [MessageId] bigint NOT NULL,
        [TenantId] int NOT NULL,
        CONSTRAINT [PK_MessageTarget] PRIMARY KEY ([MessageId], [TenantId]),
        CONSTRAINT [FK_MessageTarget_Message_MessageId] FOREIGN KEY ([MessageId]) REFERENCES [app].[Message] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_MessageTarget_Tenant_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117073303_Messages'
)
BEGIN
    CREATE INDEX [IX_Message_Kind_SentUtc] ON [app].[Message] ([Kind], [SentUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117073303_Messages'
)
BEGIN
    CREATE INDEX [IX_Message_RecipientUserName_SentUtc] ON [app].[Message] ([RecipientUserName], [SentUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117073303_Messages'
)
BEGIN
    CREATE INDEX [IX_Message_SenderUserName_SentUtc] ON [app].[Message] ([SenderUserName], [SentUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117073303_Messages'
)
BEGIN
    CREATE INDEX [IX_MessageReceipt_UserName] ON [app].[MessageReceipt] ([UserName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117073303_Messages'
)
BEGIN
    CREATE INDEX [IX_MessageTarget_TenantId] ON [app].[MessageTarget] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117073303_Messages'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261117073303_Messages', N'9.0.10');
END;
