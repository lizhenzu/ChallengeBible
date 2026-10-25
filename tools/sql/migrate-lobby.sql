BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025014951_LobbyTenant'
)
BEGIN
    ALTER TABLE [app].[Tenant] ADD [IsLobby] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025014951_LobbyTenant'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Tenant_Lobby] ON [app].[Tenant] ([IsLobby]) WHERE [IsLobby] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025014951_LobbyTenant'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261025014951_LobbyTenant', N'9.0.10');
END;

COMMIT;

