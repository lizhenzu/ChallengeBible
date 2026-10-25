/*
  임시 부서 "처음 오신 분"(ADR 0064) 만들기 + 부서 없는 기존 계정 넣기. 새 DB 에서 migrate-lobby.sql 다음에 실행.
  - 임시 부서는 하나뿐(주소 /welcome). 이미 있으면 다시 만들지 않는다.
  - 넣는 계정: 사용 중(Active)이고, 플랫폼 관리자가 아니고, 운영 중인 진짜 부서에 승인된 소속이 하나도 없는 계정
    (신청만 하고 기다리는 사람 포함). 이미 임시 부서에 있으면 건너뛴다 — 여러 번 실행해도 같은 결과.
  - 한 트랜잭션. RLS 표(소속 · 역할 · 참여자)에 쓰므로 PlatformScope 로 실행한다.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
EXEC sp_set_session_context N'PlatformScope', 1;

DECLARE @at datetime2(3) = '2026-10-25T00:00:00';   -- 넣은 날(UTC). DB 의 현재 시각 함수는 쓰지 않는다(CLAUDE.md)

BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM app.Tenant WHERE IsLobby = 1)
    INSERT app.Tenant (Slug, Name, JoinCode, TimeZoneId, ThemeColor, MissionEnabled, ReadingEnabled, ReadingBoardPublic, JoinListed, IsLobby, Status, CreatedUtc)
    VALUES ('welcome', N'처음 오신 분', 'WELCOM', 'Asia/Seoul', 'blue', 1, 1, 0, 0, 1, 'Active', @at);

DECLARE @lobby int = (SELECT Id FROM app.Tenant WHERE IsLobby = 1);

-- 넣을 계정(같은 창에서 다시 실행해도 되게 임시 표를 먼저 지움)
DROP TABLE IF EXISTS #A;
SELECT a.Id AS AccountId INTO #A
FROM app.Account a
WHERE a.Status = 'Active' AND a.IsPlatformAdmin = 0
  AND NOT EXISTS (SELECT 1 FROM app.Membership m JOIN app.Tenant t ON t.Id = m.TenantId
                  WHERE m.AccountId = a.Id AND m.Status = 'Approved' AND t.Status = 'Active' AND t.IsLobby = 0)
  AND NOT EXISTS (SELECT 1 FROM app.Membership m WHERE m.AccountId = a.Id AND m.TenantId = @lobby);

INSERT app.Membership (TenantId, AccountId, Status, GroupLabel, RequestedUtc, DecidedUtc, DecidedByAccountId)
SELECT @lobby, AccountId, 'Approved', NULL, @at, @at, NULL FROM #A;

INSERT app.MembershipRole (TenantId, MembershipId, Role)
SELECT @lobby, m.Id, 'Participant' FROM app.Membership m JOIN #A ON #A.AccountId = m.AccountId WHERE m.TenantId = @lobby;

INSERT app.Participant (TenantId, MembershipId, IsActive)
SELECT @lobby, m.Id, 1 FROM app.Membership m JOIN #A ON #A.AccountId = m.AccountId WHERE m.TenantId = @lobby;

COMMIT;

SELECT N'결과' AS 확인, @lobby AS 임시부서_번호, (SELECT COUNT(*) FROM #A) AS 이번에_넣은_계정,
       (SELECT COUNT(*) FROM app.Membership WHERE TenantId = @lobby AND Status = 'Approved') AS 임시부서_전체_인원;
