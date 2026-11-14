CREATE TABLE #T (Id int);
INSERT #T VALUES (1);
SELECT 't2 ok' AS Result, COUNT(*) AS N FROM #T;
