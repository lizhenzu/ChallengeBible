# -*- coding: utf-8 -*-
"""
가짜 시험 데이터 SQL 생성기(2026-11-14, 사용자 지시 — 선물 시스템 시험용).

만드는 파일(같은 폴더):
  fake-create.sql : superadmin(SuperAdmin 역할)만 남기고 모든 계정 · 기록을 지운 뒤, 부서마다 가짜 데이터를 넣는다
  fake-delete.sql : fake-create.sql 이 넣은 가짜 데이터만 지운다(표시: 이메일 @fake-seed.test, CreatedByUserName = FAKE-SEED-20261114)

다시 만들기:  python3 tools/sql/gen-fake-seed.py
비밀번호 1234 의 해시는 ASP.NET Core Identity PasswordHasher V3(PBKDF2-HMACSHA512, 100,000번, 소금 16바이트)와 같은 모양이다.
소금이 고정이라(다시 만들어도 같은 파일) 시험 데이터에만 쓴다.
"""
import base64, hashlib, os, random, struct

HERE = os.path.dirname(os.path.abspath(__file__))
MARKER = "FAKE-SEED-20261114"
EMAIL_DOMAIN = "fake-seed.test"
TODAY = "2026-11-14"
PEOPLE_PER_DEPT = 69


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


def names(count: int):
    sur = "김이박최정강조윤장임한오서신권황안송류홍전고문양손배백허유남심노하곽성차주우구민나진지엄채원천방공현함변염여추도소석선설마길연위표명기반왕금옥육인맹제모탁국어은편용예경봉사부황보"
    sur = list(dict.fromkeys(sur))[:40]
    first = list("민서지하도예시유주수은채윤현준우나다가소태건승재영동성혜정미선경진")
    second = list("준윤우아연원진호린율빈혁희온별솔찬결람후서민영현수은빛나름결석훈")
    rnd = random.Random(20261114)
    pool = [s + a + b for s in sur for a in first for b in second if a != b]
    rnd.shuffle(pool)
    out, seen = [], set()
    for n in pool:
        if n not in seen:
            seen.add(n); out.append(n)
        if len(out) == count:
            return out
    raise SystemExit("이름이 모자랍니다")


# ── 부서 한 곳의 사람 틀(모든 부서가 같은 틀, 이름 · 아이디만 다르다) ──
# kind: A 관리자 · T 선생님 · S 학생 · P 학부모 · N 새친구(계정 없음)
# cls: 반 번호 1 ~ 10, 0 = 반 없음(미배정반), 99 = 새친구반 담당
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
         19: None, 20: None}                                                                                     # 생일 없음
for i in range(21, 41):
    birth[i] = (((i - 21) % 10) + 1, (i * 7) % 28 + 1)   # 1 ~ 10월에 고루
rates = [100, 95, 90, 80, 65, 50, 35]
absent3 = {10, 21, 35}

tpl = []   # (code, kind, role, has_acct, bm, bd, target, cls, rate, absent3, nc_first, nc_vol, nc_evan, phone)
tpl.append(("admin", "A", "T1005", 1, 3, 12, 400, 99, 0, 0, None, None, None, "010-0000-0001"))
teacher_target = [0, 50, 150, 300, 600, 900, 1200, 0, 250, 1500]
teacher_birth = [(11, 18), (12, 2), (11, 9), (1, 7), (2, 14), (4, 3), (5, 21), (7, 9), (8, 30), (10, 11)]
for i in range(1, 11):
    tpl.append((f"t{i:02d}", "T", "T1002", 1, *teacher_birth[i - 1], teacher_target[i - 1], i, 0, 0, None, None, None, f"010-0000-{100 + i:04d}"))
for i in range(1, 41):
    b = birth[i]
    cls = ((i - 1) % 10) + 1 if i <= 37 else 0
    tpl.append((f"s{i:02d}", "S", "T1001", 1, b[0] if b else None, b[1] if b else None, students_target[i - 1], cls,
                100 if i in (1, 8, 15) else rates[i % len(rates)], 1 if i in absent3 else 0, None, None, None, f"010-0000-{1000 + i:04d}"))

# 가족팀 · 친구팀: (번호, 종류, 팀 점수 목표, 구성원[(코드, 관계 0 학생 · 1 엄마 · 2 아빠)], 준 선물 단계)
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
for i in range(1, 11):
    c = f"p{i:02d}"
    tpl.append((c, "P", None, 1, *parent_birth[i - 1], parent_target[c], 0, 0, 0, None, None, None, f"010-0000-{2000 + i:04d}"))

# 새친구(계정 없음, 역할 T1020): 첫 방문(지난 주일에서 몇 주 전) · 출석한 주(몇 주 전) · 스스로 왔는지 · 데려온 학생
newcomers = [
    ("n1", 0,  [0],                       1, None,  0, (11, 30)),   # 관리 중: 1주째
    ("n2", 1,  [1, 0],                    0, "s14", 0, (5, 5)),     # 관리 중: 2주째 · 초대
    ("n3", 5,  [5, 3, 1],                 1, None,  8, (12, 9)),    # 관리 중: 28일 지났지만 출석 3번
    ("n4", 3,  [3, 2, 1, 0],              0, "s21", 9, (2, 2)),     # 관리 중: 출석 4번이지만 28일 전 · 초대
    ("n5", 8,  [8, 7, 6, 5, 3],           0, "s07", 10, (12, 3)),   # 관리 끝 · 초대
    ("n6", 10, [10, 9, 8, 7, 6, 4, 2, 0], 1, None,  1, (11, 22)),   # 관리 끝
    ("n7", 14, [14, 13, 12, 11],          0, "s25", 2, (8, 8)),     # 관리 끝 · 초대
    ("n8", 20, [20, 18, 17, 16, 10, 5],   1, None,  3, None),       # 관리 끝
]
for code, first, _, vol, evan, cls, b in newcomers:
    tpl.append((code, "N", "T1020", 0, b[0] if b else None, b[1] if b else None, 0, cls, 0, 0, first, vol, evan, f"010-0000-{3000 + int(code[1:]):04d}"))
assert len(tpl) == PEOPLE_PER_DEPT, len(tpl)

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

classes = [(i, f"{(i + 1) // 2}학년 {'믿음반' if i % 2 else '소망반'}") for i in range(1, 11)]


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
       (SELECT COUNT(*) FROM app.ClassMember) AS [반 배정],
       (SELECT COUNT(*) FROM app.AttendanceCheck) AS [출석],
       (SELECT COUNT(*) FROM dbo.DM2_MissionActivity) AS [미션 기록],
       (SELECT COUNT(*) FROM dbo.DM2_FamilyTeam) AS [팀],
       (SELECT COUNT(*) FROM dbo.DM2_FamilyMember) AS [팀 구성원],
       (SELECT COUNT(*) FROM dbo.DM2_TeamAward) AS [시상 기록],
       (SELECT COUNT(*) FROM app.GuardianLink) AS [보호 관계],
       (SELECT COUNT(*) FROM dbo.DM2_ReadingRound) AS [통독 회차],
       (SELECT COUNT(*) FROM dbo.DM2_ReadingChapterRead) AS [통독 장],
       (SELECT COUNT(*) FROM dbo.SignupApplication) AS [참여 신청],
       (SELECT COUNT(*) FROM app.AuditLog) AS [감사 로그];
"""


def create_sql() -> str:
    sur, na, nb = name_parts()
    pw = password_hash("1234")
    team_rows = [(no, kind) for no, kind, *_ in teams]
    team_mem = [(no, c, rel, k + 1, 1 if k == 0 else 0) for no, _, _, mem, _ in teams for k, (c, rel) in enumerate(mem)]
    awards = [(no, th) for no, *_, given in teams for th in given]
    nc_visits = [(code, w) for code, _, ws, *_ in newcomers for w in ws]
    return HEADER + f"""-- ============================================================================
-- 가짜 시험 데이터 만들기 (생성기 tools/sql/gen-fake-seed.py, 2026-11-14)
-- 1) superadmin(SuperAdmin 역할 계정)만 남기고 모든 계정과 그 기록을 지운다
--    (소속 · 명부 · 반 배정 · 출석 · 미션 · 퀴즈 · 통독 · 가족팀 · 친구팀 · 시상 · 신청 · 보호 관계 · 진급 신청 · 감사 로그)
--    부서 · 반 · 시상 기준 · 말씀 · 퀴즈 · 기도 · 암송 · 성경 같은 콘텐츠와 설정은 남긴다
-- 2) "처음 오신 분"(임시 부서)을 뺀 모든 부서에 같은 틀로 가짜 사람 {PEOPLE_PER_DEPT}명과 기록을 넣는다
--    관리자 1 · 선생님 10 · 학생 40 · 학부모 10 · 새친구 8(계정 없음), 반 10개(반마다 학생 3 ~ 5명 + 선생님 1명)
--    아이디 = 부서 주소 + "-" + admin · t01 · s01 · p01, 비밀번호 모두 1234
-- 실행: 먼저 @Commit = 0(시험 실행, 끝에 모두 되돌림) → 결과 표 확인 → @Commit = 1 로 바꿔 다시 실행
-- 지우기: tools/sql/fake-delete.sql
-- ============================================================================
DECLARE @Commit bit = 0;                 -- 0 = 시험 실행(끝에 되돌림), 1 = 진짜 실행
DECLARE @Today date = '{TODAY}';         -- 기준 날짜(부서 날짜). 다른 날 실행하면 바꾼다 — 생일 · 새친구 · 통독 날짜가 이 날 기준
DECLARE @Start date = DATEFROMPARTS(YEAR(@Today), 1, 1);
DECLARE @Marker nvarchar(50) = N'{MARKER}';
DECLARE @MarkTime datetime2(3) = '2026-01-01T00:00:00.123';   -- 가짜 반 · 반 묶음 표시(만든 시각 칸)
DECLARE @Pw nvarchar(max) = N'{pw}';     -- 1234
DECLARE @Step nvarchar(20);

EXEC sp_set_session_context N'PlatformScope', 1;   -- 부서 격리(RLS)를 이 연결에서만 넘는다

BEGIN TRY
BEGIN TRANSACTION;

SET @Step = N'0 지우기 전';
{COUNTS}
-- ── 1. 남길 계정: SuperAdmin 역할 ──
CREATE TABLE #Keep (UserId nvarchar(450) COLLATE DATABASE_DEFAULT PRIMARY KEY, UserName nvarchar(256) COLLATE DATABASE_DEFAULT);
INSERT #Keep SELECT u.Id, u.UserName FROM [Identity].[User] u
WHERE EXISTS (SELECT 1 FROM [Identity].[UserRoles] ur JOIN [Identity].[Role] r ON r.Id = ur.RoleId WHERE ur.UserId = u.Id AND r.NormalizedName = N'SUPERADMIN');
IF NOT EXISTS (SELECT 1 FROM #Keep) THROW 50001, N'SuperAdmin 계정이 없습니다. 실행하지 말고 알려 주세요.', 1;
SELECT N'1 남는 계정' AS [단계], UserName AS [아이디] FROM #Keep;

-- ── 2. 지우기 ──
DELETE FROM app.AttendanceCheck;
DELETE FROM app.ClassMember;
DELETE FROM app.Class WHERE CreatedUtc = @MarkTime;   -- 앞서 이 파일로 만든 가짜 반(다시 실행할 때)
DELETE t FROM app.ClassTerm t WHERE t.CreatedUtc = @MarkTime AND NOT EXISTS (SELECT 1 FROM app.Class c WHERE c.TenantId = t.TenantId AND c.TermId = t.Id);
DELETE FROM dbo.DM2_FamilyRequest;
DELETE FROM dbo.DM2_FamilyNotice;
DELETE FROM dbo.DM2_FamilyMilestoneHidden;
DELETE FROM dbo.DM2_TeamAward;
DELETE FROM dbo.DM2_FamilyMember;
DELETE FROM dbo.DM2_FamilyTeam;
DELETE FROM app.GuardianLink;
DELETE FROM app.PromotionRequest WHERE UserName NOT IN (SELECT UserName FROM #Keep);
DELETE FROM dbo.DM2_QuizAttempt WHERE UserName NOT IN (SELECT UserName FROM #Keep);
DELETE FROM dbo.DM2_MissionActivity WHERE UserName NOT IN (SELECT UserName FROM #Keep);
DELETE FROM dbo.DM2_ReadingVerse WHERE UserName NOT IN (SELECT UserName FROM #Keep);
DELETE FROM dbo.DM2_ReadingChapterRead WHERE UserName NOT IN (SELECT UserName FROM #Keep);
DELETE FROM dbo.DM2_ReadingChapterProgress WHERE UserName NOT IN (SELECT UserName FROM #Keep);
DELETE FROM dbo.DM2_ReadingBookmark WHERE UserName NOT IN (SELECT UserName FROM #Keep);
DELETE FROM dbo.DM2_ReadingRound WHERE UserName NOT IN (SELECT UserName FROM #Keep);
DELETE FROM app.Person WHERE UserId IS NULL OR UserId NOT IN (SELECT UserId FROM #Keep);
DELETE FROM dbo.SignupApplication WHERE UserId NOT IN (SELECT UserId FROM #Keep);
DELETE FROM dbo.DeptRoleUserAssoc WHERE UserId IS NULL OR UserId NOT IN (SELECT UserId FROM #Keep);
DELETE FROM app.UserSetting WHERE UserName NOT IN (SELECT UserName FROM #Keep);
DELETE FROM app.AuditLog WHERE ActorUserName NOT IN (SELECT UserName FROM #Keep);
DELETE FROM [Identity].[UserTokens] WHERE UserId NOT IN (SELECT UserId FROM #Keep);
DELETE FROM [Identity].[UserLogins] WHERE UserId NOT IN (SELECT UserId FROM #Keep);
DELETE FROM [Identity].[UserClaims] WHERE UserId NOT IN (SELECT UserId FROM #Keep);
DELETE FROM [Identity].[UserRoles] WHERE UserId NOT IN (SELECT UserId FROM #Keep);
DELETE FROM [Identity].[User] WHERE Id NOT IN (SELECT UserId FROM #Keep);

SET @Step = N'2 지운 뒤';
{COUNTS}
-- ── 3. 틀 ──
DECLARE @RoleBasic nvarchar(450) = (SELECT Id FROM [Identity].[Role] WHERE NormalizedName = N'BASIC');
DECLARE @RoleAdmin nvarchar(450) = (SELECT Id FROM [Identity].[Role] WHERE NormalizedName = N'ADMIN');
IF @RoleBasic IS NULL OR @RoleAdmin IS NULL THROW 50002, N'Identity.Role 에 Basic · Admin 이 없습니다. 알려 주세요.', 1;

CREATE TABLE #Name (Idx int PRIMARY KEY, Name nvarchar(50) COLLATE DATABASE_DEFAULT);
-- names: surname + 2 syllables, all different, mixed order
DECLARE @Sur nvarchar(100) = N'{sur}', @Na nvarchar(100) = N'{na}', @Nb nvarchar(100) = N'{nb}';
;WITH k AS (SELECT TOP (100) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_columns),
n AS (SELECT DISTINCT SUBSTRING(@Sur, s.i, 1) + SUBSTRING(@Na, a.i, 1) + SUBSTRING(@Nb, b.i, 1) AS Name
      FROM k s JOIN k a ON a.i <= LEN(@Na) JOIN k b ON b.i <= LEN(@Nb)
      WHERE s.i <= LEN(@Sur) AND SUBSTRING(@Na, a.i, 1) <> SUBSTRING(@Nb, b.i, 1))
INSERT #Name (Idx, Name) SELECT TOP (3000) ROW_NUMBER() OVER (ORDER BY HASHBYTES('MD5', Name), Name) - 1, Name FROM n ORDER BY HASHBYTES('MD5', Name), Name;

CREATE TABLE #Tpl (Seq int PRIMARY KEY, Code varchar(10) COLLATE DATABASE_DEFAULT, Kind char(1) COLLATE DATABASE_DEFAULT, RoleCode nvarchar(50) COLLATE DATABASE_DEFAULT, HasAcct bit,
    BM tinyint, BD tinyint, Target int, ClassNo int, AttRate int, Absent3 bit, NcFirst int, NcVol bit, NcEvan varchar(10) COLLATE DATABASE_DEFAULT, Phone varchar(20) COLLATE DATABASE_DEFAULT);
INSERT #Tpl VALUES
{values([(k,) + r for k, r in enumerate(tpl)])};

CREATE TABLE #NcVisit (Code varchar(10) COLLATE DATABASE_DEFAULT, WeeksAgo int);
INSERT #NcVisit VALUES
{values(nc_visits)};

CREATE TABLE #Cls (ClassNo int PRIMARY KEY, Name nvarchar(50) COLLATE DATABASE_DEFAULT, FinalName nvarchar(50) COLLATE DATABASE_DEFAULT, ClassId int);
INSERT #Cls (ClassNo, Name) VALUES
{values(classes)};

CREATE TABLE #Team (TeamNo int PRIMARY KEY, TeamType varchar(10) COLLATE DATABASE_DEFAULT, TeamName nvarchar(50) COLLATE DATABASE_DEFAULT, TeamId int);
INSERT #Team (TeamNo, TeamType) VALUES
{values(team_rows)};
CREATE TABLE #TeamMem (TeamNo int, Code varchar(10) COLLATE DATABASE_DEFAULT, Rel tinyint, SortNo int, IsCreator bit);
INSERT #TeamMem VALUES
{values(team_mem)};
CREATE TABLE #Award (TeamNo int, Threshold int);
INSERT #Award VALUES
{values(awards)};

CREATE TABLE #Read (Code varchar(10) COLLATE DATABASE_DEFAULT, RoundNo int, StartAgo int, EndAgo int NULL, Chapters int NULL);
INSERT #Read VALUES
{values(reading)};

-- 미션 칸: 1월 1일 ~ 어제, 지금 규칙에서 켜진 항목(규칙 표가 비면 6개 모두). 암송은 주마다 하나(주 시작 = 일요일)
CREATE TABLE #Item (Item varchar(20) COLLATE DATABASE_DEFAULT);
DECLARE @Rule nvarchar(max) = (SELECT TOP 1 [Values] FROM app.PlatformRuleSetVersion WHERE EffectiveFrom <= @Today ORDER BY VersionNo DESC);
IF @Rule IS NULL
    INSERT #Item VALUES ('Word.Me'), ('Word.Parent'), ('Prayer.Me'), ('Prayer.Others'), ('Quiz.Correct'), ('Recite');
ELSE
    INSERT #Item SELECT CASE j.Item WHEN 'WordMe' THEN 'Word.Me' WHEN 'WordParent' THEN 'Word.Parent' WHEN 'PrayerMe' THEN 'Prayer.Me'
                                    WHEN 'PrayerOthers' THEN 'Prayer.Others' WHEN 'QuizCorrect' THEN 'Quiz.Correct' WHEN 'Recite' THEN 'Recite' END
    FROM OPENJSON(@Rule, '$.Items') WITH (Item varchar(20) '$.Item', Enabled bit '$.Enabled') j WHERE j.Enabled = 1;
DELETE FROM #Item WHERE Item IS NULL;

CREATE TABLE #Day (D date PRIMARY KEY);
INSERT #Day SELECT TOP (DATEDIFF(day, @Start, @Today)) DATEADD(day, ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1, @Start)
FROM sys.all_columns a CROSS JOIN sys.all_columns b;
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

CREATE TABLE #P (Code varchar(10) COLLATE DATABASE_DEFAULT PRIMARY KEY, Kind char(1) COLLATE DATABASE_DEFAULT, RoleCode nvarchar(50) COLLATE DATABASE_DEFAULT, HasAcct bit,
    BM tinyint, BD tinyint, Target int, ClassNo int, AttRate int, Absent3 bit, NcFirst int, NcVol bit, NcEvan varchar(10) COLLATE DATABASE_DEFAULT, Phone varchar(20) COLLATE DATABASE_DEFAULT,
    UserName nvarchar(256) COLLATE DATABASE_DEFAULT, Name nvarchar(50) COLLATE DATABASE_DEFAULT, UserId nvarchar(450) COLLATE DATABASE_DEFAULT, PersonId int);

-- ── 4. 부서마다 넣기 ──
DECLARE @T int, @Slug nvarchar(200), @Ord int = 0, @Term int, @NcClass int;
DECLARE tc CURSOR LOCAL FAST_FORWARD FOR SELECT Id, LOWER(Slug) FROM app.Tenant WHERE IsLobby = 0 ORDER BY Id;
OPEN tc;
FETCH NEXT FROM tc INTO @T, @Slug;
WHILE @@FETCH_STATUS = 0
BEGIN
    TRUNCATE TABLE #P;
    INSERT #P SELECT t.Code, t.Kind, t.RoleCode, t.HasAcct, t.BM, t.BD, t.Target, t.ClassNo, t.AttRate, t.Absent3, t.NcFirst, t.NcVol, t.NcEvan, t.Phone,
                     CASE WHEN t.HasAcct = 1 THEN @Slug + N'-' + t.Code END, n.Name, CASE WHEN t.HasAcct = 1 THEN CONVERT(nvarchar(450), NEWID()) END, NULL
    FROM #Tpl t JOIN #Name n ON n.Idx = @Ord * {PEOPLE_PER_DEPT} + t.Seq;
    IF (SELECT COUNT(*) FROM #P) <> {PEOPLE_PER_DEPT} THROW 50003, N'이름이 모자랍니다(부서가 너무 많음). 알려 주세요.', 1;
    IF EXISTS (SELECT 1 FROM #P p JOIN [Identity].[User] u ON u.NormalizedUserName = UPPER(p.UserName) OR u.NormalizedEmail = UPPER(p.UserName + N'@{EMAIL_DOMAIN}'))
        THROW 50004, N'가짜 아이디가 이미 있는 계정과 겹칩니다. 알려 주세요.', 1;

    -- 계정
    INSERT [Identity].[User] (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp,
        PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount, FirstName, LastName, ProfilePicture, UsernameChangeLimit, insertDT)
    SELECT UserId, UserName, UPPER(UserName), UserName + N'@{EMAIL_DOMAIN}', UPPER(UserName + N'@{EMAIL_DOMAIN}'), 1, @Pw, REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), CONVERT(nvarchar(36), NEWID()),
        NULL, 0, 0, NULL, 1, 0, Name, N'', NULL, 0, '2026-01-02T00:00:00'
    FROM #P WHERE HasAcct = 1;
    INSERT [Identity].[UserRoles] (UserId, RoleId) SELECT UserId, @RoleBasic FROM #P WHERE HasAcct = 1;
    INSERT [Identity].[UserRoles] (UserId, RoleId) SELECT UserId, @RoleAdmin FROM #P WHERE Kind = 'A';
    INSERT app.UserSetting (UserName, MustChangePassword, LastTenantId) SELECT UserName, 0, @T FROM #P WHERE HasAcct = 1;
    INSERT dbo.DeptRoleUserAssoc (TenantId, RoleCode, UserId, InsertDT, updateDT) SELECT @T, RoleCode, UserId, '2026-01-02T00:00:00', '2026-01-02T00:00:00' FROM #P WHERE HasAcct = 1;

    -- 명부(계정과 이은 줄은 역할 코드를 비운다 — 역할은 소속에서 읽는다)
    INSERT app.Person (TenantId, Name, RoleCode, BirthMonth, BirthDay, Phone1, UserId, CreatedUtc, CreatedByUserName, NewcomerFirstVisit, NewcomerVoluntary, NewcomerEvangelist)
    SELECT @T, p.Name, CASE WHEN p.HasAcct = 1 THEN NULL ELSE p.RoleCode END, p.BM, p.BD, p.Phone, p.UserId, '2026-01-02T00:00:00', @Marker,
           CASE WHEN p.Kind = 'N' THEN DATEADD(week, -p.NcFirst, @L) END, CASE WHEN p.Kind = 'N' THEN p.NcVol END, e.Name
    FROM #P p LEFT JOIN #P e ON e.Code = p.NcEvan;
    UPDATE p SET PersonId = x.Id FROM #P p JOIN app.Person x ON x.TenantId = @T AND x.Name = p.Name AND x.CreatedByUserName = @Marker;

    -- 반 묶음 · 새친구반(없으면 만든다) · 가짜 반 10개
    SET @Term = (SELECT TOP 1 Id FROM app.ClassTerm WHERE TenantId = @T ORDER BY Id);
    IF @Term IS NULL
    BEGIN
        INSERT app.ClassTerm (TenantId, Year, StartDate, CreatedUtc) VALUES (@T, YEAR(@Today), @Start, @MarkTime);
        SET @Term = SCOPE_IDENTITY();
    END;
    SET @NcClass = (SELECT TOP 1 Id FROM app.Class WHERE TenantId = @T AND TermId = @Term AND IsNewcomer = 1 ORDER BY Id);
    IF @NcClass IS NULL
    BEGIN
        INSERT app.Class (TenantId, TermId, Name, IsNewcomer, CreatedUtc) VALUES (@T, @Term, N'새친구반', 1, @MarkTime);
        SET @NcClass = SCOPE_IDENTITY();
    END;
    UPDATE c SET FinalName = CASE WHEN EXISTS (SELECT 1 FROM app.Class x WHERE x.TenantId = @T AND x.TermId = @Term AND x.Name = c.Name) THEN c.Name + N' (시험)' ELSE c.Name END, ClassId = NULL FROM #Cls c;
    INSERT app.Class (TenantId, TermId, Name, IsNewcomer, CreatedUtc) SELECT @T, @Term, FinalName, 0, @MarkTime FROM #Cls;
    UPDATE c SET ClassId = x.Id FROM #Cls c JOIN app.Class x ON x.TenantId = @T AND x.TermId = @Term AND x.Name = c.FinalName AND x.CreatedUtc = @MarkTime;

    INSERT app.ClassMember (TenantId, TermId, ClassId, PersonId, IsTeacher, CreatedUtc)
    SELECT @T, @Term, CASE WHEN p.ClassNo = 99 THEN @NcClass ELSE c.ClassId END, p.PersonId, CASE WHEN p.Kind IN ('A', 'T') THEN 1 ELSE 0 END, @MarkTime
    FROM #P p LEFT JOIN #Cls c ON c.ClassNo = p.ClassNo WHERE p.ClassNo > 0;

    -- 출석: 반에 든 학생은 주일마다 랜덤(출석률), 3주 결석 아이는 최근 3주 결석. 새친구는 정한 주만
    INSERT app.AttendanceCheck (TenantId, PersonId, Sunday, CreatedUtc, CreatedByUserName)
    SELECT @T, p.PersonId, s.D, DATEADD(hour, 2, CAST(s.D AS datetime2(3))), @Marker
    FROM #P p CROSS JOIN #Sun s
    WHERE p.Kind = 'S' AND p.ClassNo BETWEEN 1 AND 10 AND NOT (p.Absent3 = 1 AND s.WeeksAgo <= 2)
      AND ABS(CHECKSUM(@T, p.Code, s.D)) % 100 < p.AttRate;
    INSERT app.AttendanceCheck (TenantId, PersonId, Sunday, CreatedUtc, CreatedByUserName)
    SELECT @T, p.PersonId, DATEADD(week, -v.WeeksAgo, @L), DATEADD(hour, 2, CAST(DATEADD(week, -v.WeeksAgo, @L) AS datetime2(3))), @Marker
    FROM #NcVisit v JOIN #P p ON p.Code = v.Code;

    -- 미션 기록: 사람마다 목표 점수를 정확히 맞춘다. 한 줄 점수 = 1 ~ 10(그때 규칙이 그랬던 것처럼), 날짜 · 항목은 랜덤
    ;WITH ppl AS (
        SELECT p.UserName, p.Target, CASE WHEN x.c < 1 THEN 1 WHEN x.c > 10 THEN 10 ELSE x.c END AS P
        FROM #P p CROSS APPLY (SELECT CAST(CEILING(p.Target / (@NSlot * 0.6)) AS int) AS c) x
        WHERE p.HasAcct = 1 AND p.Target > 0),
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

    -- 가족팀 · 친구팀(방장 = 첫 구성원), 보호 관계(가족팀 학부모 → 학생), 준 선물 기록(시상 잠금)
    UPDATE t SET TeamName = CASE WHEN t.TeamType = 'FAMILY' THEN a.Name + N'네 가족' ELSE a.Name + N' · ' + b.Name + N' 친구팀' END, TeamId = NULL
    FROM #Team t
    JOIN #TeamMem ma ON ma.TeamNo = t.TeamNo AND ma.SortNo = 1 JOIN #P a ON a.Code = ma.Code
    LEFT JOIN #TeamMem mb ON mb.TeamNo = t.TeamNo AND mb.SortNo = 2 LEFT JOIN #P b ON b.Code = mb.Code;
    INSERT dbo.DM2_FamilyTeam (TenantId, TeamName, IsActive, Memo, InsertDT, UpdateDT, AwardKey, CreatorUserName, TeamType)
    SELECT @T, t.TeamName, 1, NULL, '2026-01-10T00:00:00', '2026-01-10T00:00:00', NULL, p.UserName, t.TeamType
    FROM #Team t JOIN #TeamMem m ON m.TeamNo = t.TeamNo AND m.IsCreator = 1 JOIN #P p ON p.Code = m.Code;
    UPDATE t SET TeamId = x.FamilyTeamId FROM #Team t JOIN dbo.DM2_FamilyTeam x ON x.TenantId = @T AND x.TeamName = t.TeamName;
    INSERT dbo.DM2_FamilyMember (TenantId, FamilyTeamId, UserName, RoleCode, SortNo, IsActive, InsertDT, UpdateDT, RelationCode, RelationLabel)
    SELECT @T, t.TeamId, p.UserName, m.Rel, m.SortNo, 1, '2026-01-10T00:00:00', '2026-01-10T00:00:00', 9, NULL
    FROM #TeamMem m JOIN #Team t ON t.TeamNo = m.TeamNo JOIN #P p ON p.Code = m.Code;
    INSERT app.GuardianLink (TenantId, GuardianUserName, StudentUserName, CreatedUtc, CreatedByUserName)
    SELECT @T, g.UserName, s.UserName, '2026-01-10T00:00:00', @Marker
    FROM #Team t
    JOIN #TeamMem mg ON mg.TeamNo = t.TeamNo AND mg.Rel > 0 JOIN #P g ON g.Code = mg.Code
    JOIN #TeamMem ms ON ms.TeamNo = t.TeamNo AND ms.Rel = 0 JOIN #P s ON s.Code = ms.Code
    WHERE t.TeamType = 'FAMILY';
    INSERT dbo.DM2_TeamAward (TenantId, Kind, AwardKey, Threshold, AwardedAt)
    SELECT @T, t.TeamType, t.TeamName, a.Threshold, DATEADD(day, a.Threshold / 100, '2026-03-01T10:00:00')
    FROM #Award a JOIN #Team t ON t.TeamNo = a.TeamNo;

    SET @Ord += 1;
    FETCH NEXT FROM tc INTO @T, @Slug;
END;
CLOSE tc;
DEALLOCATE tc;

SET @Step = N'3 넣은 뒤';
{COUNTS}
-- ── 5. 부서마다 로그인 아이디(비밀번호 모두 1234) ──
SELECT N'4 아이디' AS [단계], t.Name AS [부서],
       LOWER(t.Slug) + N'-admin' AS [관리자], LOWER(t.Slug) + N'-t01 ~ t10' AS [선생님], LOWER(t.Slug) + N'-s01 ~ s40' AS [학생], LOWER(t.Slug) + N'-p01 ~ p10' AS [학부모],
       (SELECT COUNT(*) FROM app.Person x WHERE x.TenantId = t.Id AND x.CreatedByUserName = @Marker) AS [명부 수]
FROM app.Tenant t WHERE t.IsLobby = 0 ORDER BY t.Id;

IF @Commit = 1
BEGIN
    COMMIT;
    SELECT N'끝: 저장했습니다(진짜 실행).' AS [결과];
END
ELSE
BEGIN
    ROLLBACK;
    SELECT N'끝: 시험 실행이라 모두 되돌렸습니다. 위 숫자가 맞으면 @Commit = 1 로 바꿔 다시 실행하세요.' AS [결과];
END;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    EXEC sp_set_session_context N'PlatformScope', NULL;
    THROW;
END CATCH;
EXEC sp_set_session_context N'PlatformScope', NULL;
"""


def delete_sql() -> str:
    return HEADER + f"""-- ============================================================================
-- 가짜 시험 데이터 지우기 (생성기 tools/sql/gen-fake-seed.py, 2026-11-14)
-- fake-create.sql 이 넣은 것만 지운다: 이메일 @{EMAIL_DOMAIN} 계정과 그 기록, 명부 · 출석 · 보호 관계(CreatedByUserName = {MARKER}),
-- 가짜 계정이 만든 가족팀 · 친구팀과 그 시상 기록, 가짜 반(만든 시각 2026-01-01 00:00:00.123)
-- 실행: 먼저 @Commit = 0(시험 실행) → 결과 표 확인 → @Commit = 1
-- ============================================================================
DECLARE @Commit bit = 0;                 -- 0 = 시험 실행(끝에 되돌림), 1 = 진짜 실행
DECLARE @Marker nvarchar(50) = N'{MARKER}';
DECLARE @MarkTime datetime2(3) = '2026-01-01T00:00:00.123';
DECLARE @Step nvarchar(20);

EXEC sp_set_session_context N'PlatformScope', 1;

BEGIN TRY
BEGIN TRANSACTION;

SET @Step = N'0 지우기 전';
{COUNTS}
CREATE TABLE #Del (UserId nvarchar(450) COLLATE DATABASE_DEFAULT PRIMARY KEY, UserName nvarchar(256) COLLATE DATABASE_DEFAULT);
INSERT #Del SELECT Id, UserName FROM [Identity].[User] WHERE Email LIKE N'%@{EMAIL_DOMAIN}';
CREATE TABLE #DelTeam (TenantId int, TeamId int, AwardKey nvarchar(200) COLLATE DATABASE_DEFAULT);
INSERT #DelTeam SELECT TenantId, FamilyTeamId, ISNULL(AwardKey, TeamName) FROM dbo.DM2_FamilyTeam WHERE CreatorUserName IN (SELECT UserName FROM #Del);
CREATE TABLE #DelPerson (TenantId int, PersonId int);
INSERT #DelPerson SELECT TenantId, Id FROM app.Person WHERE CreatedByUserName = @Marker OR UserId IN (SELECT UserId FROM #Del);

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
DELETE FROM app.AuditLog WHERE ActorUserName IN (SELECT UserName FROM #Del);
DELETE FROM [Identity].[UserTokens] WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM [Identity].[UserLogins] WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM [Identity].[UserClaims] WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM [Identity].[UserRoles] WHERE UserId IN (SELECT UserId FROM #Del);
DELETE FROM [Identity].[User] WHERE Id IN (SELECT UserId FROM #Del);

-- 가짜 반: 남은 사람이 없을 때만 지운다(누가 반에 넣었으면 남김)
DELETE c FROM app.Class c WHERE c.CreatedUtc = @MarkTime
  AND NOT EXISTS (SELECT 1 FROM app.ClassMember m WHERE m.TenantId = c.TenantId AND m.ClassId = c.Id);
DELETE t FROM app.ClassTerm t WHERE t.CreatedUtc = @MarkTime
  AND NOT EXISTS (SELECT 1 FROM app.Class c WHERE c.TenantId = t.TenantId AND c.TermId = t.Id);

SET @Step = N'1 지운 뒤';
{COUNTS}
IF @Commit = 1
BEGIN
    COMMIT;
    SELECT N'끝: 지웠습니다(진짜 실행).' AS [결과];
END
ELSE
BEGIN
    ROLLBACK;
    SELECT N'끝: 시험 실행이라 모두 되돌렸습니다. 위 숫자가 맞으면 @Commit = 1 로 바꿔 다시 실행하세요.' AS [결과];
END;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    EXEC sp_set_session_context N'PlatformScope', NULL;
    THROW;
END CATCH;
EXEC sp_set_session_context N'PlatformScope', NULL;
"""


# SQL Studio(site4now 웹 도구)는 한글 · BOM 이 든 파일을 읽지 못한다(2026-11-14 확인) — 예전 migrate-*.sql 처럼 ASCII 만 쓴다.
# 한글 값은 CAST(0x<UTF-16LE> AS nvarchar(100)) 로, 안내 문구 · 열 이름은 영어로, 한글 주석은 지운다.
ENGLISH = {
    "N'0 지우기 전'": "N'0 before'", "N'2 지운 뒤'": "N'2 after delete'", "N'3 넣은 뒤'": "N'3 after insert'", "N'1 지운 뒤'": "N'1 after delete'",
    "N'1 남는 계정'": "N'1 kept account'", "N'4 아이디'": "N'4 login ids'", "N'알림'": "N'note'",
    "N'SuperAdmin 계정이 없습니다. 실행하지 말고 알려 주세요.'": "N'No SuperAdmin account. Stop and report.'",
    "N'Identity.Role 에 Basic · Admin 이 없습니다. 알려 주세요.'": "N'Identity.Role has no Basic or Admin. Stop and report.'",
    "N'app.BibleChapter 가 비어 있어 통독 기록은 넣지 않습니다.'": "N'app.BibleChapter is empty: no reading records inserted.'",
    "N'이름이 모자랍니다(부서가 너무 많음). 알려 주세요.'": "N'Not enough names (too many departments). Stop and report.'",
    "N'가짜 아이디가 이미 있는 계정과 겹칩니다. 알려 주세요.'": "N'A fake user name already exists. Stop and report.'",
    "N'끝: 저장했습니다(진짜 실행).'": "N'DONE: saved (real run).'",
    "N'끝: 지웠습니다(진짜 실행).'": "N'DONE: deleted (real run).'",
    "N'끝: 시험 실행이라 모두 되돌렸습니다. 위 숫자가 맞으면 @Commit = 1 로 바꿔 다시 실행하세요.'":
        "N'DONE: test run, everything rolled back. If the numbers look right, set @Commit = 1 and run again.'",
}
ALIASES = {"단계": "Step", "계정": "Accounts", "소속": "Memberships", "명부": "Roster", "반 배정": "ClassMembers", "출석": "Attendance",
           "미션 기록": "MissionRows", "팀": "Teams", "팀 구성원": "TeamMembers", "시상 기록": "Awards", "보호 관계": "GuardianLinks",
           "통독 회차": "ReadingRounds", "통독 장": "ReadingChapters", "참여 신청": "Signups", "감사 로그": "AuditLogs", "아이디": "UserName",
           "부서": "Dept", "관리자": "Admin", "선생님": "Teachers", "학생": "Students", "학부모": "Parents", "명부 수": "RosterCount",
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
    "fake-create.sql": f"""-- fake-create.sql : FAKE TEST DATA (generated by tools/sql/gen-fake-seed.py, 2026-11-14)
-- 1) Deletes every account except SuperAdmin accounts, and all their records.
-- 2) Inserts {PEOPLE_PER_DEPT} fake people per department (not the lobby). Password for all: 1234.
-- Run with @Commit = 0 first (test run, rolled back). Check the result tables. Then set @Commit = 1 and run again.
-- Undo later: tools/sql/fake-delete.sql
""",
    "fake-delete.sql": f"""-- fake-delete.sql : DELETE FAKE TEST DATA (generated by tools/sql/gen-fake-seed.py, 2026-11-14)
-- Deletes only what fake-create.sql inserted (email @{EMAIL_DOMAIN}, CreatedByUserName = {MARKER}).
-- Run with @Commit = 0 first (test run, rolled back). Then set @Commit = 1.
""",
}

if __name__ == "__main__":
    for name, body in (("fake-create.sql", create_sql()), ("fake-delete.sql", delete_sql())):
        with open(os.path.join(HERE, name), "w", encoding="ascii", newline="\r\n") as f:
            f.write(asciify(body, TITLES[name]))
        print("wrote", name)
