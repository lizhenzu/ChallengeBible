/*
  책 개요 옮기기 1단계: 원본(옛 Bible2) DB 에서 실행. 읽기만 한다(아무것도 바꾸지 않음).
  결과 창이 두 개 나온다.
    ① 간략본(dbo.BibleBasicInfo) 66줄 — INSERT #B ...
    ② 상세본(dbo.BibleBookOverview_Detail) 66줄 — INSERT #D ...
  각 결과 창에서 열 제목(Line)을 눌러 모든 줄을 고른 뒤 Ctrl+C →
  2단계 파일(bookoverview-2-import-new.sql)의 표시된 두 자리에 각각 붙여 넣는다.
  글 안의 줄바꿈은 NCHAR(10) 으로 바꿔 한 줄에 담는다(복사할 때 줄바꿈이 사라지지 않게).
*/
SET NOCOUNT ON;

-- ① 간략본: 책 이름 · 개요 · 기록 시기 · 저자 · 기록 목적 · 영상 주소 2개
SELECT N'INSERT #B VALUES ('
     + CAST(Id AS nvarchar(10)) + N', '
     + CASE WHEN BibleText   IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(REPLACE(REPLACE(REPLACE(BibleText,   N'''', N''''''), NCHAR(13) + NCHAR(10), NCHAR(10)), NCHAR(13), NCHAR(10)), NCHAR(10), N''' + NCHAR(10) + N''') + N'''' END + N', '
     + CASE WHEN OverView    IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(REPLACE(REPLACE(REPLACE(OverView,    N'''', N''''''), NCHAR(13) + NCHAR(10), NCHAR(10)), NCHAR(13), NCHAR(10)), NCHAR(10), N''' + NCHAR(10) + N''') + N'''' END + N', '
     + CASE WHEN WhenWritten IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(REPLACE(REPLACE(REPLACE(WhenWritten, N'''', N''''''), NCHAR(13) + NCHAR(10), NCHAR(10)), NCHAR(13), NCHAR(10)), NCHAR(10), N''' + NCHAR(10) + N''') + N'''' END + N', '
     + CASE WHEN Author      IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(REPLACE(REPLACE(REPLACE(Author,      N'''', N''''''), NCHAR(13) + NCHAR(10), NCHAR(10)), NCHAR(13), NCHAR(10)), NCHAR(10), N''' + NCHAR(10) + N''') + N'''' END + N', '
     + CASE WHEN Purpose     IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(REPLACE(REPLACE(REPLACE(Purpose,     N'''', N''''''), NCHAR(13) + NCHAR(10), NCHAR(10)), NCHAR(13), NCHAR(10)), NCHAR(10), N''' + NCHAR(10) + N''') + N'''' END + N', '
     + CASE WHEN SummaryURL1 IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(SummaryURL1, N'''', N'''''') + N'''' END + N', '
     + CASE WHEN SummaryURL2 IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(SummaryURL2, N'''', N'''''') + N'''' END
     + N');' AS Line
FROM dbo.BibleBasicInfo
ORDER BY Id;

-- ② 상세본: 책 이름 · 저자 · 저자 설명 · 기록 연대(부터 · 까지 · 다른 설) · 장소 · 대상 · 시대 · 역사 배경 · 핵심어 · 내용 요약 · 영상 주소 2개
SELECT N'INSERT #D VALUES ('
     + CAST(Id AS nvarchar(10)) + N', '
     + CASE WHEN BookName             IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(REPLACE(REPLACE(REPLACE(BookName,             N'''', N''''''), NCHAR(13) + NCHAR(10), NCHAR(10)), NCHAR(13), NCHAR(10)), NCHAR(10), N''' + NCHAR(10) + N''') + N'''' END + N', '
     + CASE WHEN Author               IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(REPLACE(REPLACE(REPLACE(Author,               N'''', N''''''), NCHAR(13) + NCHAR(10), NCHAR(10)), NCHAR(13), NCHAR(10)), NCHAR(10), N''' + NCHAR(10) + N''') + N'''' END + N', '
     + CASE WHEN AuthorDescription    IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(REPLACE(REPLACE(REPLACE(AuthorDescription,    N'''', N''''''), NCHAR(13) + NCHAR(10), NCHAR(10)), NCHAR(13), NCHAR(10)), NCHAR(10), N''' + NCHAR(10) + N''') + N'''' END + N', '
     + CASE WHEN WrittenDateFrom      IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(WrittenDateFrom, N'''', N'''''') + N'''' END + N', '
     + CASE WHEN WrittenDateTo        IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(WrittenDateTo,   N'''', N'''''') + N'''' END + N', '
     + CASE WHEN AlternativeDate      IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(REPLACE(REPLACE(REPLACE(AlternativeDate,      N'''', N''''''), NCHAR(13) + NCHAR(10), NCHAR(10)), NCHAR(13), NCHAR(10)), NCHAR(10), N''' + NCHAR(10) + N''') + N'''' END + N', '
     + CASE WHEN WrittenPlace         IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(REPLACE(REPLACE(REPLACE(WrittenPlace,         N'''', N''''''), NCHAR(13) + NCHAR(10), NCHAR(10)), NCHAR(13), NCHAR(10)), NCHAR(10), N''' + NCHAR(10) + N''') + N'''' END + N', '
     + CASE WHEN WrittenTarget        IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(REPLACE(REPLACE(REPLACE(WrittenTarget,        N'''', N''''''), NCHAR(13) + NCHAR(10), NCHAR(10)), NCHAR(13), NCHAR(10)), NCHAR(10), N''' + NCHAR(10) + N''') + N'''' END + N', '
     + CASE WHEN HistoricalPeriod     IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(REPLACE(REPLACE(REPLACE(HistoricalPeriod,     N'''', N''''''), NCHAR(13) + NCHAR(10), NCHAR(10)), NCHAR(13), NCHAR(10)), NCHAR(10), N''' + NCHAR(10) + N''') + N'''' END + N', '
     + CASE WHEN HistoricalBackground IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(REPLACE(REPLACE(REPLACE(HistoricalBackground, N'''', N''''''), NCHAR(13) + NCHAR(10), NCHAR(10)), NCHAR(13), NCHAR(10)), NCHAR(10), N''' + NCHAR(10) + N''') + N'''' END + N', '
     + CASE WHEN Keywords             IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(REPLACE(REPLACE(REPLACE(Keywords,             N'''', N''''''), NCHAR(13) + NCHAR(10), NCHAR(10)), NCHAR(13), NCHAR(10)), NCHAR(10), N''' + NCHAR(10) + N''') + N'''' END + N', '
     + CASE WHEN ContentSummary       IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(REPLACE(REPLACE(REPLACE(ContentSummary,       N'''', N''''''), NCHAR(13) + NCHAR(10), NCHAR(10)), NCHAR(13), NCHAR(10)), NCHAR(10), N''' + NCHAR(10) + N''') + N'''' END + N', '
     + CASE WHEN SummaryURL1          IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(SummaryURL1, N'''', N'''''') + N'''' END + N', '
     + CASE WHEN SummaryURL2          IS NULL THEN N'NULL' ELSE N'N''' + REPLACE(SummaryURL2, N'''', N'''''') + N'''' END
     + N');' AS Line
FROM dbo.BibleBookOverview_Detail
ORDER BY Id;
