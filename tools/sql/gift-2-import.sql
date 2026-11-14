-- gift-2-import.sql : run in the ChallengeBible database (SSMS on the PC) after migration Gift (ADR 0094, R-278, R-279).
-- Puts YuYunBu products into app.GiftItem as common gifts (TenantId NULL) with the same numbers.
-- Review (not shown to children): hidden in YuYunBu, no link, or a link that is not coupang.com.
-- Run with @Commit = 0 first (test run, rolled back), check the tables, then @Commit = 1.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
DECLARE @Commit bit = 0;
DECLARE @Now datetime2(3) = '2026-11-14T00:00:00';   -- written time (UTC); change if you like
CREATE TABLE #Src (ProductID int PRIMARY KEY, ProductName nvarchar(100), ProductDescription nvarchar(500) NULL, UnitPrice int, IsHidden bit, ExternalUrl nvarchar(1000) NULL);

-- ===== paste the "Line" rows from gift-1-export-yuyunbu.sql below this line =====

-- ===== end of pasted rows =====

EXEC sp_set_session_context N'PlatformScope', 1;   -- common gifts are written on this connection only
BEGIN TRY
BEGIN TRANSACTION;
SELECT 'before' AS Step, (SELECT COUNT(*) FROM app.GiftItem) AS GiftItems, (SELECT COUNT(*) FROM #Src) AS Pasted;
IF EXISTS (SELECT 1 FROM app.GiftItem g JOIN #Src s ON s.ProductID = g.Id) THROW 50001, 'Some numbers are already in app.GiftItem. Stop and report.', 1;
IF EXISTS (SELECT 1 FROM #Src WHERE ProductID >= 5000) THROW 50002, 'A YuYunBu number is 5000 or more (new products use 5000+). Stop and report.', 1;

SET IDENTITY_INSERT app.GiftItem ON;
INSERT app.GiftItem (Id, TenantId, SharedByTenantId, Name, Description, Price, CoupangUrl, Status, ReviewReason, HasImage, CreatedUtc, UpdatedUtc, UpdatedByUserName)
SELECT s.ProductID, NULL, NULL, LEFT(LTRIM(RTRIM(s.ProductName)), 100), NULLIF(LTRIM(RTRIM(s.ProductDescription)), N''),
       CASE WHEN s.UnitPrice < 0 THEN 0 ELSE s.UnitPrice END,
       NULLIF(LTRIM(RTRIM(s.ExternalUrl)), N''),
       CASE WHEN x.Reason IS NULL THEN 'Active' ELSE 'Review' END, x.Reason, 1, @Now, @Now, N'yuyunbu-import'
FROM #Src s
CROSS APPLY (SELECT CASE
    WHEN s.IsHidden = 1 THEN N'hidden in YuYunBu'
    WHEN NULLIF(LTRIM(RTRIM(s.ExternalUrl)), N'') IS NULL THEN N'no link'
    WHEN s.ExternalUrl NOT LIKE N'https://%coupang.com/%' AND s.ExternalUrl NOT LIKE N'http://%coupang.com/%' AND s.ExternalUrl NOT LIKE N'https://link.coupang.com/%' THEN N'not coupang'
    END AS Reason) x;
SET IDENTITY_INSERT app.GiftItem OFF;

-- new products start at 5000 (migration GiftNumbers5000, 2026-11-14); YuYunBu numbers keep their own numbers, so no reseed here

SELECT 'after' AS Step, Status, ReviewReason, COUNT(*) AS Items, MIN(Id) AS FirstNo, MAX(Id) AS LastNo
FROM app.GiftItem WHERE UpdatedByUserName = N'yuyunbu-import' GROUP BY Status, ReviewReason ORDER BY Status, ReviewReason;

IF @Commit = 1 BEGIN COMMIT; SELECT 'DONE: saved (real run).' AS Result; END
ELSE BEGIN ROLLBACK; SELECT 'DONE: test run, rolled back. If the numbers look right, set @Commit = 1 and run again.' AS Result; END
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    EXEC sp_set_session_context N'PlatformScope', NULL;
    THROW;
END CATCH;
EXEC sp_set_session_context N'PlatformScope', NULL;
