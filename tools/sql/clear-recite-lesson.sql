/*
  공용 암송(TenantId NULL)의 공과 칸 비우기.
  공용 구절은 특정 공과와 상관없으므로 공과 번호 · 요약 · 배울 점 · 이렇게 살아요를 NULL 로 둔다.
  제목(질문)과 구절 범위는 그대로. 부서 암송(TenantId 있음)은 건드리지 않는다.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- 공용 콘텐츠 쓰기는 시스템 범위만 통과(RLS)
EXEC sp_set_session_context N'PlatformScope', 1;

BEGIN TRAN;

UPDATE app.WeeklyVerse
SET LessonNo = NULL, LessonSummary = NULL, LessonPoint = NULL, LessonApply = NULL
WHERE TenantId IS NULL
  AND (LessonNo IS NOT NULL OR LessonSummary IS NOT NULL OR LessonPoint IS NOT NULL OR LessonApply IS NOT NULL);

PRINT CONCAT(N'공과 칸을 비운 공용 암송: ', @@ROWCOUNT, N'건');

COMMIT;

SELECT WeekNo, Title, LessonNo, LessonSummary, LessonPoint, LessonApply
FROM app.WeeklyVerse
WHERE TenantId IS NULL AND IsCurrent = 1
ORDER BY WeekNo;
