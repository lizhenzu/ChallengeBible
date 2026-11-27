-- 유년부 DB(YuYunBu)에서 실행: 자녀를 위한 365 기도문을 도전!!! 플랫폼으로 옮기는 JSON 만들기(ADR 0106 정한 것 3).
-- FOR JSON 을 쓰지 않는다(유년부 DB 에서 "Incorrect syntax near INCLUDE_NULL_VALUES" — 버전 · 호환성 수준). 글자를 직접 이어 붙여 JSON 을 만든다.
-- 결과(Json 한 칸)를 복사해서 도전!!! 플랫폼 › 365 기도문 › "진짜 기도문 넣기 · 또는 붙여 넣기" 칸에 넣고 [넣기]. 같은 월일은 바꿔 넣어요.
-- 한 번에 366편이면 결과 칸이 잘릴 수 있어 달마다: @Month 를 1 → 12 로 바꿔 12번.

DECLARE @Month INT = 1;   -- ← 1 ~ 12
DECLARE @mm CHAR(2) = RIGHT('0' + CAST(@Month AS VARCHAR(2)), 2);

SELECT
    N'[' + ISNULL(STUFF((
        SELECT N',{"MonthDay":"' + p.MonthDay + N'"'
             + N',"Title":'     + N'"' + REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(p.Title, N'\', N'\\'), N'"', N'\"'), CHAR(13), N'\r'), CHAR(10), N'\n'), CHAR(9), N'\t') + N'"'
             + N',"VerseRef":'  + CASE WHEN p.VerseRef  IS NULL THEN N'null' ELSE N'"' + REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(p.VerseRef,  N'\', N'\\'), N'"', N'\"'), CHAR(13), N'\r'), CHAR(10), N'\n'), CHAR(9), N'\t') + N'"' END
             + N',"VerseText":' + CASE WHEN p.VerseText IS NULL THEN N'null' ELSE N'"' + REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(p.VerseText, N'\', N'\\'), N'"', N'\"'), CHAR(13), N'\r'), CHAR(10), N'\n'), CHAR(9), N'\t') + N'"' END
             + N',"Body":'      + N'"' + REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(p.Body, N'\', N'\\'), N'"', N'\"'), CHAR(13), N'\r'), CHAR(10), N'\n'), CHAR(9), N'\t') + N'"'
             + N',"Fixes":'     + CASE WHEN p.Fixes     IS NULL THEN N'null' ELSE N'"' + REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(p.Fixes,     N'\', N'\\'), N'"', N'\"'), CHAR(13), N'\r'), CHAR(10), N'\n'), CHAR(9), N'\t') + N'"' END
             + N',"IsReviewed":' + CASE WHEN p.IsReviewed = 1 THEN N'true' ELSE N'false' END
             + N'}'
        FROM dbo.Prayer365 p
        WHERE LEFT(p.MonthDay, 2) = @mm AND LTRIM(RTRIM(p.Title)) <> N'' AND LTRIM(RTRIM(p.Body)) <> N''
        ORDER BY p.MonthDay
        FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 1, N''), N'') + N']' AS Json,
    (SELECT COUNT(*) FROM dbo.Prayer365 p WHERE LEFT(p.MonthDay, 2) = @mm AND LTRIM(RTRIM(p.Title)) <> N'' AND LTRIM(RTRIM(p.Body)) <> N'') AS PrayerCount;

-- 확인용: 달마다 몇 편(모두 합쳐 366편이면 완전)
-- SELECT LEFT(MonthDay, 2) AS MM, COUNT(*) AS Prayers, SUM(CASE WHEN IsReviewed = 1 THEN 1 ELSE 0 END) AS Reviewed
-- FROM dbo.Prayer365 WHERE LTRIM(RTRIM(Title)) <> N'' AND LTRIM(RTRIM(Body)) <> N'' GROUP BY LEFT(MonthDay, 2) ORDER BY 1;
