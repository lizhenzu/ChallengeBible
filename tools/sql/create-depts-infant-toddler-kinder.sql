-- create-depts-infant-toddler-kinder.sql : 새 부서 3곳 만들기(2026-11-15 사용자 결정)
--   잠실새내교회 영아부 /jamsil-infant       초록
--   잠실새내교회 유아부 /jamsil-toddler      주황
--   잠실새내교회 유치부 /jamsil-kindergarten 보라
--   프로그램 둘 다(도전!!!성경말씀 · 도전!!!성경통독), 시간대 서울, 첫 부서 관리자는 나중에.
-- 화면 [+ 새 부서 만들기](PlatformTenantService.CreateAsync)와 같은 일을 한다: 부서 한 줄 + 참여 코드(6자, 겹치지 않게) + 역할 코드 18줄(dbo.CodeData).
-- 이미 같은 주소의 부서가 있으면 그 부서는 건너뛴다 — 여러 번 실행해도 같은 결과.
-- @Commit = 0 으로 먼저 실행(시험 실행, 되돌림) → 아래 결과 표를 확인 → @Commit = 1 로 다시 실행.
-- PC 의 SSMS 로 실행(SQL Studio 는 세션 설정 문장을 막는다). 역할 코드 표는 보안 조건이 있어 PlatformScope 로 실행한다.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET QUOTED_IDENTIFIER ON; SET NUMERIC_ROUNDABORT OFF;
EXEC sp_set_session_context N'PlatformScope', 1;

DECLARE @Commit bit = 0;
DECLARE @At datetime2(3) = '2026-11-15T01:30:00';   -- 만든 시각(UTC). DB 의 현재 시각 함수는 쓰지 않는다(CLAUDE.md)
DECLARE @Chars varchar(32) = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';   -- 참여 코드 글자(0 · O · 1 · I 빼고, 화면과 같다)

DECLARE @New TABLE (Slug varchar(40), Name nvarchar(100), Color varchar(20), Ord int);
INSERT @New VALUES ('jamsil-infant', N'잠실새내교회 영아부', 'green', 1),
                   ('jamsil-toddler', N'잠실새내교회 유아부', 'red', 2),
                   ('jamsil-kindergarten', N'잠실새내교회 유치부', 'violet', 3);

DECLARE @Roles TABLE (Code varchar(10), Name nvarchar(20), Ord int);
INSERT @Roles VALUES ('T1001', N'학생', 1), ('T1002', N'교사', 2), ('T1003', N'교사(S)', 3), ('T1004', N'지도', 4), ('T1005', N'부장', 5), ('T1006', N'부감', 6),
                     ('T1008', N'서기', 7), ('T1009', N'회계', 8), ('T1010', N'보조', 9), ('T1013', N'지원', 10), ('T1015', N'율동', 11), ('T1016', N'촬영', 12),
                     ('T1017', N'영상', 13), ('T1018', N'반주', 14), ('T1019', N'스텝', 15), ('T1020', N'새친구', 16), ('T1040', N'교장', 17), ('T1099', N'기타', 18);

BEGIN TRANSACTION;

DECLARE @Slug varchar(40), @Name nvarchar(100), @Color varchar(20), @Code varchar(6), @Id int, @Try int;   -- varchar: char(6) 는 빈칸으로 채워져 글자가 붙지 않는다
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT Slug, Name, Color FROM @New ORDER BY Ord;
OPEN c;
FETCH NEXT FROM c INTO @Slug, @Name, @Color;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (SELECT 1 FROM app.Tenant WHERE Slug = @Slug)
    BEGIN
        SET @Try = 0;
        WHILE 1 = 1   -- 겹치지 않는 참여 코드 6자
        BEGIN
            SET @Code = '';
            WHILE LEN(@Code) < 6 SET @Code = @Code + SUBSTRING(@Chars, 1 + ABS(CHECKSUM(NEWID())) % 32, 1);
            IF NOT EXISTS (SELECT 1 FROM app.Tenant WHERE JoinCode = @Code) BREAK;
            SET @Try += 1; IF @Try > 20 THROW 50001, N'참여 코드를 만들지 못했어요. 다시 실행해 주세요.', 1;
        END
        INSERT app.Tenant (Slug, Name, JoinCode, TimeZoneId, ThemeColor, MissionEnabled, ReadingEnabled, ReadingBoardPublic, JoinListed, IsLobby, Status, CreatedUtc)
        VALUES (@Slug, @Name, @Code, 'Asia/Seoul', @Color, 1, 1, 1, 0, 0, 'Active', @At);
        SET @Id = SCOPE_IDENTITY();
        INSERT dbo.CodeData (TenantId, MajorCD, MinorCD, MinorNM, InsertDT, updateDT, displayOrder)
        SELECT @Id, 'T001', Code, Name, @At, @At, Ord FROM @Roles ORDER BY Ord;
    END
    FETCH NEXT FROM c INTO @Slug, @Name, @Color;
END
CLOSE c; DEALLOCATE c;

-- 결과: 부서 · 참여 코드 · 역할 코드 수(18이어야 한다)
SELECT t.Id, t.Name, t.Slug, t.JoinCode AS 참여코드, t.ThemeColor, t.MissionEnabled, t.ReadingEnabled, t.Status,
       (SELECT COUNT(*) FROM dbo.CodeData d WHERE d.TenantId = t.Id AND d.MajorCD = 'T001') AS 역할코드수
FROM app.Tenant t WHERE t.Slug IN (SELECT Slug FROM @New) ORDER BY t.Id;

IF @Commit = 1 COMMIT; ELSE BEGIN ROLLBACK; PRINT N'시험 실행이라 되돌렸어요. 결과를 확인하고 @Commit = 1 로 다시 실행하세요.'; END
