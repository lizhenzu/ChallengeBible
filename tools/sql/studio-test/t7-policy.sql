BEGIN TRANSACTION;
ALTER SECURITY POLICY sec.TenantIsolation WITH (STATE = OFF);
SELECT 't7 ok' AS Result, (SELECT COUNT(*) FROM app.Person) AS Roster, (SELECT COUNT(*) FROM dbo.DM2_MissionActivity) AS MissionRows;
ALTER SECURITY POLICY sec.TenantIsolation WITH (STATE = ON);
ROLLBACK;
