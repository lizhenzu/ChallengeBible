-- 유년부 DB(YuYunBu)에서 실행: 자녀를 위한 365 기도문을 도전!!! 플랫폼으로 옮기는 JSON 만들기(ADR 0106 정한 것 3).
-- 결과(JSON 한 칸)를 복사해서 도전!!! 플랫폼 › 365 기도문 › "진짜 기도문 넣기" 붙여 넣기 칸에 넣고 [넣기].
-- 같은 월일은 바꿔 넣어요(여러 번 넣어도 괜찮아요). 칸: MonthDay · Title · VerseRef · VerseText · Body · Fixes · IsReviewed.
--
-- 한 번에 366편을 뽑으면 결과 칸이 너무 길어 잘릴 수 있어요(SSMS 격자 한 칸 최대 65,535자).
-- 그래서 달마다 나눠 뽑아요: 아래 @Month 를 1 → 12 로 바꿔 가며 12번 실행하고, 매번 결과를 넣어요.

DECLARE @Month INT = 1;   -- ← 1 ~ 12

SELECT (
    SELECT p.MonthDay, p.Title, p.VerseRef, p.VerseText, p.Body, p.Fixes, p.IsReviewed
    FROM dbo.Prayer365 p
    WHERE LEFT(p.MonthDay, 2) = RIGHT('0' + CAST(@Month AS VARCHAR(2)), 2)
      AND LTRIM(RTRIM(p.Title)) <> N'' AND LTRIM(RTRIM(p.Body)) <> N''
    ORDER BY p.MonthDay
    FOR JSON PATH, INCLUDE_NULL_VALUES
) AS Json,
(SELECT COUNT(*) FROM dbo.Prayer365 p WHERE LEFT(p.MonthDay, 2) = RIGHT('0' + CAST(@Month AS VARCHAR(2)), 2) AND LTRIM(RTRIM(p.Title)) <> N'' AND LTRIM(RTRIM(p.Body)) <> N'') AS 편수;

-- 확인용: 달마다 몇 편 있는지(모두 합쳐 366편이면 완전)
-- SELECT LEFT(MonthDay, 2) AS 월, COUNT(*) AS 편수, SUM(CASE WHEN IsReviewed = 1 THEN 1 ELSE 0 END) AS 검수완료
-- FROM dbo.Prayer365 WHERE LTRIM(RTRIM(Title)) <> N'' AND LTRIM(RTRIM(Body)) <> N'' GROUP BY LEFT(MonthDay, 2) ORDER BY 1;
