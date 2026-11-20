-- migrate-album-site.sql : 부서 사진첩 표 app.Album · app.Photo + 부서 격리(RLS) (ADR 0118, 2026-11-20)
-- 사이트 SQL 화면용: [저장]을 고르고 확인 글자 SAVE. 여러 번 실행해도 한 번만 만든다.
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120013936_Album'
)
BEGIN
    CREATE TABLE [app].[Album] (
        [TenantId] int NOT NULL,
        [Id] int NOT NULL IDENTITY,
        [Title] nvarchar(100) NOT NULL,
        [Description] nvarchar(2000) NULL,
        [EventDate] date NULL,
        [CoverPhotoId] int NULL,
        [Visibility] varchar(10) NOT NULL,
        [AllowDownload] bit NOT NULL,
        [CreatedUtc] datetime2(3) NOT NULL,
        [CreatedByUserName] nvarchar(256) NULL,
        [UpdatedUtc] datetime2(3) NOT NULL,
        [UpdatedByUserName] nvarchar(256) NULL,
        CONSTRAINT [PK_Album] PRIMARY KEY ([TenantId], [Id]),
        CONSTRAINT [CK_Album_Visibility] CHECK ([Visibility] IN ('Public', 'Login', 'Dept', 'Admin')),
        CONSTRAINT [FK_Album_Tenant_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120013936_Album'
)
BEGIN
    CREATE TABLE [app].[Photo] (
        [TenantId] int NOT NULL,
        [Id] int NOT NULL IDENTITY,
        [AlbumId] int NOT NULL,
        [FileName] nvarchar(200) NOT NULL,
        [StoredName] varchar(60) NOT NULL,
        [ThumbName] varchar(60) NOT NULL,
        [ContentType] varchar(30) NOT NULL,
        [Size] bigint NOT NULL,
        [Width] int NOT NULL,
        [Height] int NOT NULL,
        [Hash] varchar(64) NOT NULL,
        [SortOrder] int NOT NULL,
        [UploadedByUserName] nvarchar(256) NULL,
        [UploadedUtc] datetime2(3) NOT NULL,
        CONSTRAINT [PK_Photo] PRIMARY KEY ([TenantId], [Id]),
        CONSTRAINT [FK_Photo_Album_TenantId_AlbumId] FOREIGN KEY ([TenantId], [AlbumId]) REFERENCES [app].[Album] ([TenantId], [Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120013936_Album'
)
BEGIN
    CREATE INDEX [IX_Album_TenantId_EventDate] ON [app].[Album] ([TenantId], [EventDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120013936_Album'
)
BEGIN
    CREATE INDEX [IX_Photo_TenantId_AlbumId_Hash] ON [app].[Photo] ([TenantId], [AlbumId], [Hash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120013936_Album'
)
BEGIN
    EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.Album, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Album AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Album AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.Photo, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Photo AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Photo AFTER UPDATE;')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120013936_Album'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261120013936_Album', N'9.0.10');
END;

-- 확인: 두 표가 보이면 성공
SELECT name FROM sys.tables WHERE name IN (N'Album', N'Photo');
