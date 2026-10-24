-- 진급 요청 RLS 확인(0030, sec.fn_Promotion). 한 트랜잭션 안에서 시험 행을 넣고 확인한 뒤 되돌린다.
-- 결과 한 줄: 모두 기대값이면 통과. Src=1 Tgt=1 Other=0 Blocked=1 Left=0
-- 부서가 하나뿐이어도 되게, 받는 부서는 시험용 임시 부서를 트랜잭션 안에서 만들고 함께 되돌린다.
SET NOCOUNT ON;
DECLARE @a int, @b int, @acc int, @src int, @tgt int, @other int, @blocked int = 0, @left int, @tmp int;
SELECT TOP 1 @a = Id FROM app.Tenant ORDER BY Id;
SELECT TOP 1 @acc = Id FROM app.Account ORDER BY Id;

BEGIN TRANSACTION;

-- 1) 시스템 연결처럼 넣기: 보낸 부서 @a → 받는 부서 @b(임시)
EXEC sp_set_session_context N'PlatformScope', 1;
INSERT INTO app.Tenant (Slug, Name, JoinCode, TimeZoneId, ThemeColor, MissionEnabled, ReadingEnabled, ReadingBoardPublic, Status, CreatedUtc)
VALUES ('rls-check-tmp', N'RLS 시험 부서', 'RLSZZZ', 'Asia/Seoul', 'blue', 1, 0, 1, 'Active', '2000-01-01');
SET @b = SCOPE_IDENTITY();
INSERT INTO app.PromotionRequest (AccountId, SourceTenantId, TargetTenantId, Status, RequestedUtc, RequestedByAccountId)
VALUES (@acc, @a, @b, 'Cancelled', '2000-01-01', @acc);
EXEC sp_set_session_context N'PlatformScope', NULL;

-- 2) 읽기: 보낸 부서 · 받는 부서는 보이고, 다른 부서(없는 Id 0)는 안 보인다
EXEC sp_set_session_context N'TenantId', @a;
SELECT @src = COUNT(*) FROM app.PromotionRequest WHERE RequestedUtc = '2000-01-01';
EXEC sp_set_session_context N'TenantId', @b;
SELECT @tgt = COUNT(*) FROM app.PromotionRequest WHERE RequestedUtc = '2000-01-01';
EXEC sp_set_session_context N'TenantId', 0;
SELECT @other = COUNT(*) FROM app.PromotionRequest WHERE RequestedUtc = '2000-01-01';

-- 3) 쓰기: 다른 부서(0)로는 @a → @b 요청을 넣을 수 없다(BLOCK)
BEGIN TRY
    INSERT INTO app.PromotionRequest (AccountId, SourceTenantId, TargetTenantId, Status, RequestedUtc, RequestedByAccountId)
    VALUES (@acc, @a, @b, 'Cancelled', '2000-01-02', @acc);
END TRY
BEGIN CATCH
    SET @blocked = 1;
END CATCH;

EXEC sp_set_session_context N'TenantId', NULL;
ROLLBACK TRANSACTION;

-- 4) 흔적 확인(되돌린 뒤 0건이어야 한다)
EXEC sp_set_session_context N'PlatformScope', 1;
SELECT @left = COUNT(*) FROM app.PromotionRequest WHERE RequestedUtc IN ('2000-01-01', '2000-01-02');
SELECT @tmp = COUNT(*) FROM app.Tenant WHERE Slug = 'rls-check-tmp';
EXEC sp_set_session_context N'PlatformScope', NULL;

-- 기대: Src=1 Tgt=1 Other=0 Blocked=1 Left=0 TmpDept=0
SELECT @src AS Src, @tgt AS Tgt, @other AS Other, @blocked AS Blocked, @left AS [Left], @tmp AS TmpDept;
