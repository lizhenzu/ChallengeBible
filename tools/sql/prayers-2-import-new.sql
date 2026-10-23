/*
  2단계: 새 DB(db_a64b16_challengebible)에서 실행.
  아래 "여기에 붙여 넣기" 자리에 1단계 결과 31줄을 붙여 넣고 전체 실행.
  31건이 다 들어가면 자동 저장(COMMIT), 아니면 자동 취소(ROLLBACK)하고 못 들어간 날을 보여 준다.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
EXEC sp_set_session_context N'PlatformScope', 1;

IF OBJECT_ID('tempdb..#P') IS NOT NULL DROP TABLE #P;
CREATE TABLE #P (
    DayNo int, PrayTitle nvarchar(200), BibleTextToPray nvarchar(200),
    FromJang int, FromZul int, ToJang int, ToZul int,
    BeginMsg nvarchar(200), C1 nvarchar(200), C2 nvarchar(200), C3 nvarchar(200), EndMsg nvarchar(200));

-- ▼▼▼ 여기에 1단계 결과 31줄 붙여 넣기 ▼▼▼


-- ▲▲▲ 여기까지 ▲▲▲

IF (SELECT COUNT(*) FROM #P) <> 31
    THROW 50000, N'#P 에 31줄이 아닙니다. 1단계 결과를 모두 붙여 넣었는지 확인하세요.', 1;

DECLARE @Admin int = (SELECT TOP 1 Id FROM app.Account WHERE IsPlatformAdmin = 1 AND Status = 'Active' ORDER BY Id);

BEGIN TRAN;

INSERT app.DailyPrayer
    (TenantId, DayNo, ExposeDate, Revision, IsCurrent, Status, ReplacesId,
     TraitName, TraitNameEn, Paragraphs,
     BookId, StartChapter, StartVerse, EndChapter, EndVerse, CreatedUtc, CreatedByAccountId)
SELECT NULL, s.DayNo, NULL, 1, 1, 'Published', NULL,
       LEFT(LTRIM(RTRIM(LEFT(s.PrayTitle, CHARINDEX(N'(', s.PrayTitle + N'(') - 1))), 30),                    -- "존중"
       LEFT(NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(SUBSTRING(s.PrayTitle, CHARINDEX(N'(', s.PrayTitle + N'(') + 1, 100), N')', N''), N' ,', N','))), N''), 40),   -- "Respect, Honor"
       p.J,
       b.Id, s.FromJang, s.FromZul, s.ToJang, s.ToZul,
       '2026-10-24T00:00:00', @Admin
FROM #P s
JOIN app.BibleBook b ON b.Name = LTRIM(RTRIM(s.BibleTextToPray)) OR b.ShortName = LTRIM(RTRIM(s.BibleTextToPray))
-- 문단 JSON 배열: Begin → Content1~3 → End, 빈 칸은 뺌, ○○ → {이름}
CROSS APPLY (SELECT N'[' + ISNULL(STUFF((
        SELECT N',"' + REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(v.T)),
                   N'\', N'\\'), N'"', N'\"'), NCHAR(13), N''), NCHAR(10), N'\n'), NCHAR(9), N' '), N'○○', N'{이름}') + N'"'
        FROM (VALUES (1, s.BeginMsg), (2, s.C1), (3, s.C2), (4, s.C3), (5, s.EndMsg)) v(O, T)
        WHERE LTRIM(RTRIM(ISNULL(v.T, N''))) <> N''
        ORDER BY v.O
        FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 1, N''), N'') + N']' AS J) p
WHERE NOT EXISTS (SELECT 1 FROM app.DailyPrayer x WHERE x.TenantId IS NULL AND x.IsCurrent = 1 AND x.DayNo = s.DayNo);

DECLARE @n int = @@ROWCOUNT;
IF @n = 31
BEGIN
    COMMIT;
    PRINT N'성공: 31건 저장';
END
ELSE
BEGIN
    ROLLBACK;
    PRINT CONCAT(N'취소: ', @n, N'건만 맞아서 저장하지 않음. 아래 목록이 못 들어간 날입니다.');
    SELECT s.DayNo, s.PrayTitle, s.BibleTextToPray,
           CASE WHEN b.Id IS NULL THEN N'책 이름을 못 찾음' ELSE N'이미 공용 기도가 있음' END AS Reason
    FROM #P s
    LEFT JOIN app.BibleBook b ON b.Name = LTRIM(RTRIM(s.BibleTextToPray)) OR b.ShortName = LTRIM(RTRIM(s.BibleTextToPray))
    WHERE b.Id IS NULL
       OR EXISTS (SELECT 1 FROM app.DailyPrayer x WHERE x.TenantId IS NULL AND x.IsCurrent = 1 AND x.DayNo = s.DayNo);
END

SELECT DayNo, TraitName, TraitNameEn, Paragraphs
FROM app.DailyPrayer WHERE TenantId IS NULL AND IsCurrent = 1 ORDER BY DayNo;
