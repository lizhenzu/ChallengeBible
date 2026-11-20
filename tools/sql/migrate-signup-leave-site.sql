-- migrate-signup-leave-site.sql : 가입 생일 · 명부 탈퇴한 날 · 탈퇴 신청 표 app.LeaveRequest + 부서 격리(RLS) (ADR 0119, 2026-11-20)
-- 사이트 SQL 화면용: [저장]을 고르고 확인 글자 SAVE. 여러 번 실행해도 한 번만 만든다.
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120033229_SignupLeave'
)
BEGIN
    ALTER TABLE [app].[UserSetting] ADD [BirthDay] tinyint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120033229_SignupLeave'
)
BEGIN
    ALTER TABLE [app].[UserSetting] ADD [BirthMonth] tinyint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120033229_SignupLeave'
)
BEGIN
    ALTER TABLE [app].[Person] ADD [LeftOn] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120033229_SignupLeave'
)
BEGIN
    CREATE TABLE [app].[LeaveRequest] (
        [TenantId] int NOT NULL,
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [UserName] nvarchar(256) NOT NULL,
        [FullName] nvarchar(100) NOT NULL,
        [RoleCode] varchar(20) NULL,
        [RequestedUtc] datetime2(3) NOT NULL,
        CONSTRAINT [PK_LeaveRequest] PRIMARY KEY ([TenantId], [Id]),
        CONSTRAINT [FK_LeaveRequest_Tenant_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [app].[Tenant] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120033229_SignupLeave'
)
BEGIN
    CREATE UNIQUE INDEX [IX_LeaveRequest_UserName] ON [app].[LeaveRequest] ([UserName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120033229_SignupLeave'
)
BEGIN
    EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.LeaveRequest, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.LeaveRequest AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.LeaveRequest AFTER UPDATE;')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120033229_SignupLeave'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261120033229_SignupLeave', N'9.0.10');
END;
