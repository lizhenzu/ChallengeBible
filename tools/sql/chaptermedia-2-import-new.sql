/*
  장 듣기 영상 옮기기 2단계: 새 DB(ChallengeBible)에서 실행. migrate-chaptermedia.sql(표 만들기)을 먼저 실행해 둔다.
  1단계(chaptermedia-1-export-old.sql)의 결과를 아래 표시된 자리에 붙여 넣고 전체를 실행한다.
  - 책은 이름으로 맞춘다(띄어쓰기 무시, 옛 "요한1서 · 2서 · 3서" = "요한일서 · 이서 · 삼서"). 이름이 하나라도 안 맞으면 아무것도 넣지 않고 멈춘다.
  - 영상 주소는 유튜브 영상 번호(11자)로 바꿔 넣는다(주소 · 짧은 주소 · embed · shorts · 번호만 모두 받음). 번호를 못 찾은 줄 · 없는 장은 넣지 않고 목록으로 보여 준다.
  - 이 단계가 넣은 "장 듣기"(Kind = Listen) 줄은 모두 지우고 다시 넣는다(여러 번 실행해도 같은 결과).
  - 한 트랜잭션: 중간에 오류가 나면 모두 되돌린다.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @at datetime2 = '2026-10-25T00:00:00';   -- 옮긴 날(UTC). DB 의 현재 시각 함수는 쓰지 않는다(CLAUDE.md)

CREATE TABLE #Y (Id int, BibleText nvarchar(100), Jang int, URL nvarchar(200), TimeMM int, TimeSS int);

-- ▼▼▼ 1단계 결과(INSERT #Y ...)를 여기에 붙여 넣기 ▼▼▼


-- ▲▲▲ 여기까지 ▲▲▲

-- 책 이름 맞추기 · 영상 번호 뽑기
SELECT y.Id, y.BibleText, y.Jang, y.URL, b.Id AS BookId,
       CASE
           WHEN LEN(LTRIM(RTRIM(y.URL))) = 11 THEN LTRIM(RTRIM(y.URL))
           WHEN CHARINDEX(N'v=', y.URL) > 0 THEN SUBSTRING(y.URL, CHARINDEX(N'v=', y.URL) + 2, 11)
           WHEN CHARINDEX(N'youtu.be/', y.URL) > 0 THEN SUBSTRING(y.URL, CHARINDEX(N'youtu.be/', y.URL) + 9, 11)
           WHEN CHARINDEX(N'/embed/', y.URL) > 0 THEN SUBSTRING(y.URL, CHARINDEX(N'/embed/', y.URL) + 7, 11)
           WHEN CHARINDEX(N'/shorts/', y.URL) > 0 THEN SUBSTRING(y.URL, CHARINDEX(N'/shorts/', y.URL) + 8, 11)
       END AS VideoId,
       NULLIF(y.TimeMM * 60 + y.TimeSS, 0) AS DurationSec
INTO #M
FROM #Y y
LEFT JOIN app.BibleBook b
    ON REPLACE(b.Name, N' ', N'') = REPLACE(REPLACE(REPLACE(REPLACE(y.BibleText, N' ', N''), N'1서', N'일서'), N'2서', N'이서'), N'3서', N'삼서');

-- 쓸 수 있는 줄: 책이 맞고, 그 장이 있고, 영상 번호가 11자 영문 · 숫자 · - · _
SELECT m.*, CAST(1 AS bit) AS Ok INTO #OK FROM #M m WHERE 1 = 0;
INSERT #OK
SELECT m.*, CASE WHEN m.BookId IS NOT NULL
                   AND EXISTS (SELECT 1 FROM app.BibleChapter c WHERE c.BookId = m.BookId AND c.Chapter = m.Jang)
                   AND LEN(m.VideoId) = 11
                   AND PATINDEX(N'%[^A-Za-z0-9]%', REPLACE(REPLACE(m.VideoId, N'-', N''), N'_', N'') COLLATE Latin1_General_BIN) = 0   -- - · _ 는 빼고 검사([_-] 는 SQL Server 가 잘못 읽음)
              THEN 1 ELSE 0 END
FROM #M m;

SELECT N'붙여 넣은 줄' AS 확인, COUNT(*) AS 줄, SUM(CASE WHEN Ok = 1 THEN 1 ELSE 0 END) AS 넣을_줄 FROM #OK;
SELECT N'책 이름 안 맞음' AS 확인, Id, BibleText, Jang FROM #OK WHERE BookId IS NULL;
SELECT N'넣지 않는 줄(장 없음 · 영상 번호 못 찾음)' AS 확인, Id, BibleText, Jang, URL FROM #OK WHERE BookId IS NOT NULL AND Ok = 0;

IF (SELECT COUNT(*) FROM #OK) = 0
    THROW 50001, N'붙여 넣은 줄이 없어요. 표시된 자리에 1단계 결과를 붙여 넣으세요.', 1;
IF EXISTS (SELECT 1 FROM #OK WHERE BookId IS NULL)
    THROW 50002, N'책 이름이 맞지 않는 줄이 있어요(위 목록). 아무것도 넣지 않았어요.', 1;

BEGIN TRANSACTION;

DELETE FROM app.ChapterMedia WHERE Kind = 'Listen';

INSERT app.ChapterMedia (BookId, Chapter, Kind, SortOrder, YoutubeVideoId, Title, DurationSec, UpdatedUtc)
SELECT BookId, Jang, 'Listen', ROW_NUMBER() OVER (PARTITION BY BookId, Jang ORDER BY Id), CAST(VideoId AS varchar(20)), NULL, DurationSec, @at
FROM #OK WHERE Ok = 1;

COMMIT;

-- 결과 확인: 장 듣기 줄 · 영상이 있는 장 · 성경 전체 장(1,189)
SELECT N'결과' AS 확인, COUNT(*) AS 장_듣기, COUNT(DISTINCT CAST(BookId AS int) * 1000 + Chapter) AS 영상있는_장,
       (SELECT COUNT(*) FROM app.BibleChapter) AS 전체_장
FROM app.ChapterMedia WHERE Kind = 'Listen';
