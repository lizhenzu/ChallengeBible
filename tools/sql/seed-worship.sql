-- seed-worship.sql : 주일학교 부서 예배 안내 넣기(ADR 0116, 2026-11-18 사용자 사진 "주일학교" 표). 사이트 SQL 화면용(시험 실행 → 저장).
-- migrate-worship.sql 을 먼저 실행해야 한다. 이미 예배 안내가 있는 부서는 건너뛴다(여러 번 실행해도 같은 결과).
-- 나중에 바뀌면 부서 관리자가 화면에서 고치거나(화면은 다음에), 아래처럼 UPDATE 한다.
DECLARE @At datetime2(3) = '2026-11-18T13:40:00';
DECLARE @Addr nvarchar(200) = N'서울특별시 송파구 석촌호수로 102';
DECLARE @Rows TABLE (Dept nvarchar(20), Place nvarchar(100));
INSERT @Rows VALUES (N'영아부', N'본당 영아부실(1층)'), (N'유아부', N'본당 유아부실(B1)'), (N'유치부', N'본당 유치부실(B1)'),
                    (N'유년부', N'비전센터 믿음홀(3F)'), (N'초등부', N'비전센터 소망홀(4F)'), (N'중등부', N'비전센터 비전채플(B2)'), (N'고등부', N'비전센터 은혜채플(B1)');
INSERT app.WorshipSchedule (TenantId, SortOrder, Title, DayOfWeek, StartTime, Place, Address, BulletinUrl, BulletinDate, IsActive, UpdatedUtc, UpdatedByUserName)
SELECT t.Id, 1, NULL, 0, '10:00', r.Place, @Addr, NULL, NULL, 1, @At, 'superadmin'
FROM @Rows r JOIN app.Tenant t ON t.Name = N'잠실새내교회 ' + r.Dept
WHERE NOT EXISTS (SELECT 1 FROM app.WorshipSchedule w WHERE w.TenantId = t.Id);
SELECT t.Name AS 부서, w.StartTime AS 시간, w.Place AS 장소, w.Address AS 주소
FROM app.WorshipSchedule w JOIN app.Tenant t ON t.Id = w.TenantId ORDER BY t.Id;
-- 나중에 바꾸기 예) 유년부 장소: UPDATE app.WorshipSchedule SET Place = N'새 장소', UpdatedUtc = '2026-12-01T00:00:00' WHERE TenantId = (SELECT Id FROM app.Tenant WHERE Name = N'잠실새내교회 유년부');
