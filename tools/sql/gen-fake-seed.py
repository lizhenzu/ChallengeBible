# -*- coding: utf-8 -*-
"""
가짜 시험 데이터 SQL 생성기(2026-11-14 처음, 2026-11-16 명부 기반으로 바꿈 — 사용자 결정 "명부는 유지, 케이스별 가짜 데이터만 더하기").

만드는 파일(같은 폴더 + src/ChallengeBible.Web/TestTools):
  fake-create.sql : 아무것도 지우지 않는다. 부서마다 이미 올린 명부(반에 든 학생 · 담임)에 가짜 계정을 이어 붙이고,
                    상황(케이스)별 미션 점수 · 출석 · 생일 · 통독 · 가족팀 · 친구팀 · 학부모를 더한다.
  fake-delete.sql : 가짜 표시가 붙은 것만 지우고, 명부에 이은 줄은 원래대로 되돌린다(계정 연결 · 역할 코드 · 비어 있던 생일).
                    관리자(Admin · SuperAdmin 역할) 계정과 반 · 명부는 절대 지우지 않는다.

가짜 표시: 이메일 @fake-seed.test 계정, CreatedByUserName = FAKE-SEED-20261114(학부모 명부 · 출석 · 보호 관계),
           감사 로그 Action = FakeBirthday(비어 있던 생일을 채운 명부 줄 — 지울 때 다시 비운다).

다시 만들기:  python tools/sql/gen-fake-seed.py
비밀번호 1234 의 해시는 ASP.NET Core Identity PasswordHasher V3(PBKDF2-HMACSHA512, 100,000번, 소금 16바이트)와 같은 모양이다.
소금이 고정이라(다시 만들어도 같은 파일) 시험 데이터에만 쓴다.
"""
import base64, hashlib, os, random, struct

HERE = os.path.dirname(os.path.abspath(__file__))
MARKER = "FAKE-SEED-20261114"
EMAIL_DOMAIN = "fake-seed.test"
TODAY = "2026-11-14"
PARENTS = 10


def password_hash(password: str) -> str:
    salt = hashlib.sha256(b"challengebible-fake-seed-20261114").digest()[:16]
    iterations = 100_000
    subkey = hashlib.pbkdf2_hmac("sha512", password.encode("utf-8"), salt, iterations, 32)
    blob = b"\x01" + struct.pack(">III", 2, iterations, len(salt)) + salt + subkey   # 2 = HMACSHA512
    return base64.b64encode(blob).decode("ascii")


def name_parts():
    sur = "김이박최정강조윤장임한오서신권황안송류홍전고문양손배백허유남심노하곽성차주우구민나진지엄채원천방공현함변염여추도소석선설마길연위표명기반왕금옥육인맹제모탁국어은편용예경봉사부"
    d = lambda t: "".join(dict.fromkeys(t))
    return d(sur)[:40], d("민서지하도예시유주수은채윤현준우나다가소태건승재영동성혜정미선경진"), d("준윤우아연원진호린율빈혁희온별솔찬결람후서민영현수은빛나름석훈")


# ── 상황 틀(모든 부서가 같은 틀). 학생 · 선생님은 그 부서 명부에서 차례로 골라 잇고, 명부가 적으면 있는 만큼만 ──
# kind: S 학생(반에 든 학생 줄) · T 선생님(담임 줄) · P 학부모(가짜 명부 줄을 새로 만듦)
students_target = [3100, 2750, 2600, 2590,                                   # s01 ~ s04 상한(30,000 크레딧) 근처 · 넘음
                   2500, 2300, 2100, 1900, 1700, 1550, 1400, 1250, 1100, 1000,  # s05 ~ s14
                   990, 890, 800, 720, 650, 580, 500, 420, 350, 300, 250, 210,  # s15 ~ s26
                   199, 190, 150, 105, 100,                                    # s27 ~ s31 100점 바로 위
                   99, 95, 80, 60, 35, 10,                                     # s32 ~ s37 100점 아래
                   0, 0, 0]                                                    # s38 ~ s40 미션 안 함
assert len(students_target) == 40
birth = {1: (11, 1), 2: (11, 5), 3: (11, 10), 4: (11, 13), 5: (11, 14), 6: (11, 15), 7: (11, 20), 8: (11, 28),   # 이번 달
         9: (12, 1), 10: (12, 3), 11: (12, 5), 12: (12, 8), 13: (12, 10), 14: (12, 14),                         # 생일 선물 기간(30 ~ 7일 전)
         15: (11, 21), 16: (11, 19), 17: (12, 15), 18: (12, 20),                                                # 기간 경계 · 바로 밖
         19: None, 20: None}                                                                                     # 생일 없음(채우지 않음)
for i in range(21, 41):
    birth[i] = (((i - 21) % 10) + 1, (i * 7) % 28 + 1)   # 1 ~ 10월에 고루
rates = [100, 95, 90, 80, 65, 50, 35]
absent3 = {10, 21, 35}

tpl = []   # (code, kind, no, bm, bd, target, rate, absent3)
teacher_target = [0, 50, 150, 300, 600, 900, 1200, 0, 250, 1500]
for i in range(1, 11):
    tpl.append((f"t{i:02d}", "T", i, None, None, teacher_target[i - 1], 0, 0))
for i in range(1, 41):
    b = birth[i]
    tpl.append((f"s{i:02d}", "S", i, b[0] if b else None, b[1] if b else None, students_target[i - 1],
                100 if i in (1, 8, 15) else rates[i % len(rates)], 1 if i in absent3 else 0))

# 가족팀 · 친구팀: (번호, 종류, 팀 점수 목표, 구성원[(코드, 관계 0 학생 · 1 엄마 · 2 아빠)], 준 선물 단계)
# 구성원이 그 부서에 다 있을 때만 만든다(명부가 적은 부서는 일부 팀이 빠진다)
teams = [
    (1, "FAMILY", 600,   [("s37", 0), ("p01", 1)], []),
    (2, "FAMILY", 1050,  [("s30", 0), ("p02", 1)], []),
    (3, "FAMILY", 1990,  [("s26", 0), ("s28", 0), ("p03", 2)], []),
    (4, "FAMILY", 3020,  [("s18", 0), ("p04", 1), ("p05", 2)], [1000]),
    (5, "FAMILY", 4980,  [("s11", 0), ("s16", 0), ("p06", 1)], []),
    (6, "FAMILY", 7100,  [("s08", 0), ("p07", 2)], [1000, 2000]),
    (7, "FAMILY", 10050, [("s06", 0), ("s02", 0), ("p08", 1), ("p09", 2)], [1000]),
    (8, "FAMILY", 2050,  [("s17", 0), ("p10", 1)], []),
    (9, "FRIEND", None,  [("s24", 0), ("s20", 0)], []),     # 880
    (10, "FRIEND", None, [("s23", 0), ("s19", 0)], []),     # 1,000(경계)
    (11, "FRIEND", None, [("s12", 0), ("s09", 0)], []),     # 2,950
    (12, "FRIEND", None, [("s05", 0), ("s03", 0)], [1000]), # 5,100
]
s_target = {f"s{i:02d}": students_target[i - 1] for i in range(1, 41)}
parent_target, parent_birth = {}, [(11, 25), (12, 5), (3, 3), (6, 17), (9, 9), (1, 30), (4, 22), (7, 4), (10, 27), (12, 24)]
for no, kind, total, mem, _ in teams:
    if kind != "FAMILY":
        continue
    rest = total - sum(s_target[c] for c, _ in mem if c.startswith("s"))
    ps = [c for c, _ in mem if c.startswith("p")]
    assert rest >= 0, no
    for k, c in enumerate(ps):
        parent_target[c] = rest // len(ps) + (rest % len(ps) if k == 0 else 0)
for i in range(1, PARENTS + 1):
    c = f"p{i:02d}"
    tpl.append((c, "P", i, *parent_birth[i - 1], parent_target[c], 0, 0))

# 통독: (코드, 회차, 시작 며칠 전, 끝 며칠 전 또는 None=읽는 중, 읽은 장 수(읽는 중일 때))
reading = [
    ("s13", 1, 120, 10, None), ("s22", 1, 150, 40, None),
    ("s07", 1, 300, 150, None), ("s07", 2, 149, 5, None),
    ("s15", 1, 310, 180, None), ("s15", 2, 179, 20, None),
    ("s01", 1, 310, 220, None), ("s01", 2, 219, 100, None), ("s01", 3, 99, 3, None),
    ("s04", 1, 90, None, 1177), ("s12", 1, 80, None, 595), ("s19", 1, 30, None, 119),
    ("s25", 1, 60, None, 297), ("s29", 1, 100, None, 892), ("s33", 1, 2, None, 1),
    ("t01", 1, 200, 12, None), ("p02", 1, 180, 15, None),
]


def sql_str(v):
    if v is None:
        return "NULL"
    if isinstance(v, int):
        return str(v)
    return "N'" + str(v).replace("'", "''") + "'"


def values(rows):
    return ",\n".join("(" + ", ".join(sql_str(v) for v in r) + ")" for r in rows)


HEADER = f"""SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET QUOTED_IDENTIFIER ON; SET NUMERIC_ROUNDABORT OFF;
"""

COUNTS = """SELECT @Step AS [단계],
       (SELECT COUNT(*) FROM [Identity].[User]) AS [계정],
       (SELECT COUNT(*) FROM dbo.DeptRoleUserAssoc) AS [소속],
       (SELECT COUNT(*) FROM app.Person) AS [명부],
       (SELECT COUNT(*) FROM app.Person WHERE UserId IS NOT NULL) AS [계정 이은 명부],
       (SELECT COUNT(*) FROM app.ClassMember) AS [반 배정],
       (SELECT COUNT(*) FROM app.AttendanceCheck) AS [출석],
       (SELECT COUNT(*) FROM dbo.DM2_MissionActivity) AS [미션 기록],
       (SELECT COUNT(*) FROM dbo.DM2_FamilyTeam) AS [팀],
       (SELECT COUNT(*) FROM dbo.DM2_FamilyMember) AS [팀 구성원],
       (SELECT COUNT(*) FROM dbo.DM2_TeamAward) AS [시상 기록],
       (SELECT COUNT(*) FROM app.GuardianLink) AS [보호 관계],
       (SELECT COUNT(*) FROM dbo.DM2_ReadingRound) AS [통독 회차],
       (SELECT COUNT(*) FROM dbo.DM2_ReadingChapterRead) AS [통독 장];
"""

# 관리자 역할(Admin · SuperAdmin) 계정인가 — 넣기 · 지우기 모두 이 계정은 건드리지 않는다
IS_ADMIN = "EXISTS (SELECT 1 FROM [Identity].[UserRoles] ur JOIN [Identity].[Role] r ON r.Id = ur.RoleId WHERE ur.UserId = u.Id AND r.NormalizedName IN (N'ADMIN', N'SUPERADMIN'))"


def create_sql() -> str:
    sur, na, nb = name_parts()
    pw = password_hash("1234")
    team_rows = [(no, kind) for no, kind, *_ in teams]
    team_mem = [(no, c, rel, k + 1, 1 if k == 0 else 0) for no, _, _, mem, _ in teams for k, (c, rel) in enumerate(mem)]
    awards = [(no, th) for no, *_, given in teams for th in given]
    return HEADER + f"""-- ============================================================================
-- 가짜 시험 데이터 넣기 (생성기 tools/sql/gen-fake-seed.py, 2026-11-16 명부 기반)
-- 아무것도 지우지 않는다. 반 · 명부 · 관리자 계정은 그대로 둔다.
-- "처음 오신 분"(임시 부서)을 뺀 부서마다:
--   학생 s01 ~ s40 = 반에 든 학생 명부 줄(계정 없는 줄)에 차례로 가짜 계정을 잇는다(명부가 적으면 있는 만큼)
--   선생님 t01 ~ t10 = 담임 명부 줄에 잇는다
--   학부모 p01 ~ p10 = 가짜 명부 줄을 새로 만든다(가족팀 · 보호 관계용)
--   그리고 상황별 미션 점수 · 출석(빈 주일만) · 생일(비어 있을 때만) · 통독 · 가족팀 · 친구팀을 더한다
-- 아이디 = 부서 주소 + "-" + t01 · s01 · p01, 비밀번호 모두 1234
-- 지우기: fake-delete.sql (이은 명부 줄은 원래대로 되돌린다)
-- ============================================================================
DECLARE @Commit bit = 0;                 -- 0 = 끝에 되돌림, 1 = 저장
DECLARE @Today date = '{TODAY}';         -- 기준 날짜(부서 날짜) — 생일 · 통독 날짜가 이 날 기준
DECLARE @Start date = DATEFROMPARTS(YEAR(@Today), 1, 1);
DECLARE @Marker nvarchar(50) = N'{MARKER}';
DECLARE @Pw nvarchar(max) = N'{pw}';     -- 1234
DECLARE @Step nvarchar(20);


BEGIN TRY
BEGIN TRANSACTION;
-- tenant isolation policy off only inside this transaction (back on before the end; a rollback also restores it)
ALTER SECURITY POLICY sec.TenantIsolation WITH (STATE = OFF);

SET @Step = N'0 넣기 전';
{COUNTS}
-- ── 1. 이미 넣었으면 멈춘다(먼저 지우기) ──
IF EXISTS (SELECT 1 FROM [Identity].[User] u WHERE u.Email LIKE N'%@{EMAIL_DOMAIN}' AND NOT {IS_ADMIN})
    THROW 50005, N'가짜 데이터가 이미 있습니다. 먼저 지우기를 실행하세요.', 1;

DECLARE @RoleBasic nvarchar(450) = (SELECT Id FROM [Identity].[Role] WHERE NormalizedName = N'BASIC');
IF @RoleBasic IS NULL THROW 50002, N'Identity.Role 에 Basic 이 없습니다. 알려 주세요.', 1;

-- numbers 0 .. 999
CREATE TABLE #Num (I int PRIMARY KEY);
INSERT #Num SELECT a.v + 10 * b.v + 100 * c.v FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) a(v)
CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) b(v) CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) c(v);

-- 학부모 이름: 성 + 두 글자, 모두 다름
CREATE TABLE #Name (Idx int PRIMARY KEY, Name nvarchar(50) COLLATE DATABASE_DEFAULT);
DECLARE @Sur nvarchar(100) = N'{sur}', @Na nvarchar(100) = N'{na}', @Nb nvarchar(100) = N'{nb}';
;WITH k AS (SELECT I AS i FROM #Num WHERE I BETWEEN 1 AND 100),
n AS (SELECT DISTINCT SUBSTRING(@Sur, s.i, 1) + SUBSTRING(@Na, a.i, 1) + SUBSTRING(@Nb, b.i, 1) AS Name
      FROM k s JOIN k a ON a.i <= LEN(@Na) JOIN k b ON b.i <= LEN(@Nb)
      WHERE s.i <= LEN(@Sur) AND SUBSTRING(@Na, a.i, 1) <> SUBSTRING(@Nb, b.i, 1))
INSERT #Name (Idx, Name) SELECT TOP (3000) ROW_NUMBER() OVER (ORDER BY HASHBYTES('MD5', Name), Name) - 1, Name FROM n ORDER BY HASHBYTES('MD5', Name), Name;

CREATE TABLE #Tpl (Code varchar(10) COLLATE DATABASE_DEFAULT PRIMARY KEY, Kind char(1) COLLATE DATABASE_DEFAULT, No int, BM tinyint, BD tinyint, Target int, AttRate int, Absent3 bit);
INSERT #Tpl VALUES
{values(tpl)};

CREATE TABLE #Team (TeamNo int PRIMARY KEY, TeamType varchar(10) COLLATE DATABASE_DEFAULT);
INSERT #Team (TeamNo, TeamType) VALUES
{values(team_rows)};
CREATE TABLE #TeamMem (TeamNo int, Code varchar(10) COLLATE DATABASE_DEFAULT, Rel tinyint, SortNo int, IsCreator bit);
INSERT #TeamMem VALUES
{values(team_mem)};
CREATE TABLE #Award (TeamNo int, Threshold int);
INSERT #Award VALUES
{values(awards)};
CREATE TABLE #TeamT (TeamNo int PRIMARY KEY, TeamType varchar(10) COLLATE DATABASE_DEFAULT, TeamName nvarchar(50) COLLATE DATABASE_DEFAULT, TeamId int);

CREATE TABLE #Read (Code varchar(10) COLLATE DATABASE_DEFAULT, RoundNo int, StartAgo int, EndAgo int NULL, Chapters int NULL);
INSERT #Read VALUES
{values(reading)};

-- 미션 칸: 1월 1일 ~ 어제, 지금 규칙에서 켜진 항목(규칙 표가 비면 6개 모두). 암송은 주마다 하나(주 시작 = 일요일)
CREATE TABLE #Item (Item varchar(20) COLLATE DATABASE_DEFAULT);
DECLARE @Rule nvarchar(max) = (SELECT TOP 1 [Values] FROM app.PlatformRuleSetVersion WHERE EffectiveFrom <= @Today ORDER BY VersionNo DESC);
IF @Rule IS NULL
    INSERT #Item VALUES ('Word.Me'), ('Word.Parent'), ('Prayer.Me'), ('Prayer.Others'), ('Quiz.Correct'), ('Recite');
ELSE   -- rule JSON has "Item":"WordMe","Enabled":true ; read by text match
    INSERT #Item SELECT v.Item FROM (VALUES ('WordMe', 'Word.Me'), ('WordParent', 'Word.Parent'), ('PrayerMe', 'Prayer.Me'),
                                            ('PrayerOthers', 'Prayer.Others'), ('QuizCorrect', 'Quiz.Correct'), ('Recite', 'Recite')) v(J, Item)
    WHERE REPLACE(@Rule, ' ', '') NOT LIKE '%"Item":"' + v.J + '","Enabled":false%';

CREATE TABLE #Day (D date PRIMARY KEY);
INSERT #Day SELECT DATEADD(day, I, @Start) FROM #Num WHERE I < DATEDIFF(day, @Start, @Today);
CREATE TABLE #Slot (SlotNo int IDENTITY PRIMARY KEY, MissionDate date, Item varchar(20) COLLATE DATABASE_DEFAULT, WeekStart date NULL);
INSERT #Slot (MissionDate, Item, WeekStart) SELECT d.D, i.Item, NULL FROM #Day d CROSS JOIN #Item i WHERE i.Item <> 'Recite';
INSERT #Slot (MissionDate, Item, WeekStart) SELECT d.D, 'Recite', d.D FROM #Day d
WHERE EXISTS (SELECT 1 FROM #Item WHERE Item = 'Recite') AND (DATEPART(weekday, d.D) + @@DATEFIRST - 1) % 7 = 0;
DECLARE @NSlot int = (SELECT COUNT(*) FROM #Slot);

-- 주일: 1월 첫 주일 ~ 오늘 이전 마지막 주일(@L)
DECLARE @L date = DATEADD(day, -((DATEPART(weekday, @Today) + @@DATEFIRST - 1) % 7), @Today);
CREATE TABLE #Sun (D date PRIMARY KEY, WeeksAgo int);
INSERT #Sun SELECT D, DATEDIFF(day, D, @L) / 7 FROM #Day WHERE (DATEPART(weekday, D) + @@DATEFIRST - 1) % 7 = 0 AND D <= @L;
IF NOT EXISTS (SELECT 1 FROM #Sun WHERE D = @L) INSERT #Sun VALUES (@L, 0);

-- 통독 장 차례(창세기 1장 = 1)
CREATE TABLE #Ch (Seq int PRIMARY KEY, BookId tinyint, Chapter smallint);
INSERT #Ch SELECT ROW_NUMBER() OVER (ORDER BY BookId, Chapter), BookId, Chapter FROM app.BibleChapter;
DECLARE @NCh int = (SELECT COUNT(*) FROM #Ch);
IF @NCh = 0 SELECT N'알림' AS [단계], N'app.BibleChapter 가 비어 있어 통독 기록은 넣지 않습니다.' AS [내용];

-- 부서 한 곳의 사람: IsNew = 1 이면 새로 만든 명부 줄(학부모), 0 이면 이은 명부 줄
CREATE TABLE #P (Code varchar(10) COLLATE DATABASE_DEFAULT PRIMARY KEY, Kind char(1) COLLATE DATABASE_DEFAULT, BM tinyint, BD tinyint, Target int, AttRate int, Absent3 bit,
    MemberRole nvarchar(50) COLLATE DATABASE_DEFAULT, OrigBM tinyint, UserName nvarchar(256) COLLATE DATABASE_DEFAULT, Name nvarchar(50) COLLATE DATABASE_DEFAULT,
    UserId nvarchar(450) COLLATE DATABASE_DEFAULT, PersonId int, IsNew bit);

-- ── 2. 부서마다 넣기 ──
DECLARE @T int, @Slug nvarchar(200), @Ord int = 0;
SET @T = (SELECT MIN(Id) FROM app.Tenant WHERE IsLobby = 0);   -- loop by id
WHILE @T IS NOT NULL
BEGIN
    SET @Slug = (SELECT LOWER(Slug) FROM app.Tenant WHERE Id = @T);
    DELETE FROM #P;

    -- 학생: 반(새친구반 아님)에 든 학생 명부 줄 중 계정 없는 줄, 반 · 명부 차례로
    ;WITH s AS (
        SELECT p.Id, p.Name, p.RoleCode, p.BirthMonth, ROW_NUMBER() OVER (ORDER BY c.Id, p.Id) AS rn
        FROM app.Person p
        JOIN app.ClassMember m ON m.TenantId = p.TenantId AND m.PersonId = p.Id AND m.IsTeacher = 0
        JOIN app.Class c ON c.TenantId = m.TenantId AND c.Id = m.ClassId AND c.IsNewcomer = 0
        WHERE p.TenantId = @T AND p.UserId IS NULL AND p.NewcomerFirstVisit IS NULL AND ISNULL(p.RoleCode, N'') = N'T1001')
    INSERT #P SELECT t.Code, t.Kind, t.BM, t.BD, t.Target, t.AttRate, t.Absent3, ISNULL(s.RoleCode, N'T1001'), s.BirthMonth,
                     @Slug + N'-' + t.Code, s.Name, CONVERT(nvarchar(450), NEWID()), s.Id, 0
    FROM #Tpl t JOIN s ON s.rn = t.No WHERE t.Kind = 'S';

    -- 선생님: 담임 명부 줄 중 계정 없는 줄(한 사람 한 번)
    ;WITH tt AS (
        SELECT p.Id, p.Name, p.RoleCode, ROW_NUMBER() OVER (ORDER BY MIN(c.Id), p.Id) AS rn
        FROM app.Person p
        JOIN app.ClassMember m ON m.TenantId = p.TenantId AND m.PersonId = p.Id AND m.IsTeacher = 1
        JOIN app.Class c ON c.TenantId = m.TenantId AND c.Id = m.ClassId AND c.IsNewcomer = 0
        WHERE p.TenantId = @T AND p.UserId IS NULL
        GROUP BY p.Id, p.Name, p.RoleCode)
    INSERT #P SELECT t.Code, t.Kind, t.BM, t.BD, t.Target, t.AttRate, t.Absent3, ISNULL(tt.RoleCode, N'T1002'), NULL,
                     @Slug + N'-' + t.Code, tt.Name, CONVERT(nvarchar(450), NEWID()), tt.Id, 0
    FROM #Tpl t JOIN tt ON tt.rn = t.No WHERE t.Kind = 'T';

    -- 학부모: 새 명부 줄(역할 없음 = 학부모)
    INSERT #P SELECT t.Code, t.Kind, t.BM, t.BD, t.Target, t.AttRate, t.Absent3, NULL, NULL,
                     @Slug + N'-' + t.Code, n.Name, CONVERT(nvarchar(450), NEWID()), NULL, 1
    FROM #Tpl t JOIN #Name n ON n.Idx = @Ord * {PARENTS} + t.No - 1 WHERE t.Kind = 'P';

    IF EXISTS (SELECT 1 FROM #P p JOIN [Identity].[User] u ON u.NormalizedUserName = UPPER(p.UserName) OR u.NormalizedEmail = UPPER(p.UserName + N'@{EMAIL_DOMAIN}'))
        THROW 50004, N'가짜 아이디가 이미 있는 계정과 겹칩니다. 알려 주세요.', 1;

    -- 계정 · 소속(역할은 명부 줄의 역할 코드를 옮긴다)
    INSERT [Identity].[User] (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp,
        PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount, FirstName, LastName, ProfilePicture, UsernameChangeLimit, insertDT)
    SELECT UserId, UserName, UPPER(UserName), UserName + N'@{EMAIL_DOMAIN}', UPPER(UserName + N'@{EMAIL_DOMAIN}'), 1, @Pw, REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), CONVERT(nvarchar(36), NEWID()),
        NULL, 0, 0, NULL, 1, 0, Name, N'', NULL, 0, '2026-01-02T00:00:00'
    FROM #P;
    INSERT [Identity].[UserRoles] (UserId, RoleId) SELECT UserId, @RoleBasic FROM #P;
    INSERT app.UserSetting (UserName, MustChangePassword, LastTenantId) SELECT UserName, 0, @T FROM #P;
    INSERT dbo.DeptRoleUserAssoc (TenantId, RoleCode, UserId, InsertDT, updateDT) SELECT @T, MemberRole, UserId, '2026-01-02T00:00:00', '2026-01-02T00:00:00' FROM #P;

    -- 명부 줄에 잇기(역할 코드는 소속으로 옮겼으니 비운다 — 지울 때 소속에서 되돌린다)
    UPDATE x SET UserId = p.UserId, RoleCode = NULL
    FROM app.Person x JOIN #P p ON p.PersonId = x.Id AND p.IsNew = 0
    WHERE x.TenantId = @T AND x.UserId IS NULL;
    -- 생일: 비어 있던 줄만 채우고, 채운 줄을 감사 로그에 남긴다(지울 때 다시 비운다)
    INSERT app.AuditLog (OccurredUtc, ActorUserName, Action, TenantId, Detail)
    SELECT '2026-01-02T00:00:00', @Marker, N'FakeBirthday', @T, CONVERT(nvarchar(20), p.PersonId)
    FROM #P p WHERE p.IsNew = 0 AND p.BM IS NOT NULL AND p.OrigBM IS NULL AND p.Kind = 'S';
    UPDATE x SET BirthMonth = p.BM, BirthDay = p.BD
    FROM app.Person x JOIN #P p ON p.PersonId = x.Id AND p.IsNew = 0
    WHERE x.TenantId = @T AND p.Kind = 'S' AND p.BM IS NOT NULL AND x.BirthMonth IS NULL;

    -- 학부모 명부 줄
    INSERT app.Person (TenantId, Name, RoleCode, BirthMonth, BirthDay, UserId, CreatedUtc, CreatedByUserName)
    SELECT @T, p.Name, NULL, p.BM, p.BD, p.UserId, '2026-01-02T00:00:00', @Marker FROM #P p WHERE p.IsNew = 1;
    UPDATE p SET PersonId = x.Id FROM #P p JOIN app.Person x ON x.TenantId = @T AND x.UserId = p.UserId WHERE p.IsNew = 1;

    -- 출석: 이은 학생만, 주일마다 랜덤(출석률), 3주 결석 아이는 최근 3주 결석. 이미 기록이 있는 주일은 그대로
    INSERT app.AttendanceCheck (TenantId, PersonId, Sunday, CreatedUtc, CreatedByUserName)
    SELECT @T, p.PersonId, s.D, DATEADD(hour, 2, CAST(s.D AS datetime2(3))), @Marker
    FROM #P p CROSS JOIN #Sun s
    WHERE p.Kind = 'S' AND NOT (p.Absent3 = 1 AND s.WeeksAgo <= 2)
      AND ABS(CHECKSUM(@T, p.Code, s.D)) % 100 < p.AttRate
      AND NOT EXISTS (SELECT 1 FROM app.AttendanceCheck a WHERE a.TenantId = @T AND a.PersonId = p.PersonId AND a.Sunday = s.D);

    -- 미션 기록: 사람마다 목표 점수를 정확히 맞춘다. 한 줄 점수 = 1 ~ 10, 날짜 · 항목은 랜덤
    ;WITH ppl AS (
        SELECT p.UserName, p.Target, CASE WHEN x.c < 1 THEN 1 WHEN x.c > 10 THEN 10 ELSE x.c END AS P
        FROM #P p CROSS APPLY (SELECT CAST(CEILING(p.Target / (@NSlot * 0.6)) AS int) AS c) x
        WHERE p.Target > 0),
    r AS (
        SELECT p.UserName, p.Target, p.P, s.MissionDate, s.Item, s.WeekStart,
               ROW_NUMBER() OVER (PARTITION BY p.UserName ORDER BY ABS(CHECKSUM(p.UserName, s.SlotNo)), s.SlotNo) AS rn
        FROM ppl p CROSS JOIN #Slot s)
    INSERT dbo.DM2_MissionActivity (TenantId, UserName, MissionDate, MissionItem, Points, ReciteWeekStart, PerformedAt)
    SELECT @T, UserName, MissionDate, Item, CASE WHEN rn <= Target / P THEN P ELSE Target % P END, WeekStart,
           DATEADD(minute, 600 + ABS(CHECKSUM(UserName, MissionDate, Item)) % 180, CAST(MissionDate AS datetime2(3)))
    FROM r WHERE rn <= Target / P OR (rn = Target / P + 1 AND Target % P > 0);

    -- 통독: 끝낸 회차는 모든 장, 읽는 중인 회차는 앞에서부터 정한 장 수
    IF @NCh > 0
    BEGIN
        INSERT dbo.DM2_ReadingRound (UserName, RoundNo, StartedAt, CompletedAt, ClosedAt)
        SELECT p.UserName, r.RoundNo, DATEADD(hour, 10, CAST(DATEADD(day, -r.StartAgo, @Today) AS datetime2(3))),
               CASE WHEN r.EndAgo IS NOT NULL THEN DATEADD(hour, 11, CAST(DATEADD(day, -r.EndAgo, @Today) AS datetime2(3))) END, NULL
        FROM #Read r JOIN #P p ON p.Code = r.Code;
        INSERT dbo.DM2_ReadingChapterRead (UserName, RoundNo, BookId, Chapter, ReadAt)
        SELECT p.UserName, r.RoundNo, c.BookId, c.Chapter,
               DATEADD(minute, CAST(CAST(c.Seq AS bigint) * DATEDIFF(minute, x.St, x.En) / x.Cnt AS int), x.St)
        FROM #Read r JOIN #P p ON p.Code = r.Code
        CROSS APPLY (SELECT DATEADD(hour, 10, CAST(DATEADD(day, -r.StartAgo, @Today) AS datetime2(3))) AS St,
                            DATEADD(hour, 11, CAST(DATEADD(day, -ISNULL(r.EndAgo, 1), @Today) AS datetime2(3))) AS En,
                            CASE WHEN r.EndAgo IS NOT NULL THEN @NCh WHEN r.Chapters > @NCh THEN @NCh ELSE r.Chapters END AS Cnt) x
        JOIN #Ch c ON c.Seq <= x.Cnt;
    END;

    -- 가족팀 · 친구팀(구성원이 이 부서에 다 있는 팀만, 방장 = 첫 구성원), 보호 관계(가족팀 학부모 → 학생), 준 선물 기록(시상 잠금)
    DELETE FROM #TeamT;
    INSERT #TeamT (TeamNo, TeamType) SELECT t.TeamNo, t.TeamType FROM #Team t
    WHERE NOT EXISTS (SELECT 1 FROM #TeamMem m WHERE m.TeamNo = t.TeamNo AND NOT EXISTS (SELECT 1 FROM #P p WHERE p.Code = m.Code));
    UPDATE t SET TeamName = LEFT(CASE WHEN t.TeamType = 'FAMILY' THEN a.Name + N'네 가족' ELSE a.Name + N' · ' + b.Name + N' 친구팀' END, 50), TeamId = NULL
    FROM #TeamT t
    JOIN #TeamMem ma ON ma.TeamNo = t.TeamNo AND ma.SortNo = 1 JOIN #P a ON a.Code = ma.Code
    LEFT JOIN #TeamMem mb ON mb.TeamNo = t.TeamNo AND mb.SortNo = 2 LEFT JOIN #P b ON b.Code = mb.Code;
    INSERT dbo.DM2_FamilyTeam (TenantId, TeamName, IsActive, Memo, InsertDT, UpdateDT, AwardKey, CreatorUserName, TeamType)
    SELECT @T, t.TeamName, 1, NULL, '2026-01-10T00:00:00', '2026-01-10T00:00:00', NULL, p.UserName, t.TeamType
    FROM #TeamT t JOIN #TeamMem m ON m.TeamNo = t.TeamNo AND m.IsCreator = 1 JOIN #P p ON p.Code = m.Code;
    UPDATE t SET TeamId = x.FamilyTeamId FROM #TeamT t
    JOIN #TeamMem m ON m.TeamNo = t.TeamNo AND m.IsCreator = 1 JOIN #P p ON p.Code = m.Code
    JOIN dbo.DM2_FamilyTeam x ON x.TenantId = @T AND x.TeamName = t.TeamName AND x.CreatorUserName = p.UserName;
    INSERT dbo.DM2_FamilyMember (TenantId, FamilyTeamId, UserName, RoleCode, SortNo, IsActive, InsertDT, UpdateDT, RelationCode, RelationLabel)
    SELECT @T, t.TeamId, p.UserName, m.Rel, m.SortNo, 1, '2026-01-10T00:00:00', '2026-01-10T00:00:00', 9, NULL
    FROM #TeamMem m JOIN #TeamT t ON t.TeamNo = m.TeamNo JOIN #P p ON p.Code = m.Code;
    INSERT app.GuardianLink (TenantId, GuardianUserName, StudentUserName, CreatedUtc, CreatedByUserName)
    SELECT @T, g.UserName, s.UserName, '2026-01-10T00:00:00', @Marker
    FROM #TeamT t
    JOIN #TeamMem mg ON mg.TeamNo = t.TeamNo AND mg.Rel > 0 JOIN #P g ON g.Code = mg.Code
    JOIN #TeamMem ms ON ms.TeamNo = t.TeamNo AND ms.Rel = 0 JOIN #P s ON s.Code = ms.Code
    WHERE t.TeamType = 'FAMILY';
    INSERT dbo.DM2_TeamAward (TenantId, Kind, AwardKey, Threshold, AwardedAt)
    SELECT @T, t.TeamType, t.TeamName, a.Threshold, DATEADD(day, a.Threshold / 100, '2026-03-01T10:00:00')
    FROM #Award a JOIN #TeamT t ON t.TeamNo = a.TeamNo;

    SET @Ord += 1;
    SET @T = (SELECT MIN(Id) FROM app.Tenant WHERE IsLobby = 0 AND Id > @T);
END;

SET @Step = N'1 넣은 뒤';
{COUNTS}
-- ── 3. 부서마다 넣은 수(비밀번호 모두 1234) ──
SELECT N'2 부서별' AS [단계], t.Name AS [부서],
       (SELECT COUNT(*) FROM [Identity].[User] u WHERE u.UserName LIKE LOWER(t.Slug) + N'-s%' AND u.Email LIKE N'%@{EMAIL_DOMAIN}') AS [학생],
       (SELECT COUNT(*) FROM [Identity].[User] u WHERE u.UserName LIKE LOWER(t.Slug) + N'-t%' AND u.Email LIKE N'%@{EMAIL_DOMAIN}') AS [선생님],
       (SELECT COUNT(*) FROM [Identity].[User] u WHERE u.UserName LIKE LOWER(t.Slug) + N'-p%' AND u.Email LIKE N'%@{EMAIL_DOMAIN}') AS [학부모],
       (SELECT COUNT(*) FROM dbo.DM2_FamilyTeam f WHERE f.TenantId = t.Id AND f.CreatorUserName LIKE LOWER(t.Slug) + N'-%') AS [팀]
FROM app.Tenant t WHERE t.IsLobby = 0 ORDER BY t.Id;

ALTER SECURITY POLICY sec.TenantIsolation WITH (STATE = ON);
IF @Commit = 1
BEGIN
    COMMIT;
    SELECT N'끝: 저장했습니다.' AS [결과];
END
ELSE
BEGIN
    ROLLBACK;
    SELECT N'끝: 되돌렸습니다(저장 안 함).' AS [결과];
END;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
"""


def delete_sql() -> str:
    return HEADER + f"""-- ============================================================================
-- 가짜 시험 데이터 지우기 (생성기 tools/sql/gen-fake-seed.py, 2026-11-16 명부 기반)
-- 지우는 것: 이메일 @{EMAIL_DOMAIN} 계정(관리자 역할 계정은 빼고)과 그 기록, 가짜 표시({MARKER})가 붙은 학부모 명부 · 출석 · 보호 관계,
--            가짜 계정이 만든 가족팀 · 친구팀과 그 시상 기록
-- 되돌리는 것: 가짜 계정을 이은 명부 줄(연결을 풀고 역할 코드를 돌려놓음), 비어 있던 생일(감사 로그 FakeBirthday)
-- 남기는 것: 반 · 명부 · 관리자(Admin · SuperAdmin) 계정과 그 명부 줄, 진짜 출석 기록
-- ============================================================================
DECLARE @Commit bit = 0;                 -- 0 = 끝에 되돌림, 1 = 저장
DECLARE @Marker nvarchar(50) = N'{MARKER}';
DECLARE @MarkTime datetime2(3) = '2026-01-01T00:00:00.123';   -- 예전 가짜 반 표시
DECLARE @Step nvarchar(20);


BEGIN TRY
BEGIN TRANSACTION;
-- tenant isolation policy off only inside this transaction (back on before the end; a rollback also restores it)
ALTER SECURITY POLICY sec.TenantIsolation WITH (STATE = OFF);

SET @Step = N'0 지우기 전';
{COUNTS}
CREATE TABLE #Del (UserId nvarchar(450) COLLATE DATABASE_DEFAULT PRIMARY KEY, UserName nvarchar(256) COLLATE DATABASE_DEFAULT);
INSERT #Del SELECT u.Id, u.UserName FROM [Identity].[User] u WHERE u.Email LIKE N'%@{EMAIL_DOMAIN}' AND NOT {IS_ADMIN};
CREATE TABLE #DelTeam (TenantId int, TeamId int, AwardKey nvarchar(200) COLLATE DATABASE_DEFAULT);
INSERT #DelTeam SELECT TenantId, FamilyTeamId, ISNULL(AwardKey, TeamName) FROM dbo.DM2_FamilyTeam WHERE CreatorUserName IN (SELECT UserName FROM #Del);
-- 지울 명부 줄: 가짜 표시가 붙은 줄 중 관리자 계정에 이어지지 않은 줄(학부모 등)
CREATE TABLE #DelPerson (TenantId int, PersonId int);
INSERT #DelPerson SELECT TenantId, Id FROM app.Person WHERE CreatedByUserName = @Marker AND (UserId IS NULL OR UserId IN (SELECT UserId FROM #Del));

-- 되돌리기: 비어 있던 생일, 이은 명부 줄(역할 코드는 소속에서)
UPDATE p SET BirthMonth = NULL, BirthDay = NULL
FROM app.Person p JOIN app.AuditLog a ON a.Action = N'FakeBirthday' AND a.ActorUserName = @Marker AND a.TenantId = p.TenantId AND a.Detail = CONVERT(nvarchar(20), p.Id);
UPDATE p SET UserId = NULL, RoleCode = d.RoleCode
FROM app.Person p JOIN dbo.DeptRoleUserAssoc d ON d.TenantId = p.TenantId AND d.UserId = p.UserId
WHERE p.UserId IN (SELECT UserId FROM #Del) AND NOT EXISTS (SELECT 1 FROM #DelPerson x WHERE x.TenantId = p.TenantId AND x.PersonId = p.Id);
UPDATE p SET UserId = NULL
FROM app.Person p
WHERE p.UserId IN (SELECT UserId FROM #Del) AND NOT EXISTS (SELECT 1 FROM #DelPerson x WHERE x.TenantId = p.TenantId AND x.PersonId = p.Id);

DELETE a FROM app.AttendanceCheck a JOIN #DelPerson d ON d.TenantId = a.TenantId AND d.PersonId = a.PersonId;
DELETE FROM app.AttendanceCheck WHERE CreatedByUserName = @Marker;
DELETE m FROM app.ClassMember m JOIN #DelPerson d ON d.TenantId = m.TenantId AND d.PersonId = m.PersonId;
DELETE r FROM dbo.DM2_FamilyRequest r JOIN #DelTeam t ON t.TenantId = r.TenantId AND t.TeamId = r.FamilyTeamId;
DELETE FROM dbo.DM2_FamilyRequest WHERE TargetUserName IN (SELECT UserName FROM #Del) OR FromUserName IN (SELECT UserName FROM #Del);
DELETE FROM dbo.DM2_FamilyNotice WHERE UserName IN (SELECT UserName FROM #Del);
DELETE h FROM dbo.DM2_FamilyMilestoneHidden h JOIN #DelTeam t ON t.TenantId = h.TenantId AND t.TeamId = h.FamilyTeamId;
DELETE a FROM dbo.DM2_TeamAward a JOIN #DelTeam t ON t.TenantId = a.TenantId AND t.AwardKey = a.AwardKey;
DELETE m FROM dbo.DM2_FamilyMember m JOIN #DelTeam t ON t.TenantId = m.TenantId AND t.TeamId = m.FamilyTeamId;
DELETE FROM dbo.DM2_FamilyMember WHERE UserName IN (SELECT UserName FROM #Del);
DELETE f FROM dbo.DM2_FamilyTeam f JOIN #DelTeam t ON t.TenantId = f.TenantId AND t.TeamId = f.FamilyTeamId;
DELETE FROM app.GuardianLink WHERE CreatedByUserName = @Marker OR GuardianUserName IN (SELECT UserName FROM #Del) OR StudentUserName IN (SELECT UserName FROM #Del);
DELETE FROM app.PromotionRequest WHERE UserName IN (SELECT UserName FROM #Del);
DELETE FROM dbo.DM2_QuizAttempt WHERE UserName IN (SELECT UserName FROM #Del);
DELETE FROM dbo.DM2_MissionActivity WHERE UserName IN (SELECT UserName FROM #Del);
DELETE FROM dbo.DM2_ReadingVerse WHERE UserName IN (SELECT UserName FROM #Del);
DELETE FROM dbo.DM2_ReadingChapterRead WHERE UserName IN (SELECT UserName FROM #Del);
DELETE FROM dbo.DM2_ReadingChapterProgress WHERE UserName IN (SELECT UserName FROM #Del);
DELETE FROM dbo.DM2_ReadingBookmark WHERE UserName IN (SELECT UserName FROM #Del);
DELETE FROM dbo.DM2_ReadingRound WHERE UserName IN (SELECT UserName FROM #Del);
DELETE p FROM app.Person p JOIN #DelPerson d ON d.TenantId = p.TenantId AND d.PersonId = p.Id;
DELETE FROM dbo.SignupApplication WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM dbo.DeptRoleUserAssoc WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM app.UserSetting WHERE UserName IN (SELECT UserName FROM #Del);
DELETE FROM app.AuditLog WHERE ActorUserName IN (SELECT UserName FROM #Del) OR ActorUserName = @Marker;
DELETE FROM [Identity].[UserTokens] WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM [Identity].[UserLogins] WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM [Identity].[UserClaims] WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM [Identity].[UserRoles] WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM [Identity].[User] WHERE Id IN (SELECT UserId FROM #Del);

-- 예전 가짜 반(2026-11-14 방식): 남은 사람이 없을 때만 지운다
DELETE c FROM app.Class c WHERE c.CreatedUtc = @MarkTime
  AND NOT EXISTS (SELECT 1 FROM app.ClassMember m WHERE m.TenantId = c.TenantId AND m.ClassId = c.Id);
DELETE t FROM app.ClassTerm t WHERE t.CreatedUtc = @MarkTime
  AND NOT EXISTS (SELECT 1 FROM app.Class c WHERE c.TenantId = t.TenantId AND c.TermId = t.Id);

SET @Step = N'1 지운 뒤';
{COUNTS}
ALTER SECURITY POLICY sec.TenantIsolation WITH (STATE = ON);
IF @Commit = 1
BEGIN
    COMMIT;
    SELECT N'끝: 지웠습니다.' AS [결과];
END
ELSE
BEGIN
    ROLLBACK;
    SELECT N'끝: 되돌렸습니다(저장 안 함).' AS [결과];
END;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
"""


# SQL Studio(site4now 웹 도구)는 한글 · BOM 이 든 파일을 읽지 못한다(2026-11-14 확인) — 예전 migrate-*.sql 처럼 ASCII 만 쓴다.
# 한글 값은 CAST(0x<UTF-16LE> AS nvarchar(100)) 로, 안내 문구 · 열 이름은 영어로, 한글 주석은 지운다.
ENGLISH = {
    "N'0 넣기 전'": "N'0 before'", "N'1 넣은 뒤'": "N'1 after insert'", "N'2 부서별'": "N'2 per dept'",
    "N'0 지우기 전'": "N'0 before'", "N'1 지운 뒤'": "N'1 after delete'", "N'알림'": "N'note'",
    "N'가짜 데이터가 이미 있습니다. 먼저 지우기를 실행하세요.'": "N'Fake data already exists. Run delete first.'",
    "N'Identity.Role 에 Basic 이 없습니다. 알려 주세요.'": "N'Identity.Role has no Basic. Stop and report.'",
    "N'app.BibleChapter 가 비어 있어 통독 기록은 넣지 않습니다.'": "N'app.BibleChapter is empty: no reading records inserted.'",
    "N'가짜 아이디가 이미 있는 계정과 겹칩니다. 알려 주세요.'": "N'A fake user name already exists. Stop and report.'",
    "N'끝: 저장했습니다.'": "N'DONE: saved.'",
    "N'끝: 지웠습니다.'": "N'DONE: deleted.'",
    "N'끝: 되돌렸습니다(저장 안 함).'": "N'DONE: rolled back (not saved).'",
}
ALIASES = {"단계": "Step", "계정": "Accounts", "소속": "Memberships", "명부": "Roster", "계정 이은 명부": "LinkedRoster", "반 배정": "ClassMembers",
           "출석": "Attendance", "미션 기록": "MissionRows", "팀": "Teams", "팀 구성원": "TeamMembers", "시상 기록": "Awards", "보호 관계": "GuardianLinks",
           "통독 회차": "ReadingRounds", "통독 장": "ReadingChapters", "부서": "Dept", "선생님": "Teachers", "학생": "Students", "학부모": "Parents",
           "결과": "Result", "내용": "Message"}


def asciify(sql: str, title: str) -> str:
    import re
    for k, v in ENGLISH.items():
        sql = sql.replace(k, v)
    sql = re.sub(r"\[([^\]\x00-\x7f][^\]]*)\]", lambda m: "[" + ALIASES[m.group(1)] + "]", sql)
    def lit(m):
        s = m.group(1).replace("''", "'")
        if all(ord(ch) < 128 for ch in s):
            return m.group(0)
        return "CAST(0x" + s.encode("utf-16-le").hex().upper() + " AS nvarchar(100))"
    sql = re.sub(r"(?<![A-Za-z0-9_'])N'((?:[^']|'')*)'", lit, sql)
    out = []
    for line in sql.split("\n"):
        i = line.find("--")
        if i >= 0 and any(ord(ch) >= 128 for ch in line[i:]):
            line = line[:i].rstrip()
            if not line:
                continue
        out.append(line)
    sql = "\n".join(out)
    bad = [l for l in sql.split("\n") if any(ord(ch) >= 128 for ch in l)]
    assert not bad, bad[:3]
    return title + sql


TITLES = {
    "fake-create.sql": """-- fake-create.sql : FAKE TEST DATA ON THE UPLOADED ROSTER (generated by tools/sql/gen-fake-seed.py, 2026-11-16)
-- Deletes nothing. Classes, roster and admin accounts are kept.
-- Links fake accounts (password 1234) to roster rows: students in classes (s01 ~ s40), class teachers (t01 ~ t10),
-- adds fake parents (p01 ~ p10) and per-case missions, attendance, birthdays (only empty ones), reading, teams.
-- Undo: fake-delete.sql
""",
    "fake-delete.sql": f"""-- fake-delete.sql : DELETE FAKE TEST DATA (generated by tools/sql/gen-fake-seed.py, 2026-11-16)
-- Deletes only fake data (email @{EMAIL_DOMAIN} except Admin/SuperAdmin accounts, CreatedByUserName = {MARKER}).
-- Restores linked roster rows (account link, role code, emptied birthdays). Never deletes classes, roster or admin accounts.
""",
}

POLICY_OFF = "-- tenant isolation policy off only inside this transaction (back on before the end; a rollback also restores it)\nALTER SECURITY POLICY sec.TenantIsolation WITH (STATE = OFF);\n"
POLICY_ON = "ALTER SECURITY POLICY sec.TenantIsolation WITH (STATE = ON);\n"


def ssms(sql: str) -> str:
    """PC 의 SSMS 용: 이 연결에만 PlatformScope 를 켠다(SQL Studio 는 이 프로시저를 막는다)."""
    assert POLICY_OFF in sql and POLICY_ON in sql
    sql = sql.replace(POLICY_OFF, "")
    sql = sql.replace("BEGIN TRY\n", "EXEC sp_set_session_context N'PlatformScope', 1;   -- tenant isolation is crossed only on this connection\nBEGIN TRY\n", 1)
    sql = sql.replace(POLICY_ON, "")
    return sql + "EXEC sp_set_session_context N'PlatformScope', NULL;\n"


def app(sql: str) -> str:
    """앱 시험 도구(/test-tools)용: 시스템 연결이 PlatformScope 를 이미 켠다. 저장 여부 · 기준 날짜는 매개변수."""
    sql = sql.replace(POLICY_OFF, "").replace(POLICY_ON, "")
    for old, new in (("DECLARE @Commit bit = 0;", "DECLARE @Commit bit = @pCommit;"), (f"DECLARE @Today date = '{TODAY}';", "DECLARE @Today date = @pToday;")):
        assert old in sql or "@Today" not in sql, old
        sql = sql.replace(old, new)
    if "@pToday" not in sql:
        sql = sql.replace("DECLARE @Commit bit = @pCommit;", "DECLARE @Commit bit = @pCommit;   -- @pToday is not used here\nDECLARE @Unused date = @pToday;")
    return sql


SSMS_NOTE = "-- For SSMS on the PC (SQL Studio on sql.site4now.net blocks session settings). Set @Commit = 1 to save.\n"
APP_NOTE = "-- Embedded in the web app: run by /test-tools (TestToolsService). Parameters: @pCommit bit, @pToday date.\n"
WEB = os.path.normpath(os.path.join(HERE, "..", "..", "src", "ChallengeBible.Web", "TestTools"))

if __name__ == "__main__":
    os.makedirs(WEB, exist_ok=True)
    for name, body in (("fake-create.sql", create_sql()), ("fake-delete.sql", delete_sql())):
        for folder, flavor, note in ((HERE, ssms, SSMS_NOTE), (WEB, app, APP_NOTE)):
            with open(os.path.join(folder, name), "w", encoding="ascii", newline="\r\n") as f:
                f.write(asciify(flavor(body), TITLES[name] + note))
            print("wrote", os.path.relpath(os.path.join(folder, name), os.path.join(HERE, "..", "..")))
