/*
  1단계: 원본(옛) DB 에서 실행.
  결과로 나오는 31줄(INSERT 문)을 모두 복사해서 2단계 파일(prayers-2-import-new.sql)의 표시된 자리에 붙여 넣는다.
  복사 방법: 결과(표) 창에서 열 제목을 눌러 31줄을 모두 고른 뒤 Ctrl+C.
*/
SET NOCOUNT ON;

SELECT N'INSERT #P VALUES ('
     + CAST(DayNo AS nvarchar(10)) + N', '
     + CASE WHEN PrayTitle            IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(PrayTitle,            N'''', N'''''') + N'''' END + N', '
     + CASE WHEN BibleTextToPray      IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(BibleTextToPray,      N'''', N'''''') + N'''' END + N', '
     + CAST(BibleTextToPrayFromJang AS nvarchar(10)) + N', '
     + CAST(BibleTextToPrayFromzul  AS nvarchar(10)) + N', '
     + CAST(BibleTextToPrayToJang   AS nvarchar(10)) + N', '
     + CAST(BibleTextToPrayTozul    AS nvarchar(10)) + N', '
     + CASE WHEN PraryBeginMesssage   IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(PraryBeginMesssage,   N'''', N'''''') + N'''' END + N', '
     + CASE WHEN PrayContent1         IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(PrayContent1,         N'''', N'''''') + N'''' END + N', '
     + CASE WHEN PrayContent2         IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(PrayContent2,         N'''', N'''''') + N'''' END + N', '
     + CASE WHEN PrayContent3         IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(PrayContent3,         N'''', N'''''') + N'''' END + N', '
     + CASE WHEN PraryEndMesssage     IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(PraryEndMesssage,     N'''', N'''''') + N'''' END
     + N');' AS Line
FROM dbo.PrayPhraseTemplate
ORDER BY DayNo;
