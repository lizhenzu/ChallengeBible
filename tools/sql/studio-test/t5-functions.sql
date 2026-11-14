SELECT 't5 ok' AS Result, CAST(0xC8C05CCE6CAD18BC AS nvarchar(100)) AS KoreanText, SYSUTCDATETIME() AS UtcNow,
 (SELECT STRING_AGG(v, ',') FROM (VALUES ('a'), ('b')) x(v)) AS Agg, HASHBYTES('MD5', N'x') AS Hash;
