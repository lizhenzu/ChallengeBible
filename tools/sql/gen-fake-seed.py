# -*- coding: utf-8 -*-
"""
가짜 시험 데이터 SQL 생성기(2026-11-14 처음, 2026-11-16 ADR 0102 — 이미 있는 반에 가짜 아이 · 담임을 새로 만든다).

만드는 파일(같은 폴더 + src/ChallengeBible.Web/TestTools):
  fake-create.sql : 아무것도 지우지 않는다. 부서마다 가장 최근 학기의 반(새친구반 제외)에
                    학생 s01 ~ s40(반마다 고루) · 담임 t01 ~(반마다 1명, 최대 10) · 학부모 p01 ~ p10 의 계정 · 명부 · 반 배정을 새로 만들고,
                    상황(케이스)별 미션 점수 · 출석 · 생일 · 통독 · 가족팀 · 친구팀 · 보호 관계를 더한다. 반이 없는 부서는 건너뛴다.
  지우기는 따로 없다: 플랫폼 ☰ › 도구 › 완전 초기화(/platform/reset)가 지운다(관리자 · 부서 · 반은 남김).

가짜 표시: 이메일 @fake-seed.test 계정, CreatedByUserName = FAKE-SEED-20261114(명부 · 출석 · 보호 관계).

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


# ── 상황 틀(모든 부서가 같은 틀). 모두 새 명부 줄 + 계정. 학생은 반마다 고루(s38 은 미배정), 선생님은 반 하나에 한 명(반 수만큼) ──
# kind: S 학생 · T 선생님(담임) · P 학부모
# 선물 점수(ADR 0104: 1점 = 15 크레딧, 처음 보너스 없음, 한 해 상한 30,000 = 2,000점) 시험용:
#   s01 2,350점 + 받은 선물 600점 → 쓸 수 있는 1,400(상한 넘음) · s03 2,000점(상한 딱) · s04 1,877점(1년 매일)
#   s05 734점 + 받은 선물 667점 → 67점 남음 · s07 700점 + 고른 선물 400점(받기 전) · s09 300점 · s10 60점(가장 싼 선물 바로 아래) · s12 1,111점
students_target = [2350, 2750, 2000, 1877,                                   # s01 ~ s04 상한 넘음 · 딱 · 1년 매일
                   734, 2300, 700, 1900, 300, 60, 1400, 1111, 1100, 1000,     # s05 ~ s14
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
# 구성원이 그 부서에 다 있을 때만 만든다
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

# 개인 선물(ADR 0104): (코드, 상태, 상품 이름, 가격, 쓴 점수, 고른 지 며칠, 산 지 며칠 또는 None, 받은 지 며칠 또는 None)
#   쓴 점수 = 가격(1,000 크레딧 단위 올림) ÷ 15 올림 — 부서 크레딧 표가 비어 있을 때와 같다
personal = [
    ("s01", "Delivered", "가짜 선물 보드게임", 9000, 600, 40, 38, 35),
    ("s05", "Delivered", "가짜 선물 색연필 세트", 9900, 667, 20, 18, 14),
    ("s07", "Picked", "가짜 선물 축구공", 6000, 400, 2, None, None),
]

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
-- 가짜 시험 데이터 넣기 (생성기 tools/sql/gen-fake-seed.py, 2026-11-16 ADR 0102)
-- 아무것도 지우지 않는다. 반 · 관리자 계정 · 이미 있는 명부는 그대로 둔다.
-- "처음 오신 분"(임시 부서)을 뺀 부서마다, 가장 최근 학기의 반(새친구반 제외)이 있으면:
--   학생 s01 ~ s40 = 새 명부 줄 + 계정, 반마다 고루 배정(s38 은 미배정)
--   선생님 t01 ~ = 반 하나에 담임 한 명(반 수만큼, 최대 10)
--   학부모 p01 ~ p10 = 새 명부 줄 + 계정(가족팀 · 보호 관계용)
--   그리고 상황별 미션 점수 · 출석 · 생일 · 통독 · 가족팀 · 친구팀을 더한다. 반이 없는 부서는 건너뛴다
-- 아이디 = 부서 주소 + "-" + t01 · s01 · p01, 비밀번호 모두 1234
-- 지우기: 플랫폼 > 완전 초기화(/platform/reset)
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
    THROW 50005, N'가짜 데이터가 이미 있습니다. 먼저 완전 초기화를 실행하세요.', 1;

DECLARE @RoleBasic nvarchar(450) = (SELECT Id FROM [Identity].[Role] WHERE NormalizedName = N'BASIC');
IF @RoleBasic IS NULL THROW 50002, N'Identity.Role 에 Basic 이 없습니다. 알려 주세요.', 1;

-- numbers 0 .. 999
CREATE TABLE #Num (I int PRIMARY KEY);
INSERT #Num SELECT a.v + 10 * b.v + 100 * c.v FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) a(v)
CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) b(v) CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) c(v);

-- 가짜 사람 이름: 성 + 두 글자, 모두 다름(부서마다 60개: 선생님 0 ~ 9, 학생 10 ~ 49, 학부모 50 ~ 59)
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

CREATE TABLE #PG (Code varchar(10) COLLATE DATABASE_DEFAULT, State varchar(20) COLLATE DATABASE_DEFAULT, Item nvarchar(100) COLLATE DATABASE_DEFAULT, Price int, Pts int, PickAgo int, BuyAgo int NULL, GiveAgo int NULL);
INSERT #PG VALUES
{values(personal)};

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
-- 새해 초기화(선물 점수는 누른 뒤 기록만 센다)를 눌렀으면 미션 기록 시각을 그 뒤로 둔다(2026-11-16, 선물 팝업이 안 뜨던 문제)
DECLARE @Reset datetime2(3) = (SELECT ClosedUtc FROM app.GiftYearClose WHERE Year = YEAR(@Today));

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

-- 부서 한 곳의 사람(모두 새 명부 줄). ClassRn = 배정할 반 차례(NULL = 미배정)
CREATE TABLE #P (Code varchar(10) COLLATE DATABASE_DEFAULT PRIMARY KEY, Kind char(1) COLLATE DATABASE_DEFAULT, BM tinyint, BD tinyint, Target int, AttRate int, Absent3 bit,
    MemberRole nvarchar(50) COLLATE DATABASE_DEFAULT, UserName nvarchar(256) COLLATE DATABASE_DEFAULT, Name nvarchar(50) COLLATE DATABASE_DEFAULT,
    UserId nvarchar(450) COLLATE DATABASE_DEFAULT, PersonId int, ClassRn int);
-- 부서 한 곳의 반: 가장 최근 학기(새친구반이 아닌 반이 있는 학기)의 새친구반이 아닌 반, Id 차례
CREATE TABLE #Cls (Rn int PRIMARY KEY, ClassId int, TermId int);
DECLARE @NCls int;
CREATE TABLE #Skip (Dept nvarchar(200) COLLATE DATABASE_DEFAULT);

-- ── 2. 부서마다 넣기 ──
DECLARE @T int, @Slug nvarchar(200), @Ord int = 0;
SET @T = (SELECT MIN(Id) FROM app.Tenant WHERE IsLobby = 0);   -- loop by id
WHILE @T IS NOT NULL
BEGIN
    SET @Slug = (SELECT LOWER(Slug) FROM app.Tenant WHERE Id = @T);
    DELETE FROM #P;
    DELETE FROM #Cls;
    INSERT #Cls (Rn, ClassId, TermId)
    SELECT ROW_NUMBER() OVER (ORDER BY c.Id), c.Id, c.TermId FROM app.Class c
    WHERE c.TenantId = @T AND c.IsNewcomer = 0 AND c.TermId = (
        SELECT TOP 1 t.Id FROM app.ClassTerm t
        WHERE t.TenantId = @T AND EXISTS (SELECT 1 FROM app.Class x WHERE x.TenantId = @T AND x.TermId = t.Id AND x.IsNewcomer = 0)
        ORDER BY t.Year DESC, t.Id DESC);
    SET @NCls = (SELECT COUNT(*) FROM #Cls);

    IF @NCls = 0
        INSERT #Skip SELECT Name FROM app.Tenant WHERE Id = @T;   -- 반이 없는 부서는 건너뛴다
    ELSE
    BEGIN
    -- 학생: 반마다 고루(s38 은 미배정), 선생님: 반 하나에 한 명, 학부모: 반 없음
    INSERT #P SELECT t.Code, t.Kind, t.BM, t.BD, t.Target, t.AttRate, t.Absent3,
                     CASE t.Kind WHEN 'S' THEN N'T1001' WHEN 'T' THEN N'T1002' END,
                     @Slug + N'-' + t.Code, CASE WHEN t.Kind = 'T' THEN n.Name + N' 선생님' ELSE n.Name END, CONVERT(nvarchar(450), NEWID()), NULL,   -- 선생님은 이름 뒤에 "선생님"(아이 이름과 헷갈리지 않게, 2026-11-17 사용자)
                     CASE WHEN t.Kind = 'S' AND t.Code <> 's38' THEN (t.No - 1) % @NCls + 1 WHEN t.Kind = 'T' THEN t.No END
    FROM #Tpl t JOIN #Name n ON n.Idx = @Ord * 60 + CASE t.Kind WHEN 'T' THEN t.No - 1 WHEN 'S' THEN 9 + t.No ELSE 49 + t.No END
    WHERE t.Kind <> 'T' OR t.No <= @NCls;

    IF EXISTS (SELECT 1 FROM #P p JOIN [Identity].[User] u ON u.NormalizedUserName = UPPER(p.UserName) OR u.NormalizedEmail = UPPER(p.UserName + N'@{EMAIL_DOMAIN}'))
        THROW 50004, N'가짜 아이디가 이미 있는 계정과 겹칩니다. 알려 주세요.', 1;

    -- 계정 · 소속
    INSERT [Identity].[User] (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp,
        PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount, FirstName, LastName, ProfilePicture, UsernameChangeLimit, insertDT)
    SELECT UserId, UserName, UPPER(UserName), UserName + N'@{EMAIL_DOMAIN}', UPPER(UserName + N'@{EMAIL_DOMAIN}'), 1, @Pw, REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), CONVERT(nvarchar(36), NEWID()),
        NULL, 0, 0, NULL, 1, 0, Name, N'', NULL, 0, '2026-01-02T00:00:00'
    FROM #P;
    INSERT [Identity].[UserRoles] (UserId, RoleId) SELECT UserId, @RoleBasic FROM #P;
    INSERT app.UserSetting (UserName, MustChangePassword, LastTenantId) SELECT UserName, 0, @T FROM #P;
    INSERT dbo.DeptRoleUserAssoc (TenantId, RoleCode, UserId, InsertDT, updateDT) SELECT @T, MemberRole, UserId, '2026-01-02T00:00:00', '2026-01-02T00:00:00' FROM #P;

    -- 명부 줄(생일 포함) · 반 배정
    INSERT app.Person (TenantId, Name, RoleCode, BirthMonth, BirthDay, UserId, CreatedUtc, CreatedByUserName)
    SELECT @T, p.Name, NULL, p.BM, p.BD, p.UserId, '2026-01-02T00:00:00', @Marker FROM #P p;
    UPDATE p SET PersonId = x.Id FROM #P p JOIN app.Person x ON x.TenantId = @T AND x.UserId = p.UserId;
    INSERT app.ClassMember (TenantId, TermId, ClassId, PersonId, IsTeacher, CreatedUtc)
    SELECT @T, c.TermId, c.ClassId, p.PersonId, CASE WHEN p.Kind = 'T' THEN 1 ELSE 0 END, '2026-01-02T00:00:00'
    FROM #P p JOIN #Cls c ON c.Rn = p.ClassRn;

    -- 출석: 학생만, 주일마다 랜덤(출석률), 3주 결석 아이는 최근 3주 결석. 이미 기록이 있는 주일은 그대로
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
           CASE WHEN @Reset IS NOT NULL THEN DATEADD(second, 1 + ABS(CHECKSUM(UserName, MissionDate, Item)) % 3600, @Reset)
                ELSE DATEADD(minute, 600 + ABS(CHECKSUM(UserName, MissionDate, Item)) % 180, CAST(MissionDate AS datetime2(3))) END
    FROM r WHERE rn <= Target / P OR (rn = Target / P + 1 AND Target % P > 0);

    -- 개인 선물(ADR 0104): 받은 것 · 고른 것과 쓴 점수(GiftPointUse) · 이력. 열쇠는 FAKE-P-코드
    INSERT app.Gift (TenantId, Kind, Year, UserName, SourceKey, State, MinAmount, MaxAmount, ItemName, ItemPrice, ItemHasImage, CreditsUsed, PointsUsed, PointsBurned,
        PickedUtc, PurchasedUtc, DeliveredUtc, CreatedUtc)
    SELECT @T, 'Personal', YEAR(@Today), p.UserName, N'FAKE-P-' + g.Code, g.State, 0, g.Price, g.Item, g.Price, 0, (g.Price + 999) / 1000 * 1000, g.Pts, 0,
        DATEADD(hour, 9, CAST(DATEADD(day, -g.PickAgo, @Today) AS datetime2(3))),
        CASE WHEN g.BuyAgo IS NOT NULL THEN DATEADD(hour, 9, CAST(DATEADD(day, -g.BuyAgo, @Today) AS datetime2(3))) END,
        CASE WHEN g.GiveAgo IS NOT NULL THEN DATEADD(hour, 9, CAST(DATEADD(day, -g.GiveAgo, @Today) AS datetime2(3))) END,
        DATEADD(hour, 9, CAST(DATEADD(day, -g.PickAgo, @Today) AS datetime2(3)))
    FROM #PG g JOIN #P p ON p.Code = g.Code;
    INSERT app.GiftPointUse (TenantId, UserName, Year, GiftId, PointsUsed, PointsBurned, FirstBonus, CreatedUtc)
    SELECT @T, x.UserName, x.Year, x.Id, x.PointsUsed, 0, 0, x.PickedUtc
    FROM app.Gift x WHERE x.TenantId = @T AND x.SourceKey LIKE N'FAKE-P-%';
    INSERT app.GiftLog (TenantId, GiftId, Action, ActorUserName, AtUtc, ItemName, ItemPrice)
    SELECT @T, x.Id, a.Action, CASE WHEN a.Action = 'Pick' THEN x.UserName ELSE @Marker END, a.At, x.ItemName, x.ItemPrice
    FROM app.Gift x
    CROSS APPLY (VALUES ('Pick', x.PickedUtc), ('Purchase', x.PurchasedUtc), ('Deliver', x.DeliveredUtc)) a(Action, At)
    WHERE x.TenantId = @T AND x.SourceKey LIKE N'FAKE-P-%' AND a.At IS NOT NULL;

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
    END;   -- 반이 있는 부서
    SET @T = (SELECT MIN(Id) FROM app.Tenant WHERE IsLobby = 0 AND Id > @T);
END;

SET @Step = N'1 넣은 뒤';
{COUNTS}
-- ── 3. 부서마다 넣은 수(비밀번호 모두 1234) ──
SELECT N'2 부서별' AS [단계], t.Name AS [부서],
       (SELECT COUNT(*) FROM [Identity].[User] u WHERE u.UserName LIKE LOWER(t.Slug) + N'-s%' AND u.Email LIKE N'%@{EMAIL_DOMAIN}') AS [학생],
       (SELECT COUNT(*) FROM [Identity].[User] u WHERE u.UserName LIKE LOWER(t.Slug) + N'-t%' AND u.Email LIKE N'%@{EMAIL_DOMAIN}') AS [선생님],
       (SELECT COUNT(*) FROM [Identity].[User] u WHERE u.UserName LIKE LOWER(t.Slug) + N'-p%' AND u.Email LIKE N'%@{EMAIL_DOMAIN}') AS [학부모],
       (SELECT COUNT(*) FROM dbo.DM2_FamilyTeam f WHERE f.TenantId = t.Id AND f.CreatorUserName LIKE LOWER(t.Slug) + N'-%') AS [팀],
       CASE WHEN EXISTS (SELECT 1 FROM #Skip k WHERE k.Dept = t.Name) THEN N'반 없음 - 건너뜀' ELSE N'' END AS [메모]
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


# SQL Studio(site4now 웹 도구)는 한글 · BOM 이 든 파일을 읽지 못한다(2026-11-14 확인) — 예전 migrate-*.sql 처럼 ASCII 만 쓴다.
# 한글 값은 CAST(0x<UTF-16LE> AS nvarchar(100)) 로, 안내 문구 · 열 이름은 영어로, 한글 주석은 지운다.
ENGLISH = {
    "N'0 넣기 전'": "N'0 before'", "N'1 넣은 뒤'": "N'1 after insert'", "N'2 부서별'": "N'2 per dept'",
    "N'알림'": "N'note'", "N'반 없음 - 건너뜀'": "N'no class - skipped'",
    "N'가짜 데이터가 이미 있습니다. 먼저 완전 초기화를 실행하세요.'": "N'Fake data already exists. Run the full reset first.'",
    "N'Identity.Role 에 Basic 이 없습니다. 알려 주세요.'": "N'Identity.Role has no Basic. Stop and report.'",
    "N'app.BibleChapter 가 비어 있어 통독 기록은 넣지 않습니다.'": "N'app.BibleChapter is empty: no reading records inserted.'",
    "N'가짜 아이디가 이미 있는 계정과 겹칩니다. 알려 주세요.'": "N'A fake user name already exists. Stop and report.'",
    "N'끝: 저장했습니다.'": "N'DONE: saved.'",
    "N'끝: 되돌렸습니다(저장 안 함).'": "N'DONE: rolled back (not saved).'",
}
ALIASES = {"단계": "Step", "계정": "Accounts", "소속": "Memberships", "명부": "Roster", "계정 이은 명부": "LinkedRoster", "반 배정": "ClassMembers",
           "출석": "Attendance", "미션 기록": "MissionRows", "팀": "Teams", "팀 구성원": "TeamMembers", "시상 기록": "Awards", "보호 관계": "GuardianLinks",
           "통독 회차": "ReadingRounds", "통독 장": "ReadingChapters", "부서": "Dept", "선생님": "Teachers", "학생": "Students", "학부모": "Parents",
           "결과": "Result", "내용": "Message", "메모": "Note"}


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
    "fake-create.sql": """-- fake-create.sql : FAKE TEST DATA IN EXISTING CLASSES (generated by tools/sql/gen-fake-seed.py, 2026-11-16, ADR 0102)
-- Deletes nothing. Classes, existing roster rows and admin accounts are kept.
-- Per department with classes: new students (s01 ~ s40) spread over the latest term's classes, one teacher per class (t01 ~, max 10),
-- fake parents (p01 ~ p10), all with accounts (password 1234), plus per-case missions, attendance, birthdays, reading, teams.
-- Undo: platform full reset (/platform/reset).
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
    """앱 시험 도구(/test-tools/data)용: 시스템 연결이 PlatformScope 를 이미 켠다. 저장 여부 · 기준 날짜는 매개변수."""
    sql = sql.replace(POLICY_OFF, "").replace(POLICY_ON, "")
    for old, new in (("DECLARE @Commit bit = 0;", "DECLARE @Commit bit = @pCommit;"), (f"DECLARE @Today date = '{TODAY}';", "DECLARE @Today date = @pToday;")):
        assert old in sql or "@Today" not in sql, old
        sql = sql.replace(old, new)
    if "@pToday" not in sql:
        sql = sql.replace("DECLARE @Commit bit = @pCommit;", "DECLARE @Commit bit = @pCommit;   -- @pToday is not used here\nDECLARE @Unused date = @pToday;")
    return sql


SSMS_NOTE = "-- For SSMS on the PC (SQL Studio on sql.site4now.net blocks session settings). Set @Commit = 1 to save.\n"
APP_NOTE = "-- Embedded in the web app: run by /test-tools/data (TestToolsService). Parameters: @pCommit bit, @pToday date.\n"
WEB = os.path.normpath(os.path.join(HERE, "..", "..", "src", "ChallengeBible.Web", "TestTools"))

if __name__ == "__main__":
    os.makedirs(WEB, exist_ok=True)
    for name, body in (("fake-create.sql", create_sql()),):
        for folder, flavor, note in ((HERE, ssms, SSMS_NOTE), (WEB, app, APP_NOTE)):
            with open(os.path.join(folder, name), "w", encoding="ascii", newline="\r\n") as f:
                f.write(asciify(flavor(body), TITLES[name] + note))
            print("wrote", os.path.relpath(os.path.join(folder, name), os.path.join(HERE, "..", "..")))
