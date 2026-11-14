-- gift-1-export-yuyunbu.sql : run in the YuYunBu database (SSMS on the PC). Read only.
-- Makes one INSERT line per product (dbo.AwardItem). Copy every line of the "Line" column
-- (Results to Text, or select the column and Copy) and paste it into gift-2-import.sql where marked.
SET NOCOUNT ON;
SELECT 'INSERT #Src VALUES (' + CAST(ProductID AS varchar(12)) + ', '
     + 'N''' + REPLACE(ProductName, '''', '''''') + ''', '
     + CASE WHEN ProductDescription IS NULL THEN 'NULL' ELSE 'N''' + REPLACE(ProductDescription, '''', '''''') + '''' END + ', '
     + CAST(UnitPrice AS varchar(12)) + ', '
     + CAST(CAST(ISNULL(IsHidden, 0) AS int) AS varchar(1)) + ', '
     + CASE WHEN ExternalUrl IS NULL THEN 'NULL' ELSE 'N''' + REPLACE(ExternalUrl, '''', '''''') + '''' END
     + ');' AS Line
FROM dbo.AwardItem
ORDER BY ProductID;
SELECT COUNT(*) AS Products, MIN(ProductID) AS FirstNo, MAX(ProductID) AS LastNo FROM dbo.AwardItem;
