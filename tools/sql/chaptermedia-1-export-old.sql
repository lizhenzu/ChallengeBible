/*
  장 듣기 영상 옮기기 1단계: 원본(옛 Bible2) DB 에서 실행. 읽기만 한다(아무것도 바꾸지 않음).
  결과(INSERT #Y ...) 약 1,189줄을 모두 고른 뒤 Ctrl+C →
  2단계 파일(chaptermedia-2-import-new.sql)의 표시된 자리에 붙여 넣는다.
  복사 방법: 결과(표) 창에서 열 제목(Line)을 눌러 모든 줄을 고른 뒤 Ctrl+C.
  TimeMM · TimeSS 는 영상 길이다(시작 위치 아님 — 옛 화면도 처음부터 재생).
*/
SET NOCOUNT ON;

SELECT N'INSERT #Y VALUES ('
     + CAST(Id AS nvarchar(10)) + N', '
     + N'N''' + REPLACE(BibleText, N'''', N'''''') + N''', '
     + CAST(Jang AS nvarchar(10)) + N', '
     + N'N''' + REPLACE(REPLACE(REPLACE(URL, N'''', N''''''), NCHAR(13), N''), NCHAR(10), N'') + N''', '
     + CAST(TimeMM AS nvarchar(10)) + N', '
     + CAST(TimeSS AS nvarchar(10))
     + N');' AS Line
FROM dbo.eBibleYouTuBu
ORDER BY Id;
