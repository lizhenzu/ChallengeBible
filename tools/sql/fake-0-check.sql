-- fake-0-check.sql : read-only check before fake-create.sql (2026-11-14). Changes nothing (rolled back).
-- Run the whole file. Send back every result table.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
ALTER SECURITY POLICY sec.TenantIsolation WITH (STATE = OFF);

-- A. current data (did the last run change anything?)
SELECT 'A' AS Step,
       (SELECT COUNT(*) FROM [Identity].[User]) AS Accounts,
       (SELECT COUNT(*) FROM app.Person) AS Roster,
       (SELECT COUNT(*) FROM dbo.DM2_MissionActivity) AS MissionRows,
       (SELECT COUNT(*) FROM app.Tenant WHERE IsLobby = 0) AS Depts,
       (SELECT COUNT(*) FROM app.BibleChapter) AS BibleChapters,
       (SELECT COUNT(*) FROM app.PlatformRuleSetVersion) AS RuleVersions;

-- B. temp table + loop
CREATE TABLE #T (Id int PRIMARY KEY, Slug nvarchar(200));
DECLARE @Id int = (SELECT MIN(Id) FROM app.Tenant WHERE IsLobby = 0), @N int = 0;
WHILE @Id IS NOT NULL
BEGIN
    INSERT #T SELECT Id, Slug FROM app.Tenant WHERE Id = @Id;
    SET @N += 1;
    SET @Id = (SELECT MIN(Id) FROM app.Tenant WHERE IsLobby = 0 AND Id > @Id);
END;
SELECT 'B' AS Step, @N AS DeptsByLoop, (SELECT STRING_AGG(Slug, ', ') FROM #T) AS Slugs;

-- C. Korean text from code (should show a Korean word)
SELECT 'C' AS Step, CAST(0xC8C05CCE6CAD18BC AS nvarchar(100)) AS KoreanText;

-- D. rule text (which mission items are off)
DECLARE @Rule nvarchar(max) = (SELECT TOP 1 [Values] FROM app.PlatformRuleSetVersion ORDER BY VersionNo DESC);
SELECT 'D' AS Step, CASE WHEN @Rule IS NULL THEN 'no rule rows' ELSE
       (SELECT STRING_AGG(v.J + ':' + CASE WHEN REPLACE(@Rule, ' ', '') LIKE '%"Item":"' + v.J + '","Enabled":false%' THEN 'off' ELSE 'on' END, ', ')
        FROM (VALUES ('WordMe'), ('WordParent'), ('PrayerMe'), ('PrayerOthers'), ('QuizCorrect'), ('Recite')) v(J)) END AS Items;

-- E. speed: 50,000 rows into a temp table
DECLARE @t0 datetime2(3) = SYSUTCDATETIME();
CREATE TABLE #Big (I int, D date, S nvarchar(50));
INSERT #Big SELECT a.v + 10 * b.v + 100 * c.v + 1000 * d.v + 10000 * e.v, '2026-01-01', N'x'
FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) a(v) CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) b(v)
CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) c(v) CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) d(v)
CROSS JOIN (VALUES (0),(1),(2),(3),(4)) e(v);
SELECT 'E' AS Step, (SELECT COUNT(*) FROM #Big) AS RowsMade, DATEDIFF(millisecond, @t0, SYSUTCDATETIME()) AS Ms;

ALTER SECURITY POLICY sec.TenantIsolation WITH (STATE = ON);
ROLLBACK;
SELECT 'DONE' AS Step, 'nothing changed' AS Result;
