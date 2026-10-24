/*
  책 개요 옮기기 2단계: 새 DB(ChallengeBible)에서 실행. migrate-bookoverview.sql(표 만들기)을 먼저 실행해 둔다.
  1단계(bookoverview-1-export-old.sql)의 결과 두 개를 아래 표시된 두 자리에 각각 붙여 넣고 전체를 실행한다.
  - 책은 이름으로 맞춘다(띄어쓰기 무시). 맞지 않는 이름이 하나라도 있으면 아무것도 넣지 않고 멈춘다(목록을 보여 줌).
  - 이미 있는 책은 새 내용으로 바꾼다(여러 번 실행해도 같은 결과).
  - 한 트랜잭션: 중간에 오류가 나면 모두 되돌린다.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @at datetime2 = '2026-10-25T00:00:00';   -- 옮긴 날(UTC). DB 의 현재 시각 함수는 쓰지 않는다(CLAUDE.md)

CREATE TABLE #B (Id int, BibleText nvarchar(100), OverView nvarchar(max), WhenWritten nvarchar(200), Author nvarchar(200), Purpose nvarchar(max), Url1 nvarchar(200), Url2 nvarchar(200));
CREATE TABLE #D (Id int, BookName nvarchar(50), Author nvarchar(200), AuthorDescription nvarchar(max), WrittenDateFrom nvarchar(20), WrittenDateTo nvarchar(20),
                 AlternativeDate nvarchar(100), WrittenPlace nvarchar(200), WrittenTarget nvarchar(max), HistoricalPeriod nvarchar(100),
                 HistoricalBackground nvarchar(max), Keywords nvarchar(300), ContentSummary nvarchar(max), Url1 nvarchar(200), Url2 nvarchar(200));

-- ▼▼▼ ① 간략본 결과(INSERT #B ...) 66줄을 여기에 붙여 넣기 ▼▼▼


-- ▲▲▲ ① 여기까지 ▲▲▲

-- ▼▼▼ ② 상세본 결과(INSERT #D ...) 66줄을 여기에 붙여 넣기 ▼▼▼


-- ▲▲▲ ② 여기까지 ▲▲▲

-- 이름 맞추기
SELECT b.Id AS BookId, s.Id AS BId INTO #MB FROM #B s JOIN app.BibleBook b ON REPLACE(b.Name, N' ', N'') = REPLACE(s.BibleText, N' ', N'');
SELECT b.Id AS BookId, s.Id AS DId INTO #MD FROM #D s JOIN app.BibleBook b ON REPLACE(b.Name, N' ', N'') = REPLACE(s.BookName, N' ', N'');

SELECT N'붙여 넣은 줄' AS 확인, (SELECT COUNT(*) FROM #B) AS 간략본, (SELECT COUNT(*) FROM #D) AS 상세본;
SELECT N'간략본 이름 안 맞음' AS 확인, s.Id, s.BibleText FROM #B s WHERE s.Id NOT IN (SELECT BId FROM #MB)
UNION ALL
SELECT N'상세본 이름 안 맞음', s.Id, s.BookName FROM #D s WHERE s.Id NOT IN (SELECT DId FROM #MD);

IF (SELECT COUNT(*) FROM #B) = 0 OR (SELECT COUNT(*) FROM #D) = 0
    THROW 50001, N'붙여 넣은 줄이 없어요. ①과 ② 자리에 1단계 결과를 붙여 넣으세요.', 1;
IF EXISTS (SELECT 1 FROM #B WHERE Id NOT IN (SELECT BId FROM #MB)) OR EXISTS (SELECT 1 FROM #D WHERE Id NOT IN (SELECT DId FROM #MD))
    THROW 50002, N'책 이름이 맞지 않는 줄이 있어요(위 목록). 아무것도 넣지 않았어요.', 1;
IF EXISTS (SELECT BookId FROM #MB GROUP BY BookId HAVING COUNT(*) > 1) OR EXISTS (SELECT BookId FROM #MD GROUP BY BookId HAVING COUNT(*) > 1)
    THROW 50003, N'같은 책이 두 줄 이상 있어요. 아무것도 넣지 않았어요.', 1;

BEGIN TRANSACTION;

MERGE app.BibleBookOverview AS t
USING (
    SELECT bk.Id AS BookId,
           s.Author, s.WhenWritten, s.OverView AS Summary, s.Purpose,
           d.Author AS DetailAuthor, d.AuthorDescription, d.WrittenDateFrom, d.WrittenDateTo, d.AlternativeDate, d.WrittenPlace, d.WrittenTarget,
           d.HistoricalPeriod, d.HistoricalBackground, d.Keywords, d.ContentSummary,
           COALESCE(NULLIF(d.Url1, N''), NULLIF(s.Url1, N'')) AS VideoUrl1,
           COALESCE(NULLIF(d.Url2, N''), NULLIF(s.Url2, N'')) AS VideoUrl2
    FROM app.BibleBook bk
    LEFT JOIN #MB mb ON mb.BookId = bk.Id LEFT JOIN #B s ON s.Id = mb.BId
    LEFT JOIN #MD md ON md.BookId = bk.Id LEFT JOIN #D d ON d.Id = md.DId
    WHERE mb.BookId IS NOT NULL OR md.BookId IS NOT NULL
) AS x ON t.BookId = x.BookId
WHEN MATCHED THEN UPDATE SET
    Author = x.Author, WhenWritten = x.WhenWritten, Summary = x.Summary, Purpose = x.Purpose,
    DetailAuthor = x.DetailAuthor, AuthorDescription = x.AuthorDescription, WrittenFrom = x.WrittenDateFrom, WrittenTo = x.WrittenDateTo,
    AlternativeDate = x.AlternativeDate, WrittenPlace = x.WrittenPlace, WrittenTarget = x.WrittenTarget, HistoricalPeriod = x.HistoricalPeriod,
    HistoricalBackground = x.HistoricalBackground, Keywords = x.Keywords, ContentSummary = x.ContentSummary,
    VideoUrl1 = x.VideoUrl1, VideoUrl2 = x.VideoUrl2, UpdatedUtc = @at
WHEN NOT MATCHED THEN INSERT
    (BookId, Author, WhenWritten, Summary, Purpose, DetailAuthor, AuthorDescription, WrittenFrom, WrittenTo, AlternativeDate, WrittenPlace,
     WrittenTarget, HistoricalPeriod, HistoricalBackground, Keywords, ContentSummary, VideoUrl1, VideoUrl2, UpdatedUtc)
    VALUES
    (x.BookId, x.Author, x.WhenWritten, x.Summary, x.Purpose, x.DetailAuthor, x.AuthorDescription, x.WrittenDateFrom, x.WrittenDateTo, x.AlternativeDate, x.WrittenPlace,
     x.WrittenTarget, x.HistoricalPeriod, x.HistoricalBackground, x.Keywords, x.ContentSummary, x.VideoUrl1, x.VideoUrl2, @at);

COMMIT;

-- 결과 확인: 66권 · 간략본 개요가 있는 책 · 상세본 요약이 있는 책 · 영상 주소가 있는 책
SELECT N'결과' AS 확인, COUNT(*) AS 책,
       SUM(CASE WHEN Summary IS NOT NULL THEN 1 ELSE 0 END) AS 간략본_개요,
       SUM(CASE WHEN ContentSummary IS NOT NULL THEN 1 ELSE 0 END) AS 상세본_요약,
       SUM(CASE WHEN VideoUrl1 IS NOT NULL OR VideoUrl2 IS NOT NULL THEN 1 ELSE 0 END) AS 영상
FROM app.BibleBookOverview;
