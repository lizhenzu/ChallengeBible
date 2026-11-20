# 유년부 이관: 예전 DB 에서 돌릴 뽑기 SQL(yuyunbu-export.sql)을 만든다. 넣기 본문(import-body.sql)은 줄마다 결과 줄로 넣는다.
# 실행: python3 tools/sql/yuyunbu/build.py  → tools/sql/yuyunbu-export.sql
import pathlib
here = pathlib.Path(__file__).parent
body = (here / 'import-body.sql').read_text(encoding='utf-8-sig').splitlines()

def q(x):   # 글자 → N'..' 또는 NULL
    return f"ISNULL(N'N''' + REPLACE(CONVERT(nvarchar(max), {x}), N'''', N'''''') + N'''', N'NULL')"
def n(x):   # 숫자 · 참거짓
    return f"ISNULL(CONVERT(nvarchar(20), {x}), N'NULL')"
def d(x):   # 날짜
    return f"ISNULL(N'''' + CONVERT(nvarchar(30), {x}, 126) + N'''', N'NULL')"
def row(name, cols):
    return "N'INSERT " + name + " VALUES (' + " + " + N', ' + ".join(cols) + " + N');'"
def lit(s):
    return "N'" + s.replace("'", "''") + "'"

out = []
w = out.append
w("-- yuyunbu-export.sql : 예전 유년부 DB(SSMS)에서 실행 → 나온 결과(s 칸)를 모두 복사해 새 사이트 [SQL 실행]에 붙여 넣는다 (ADR 0120)")
w("-- 이 파일은 tools/sql/yuyunbu/build.py 로 만든다. 고칠 때는 import-body.sql · build.py 를 고친다.")
w("-- 결과 보기: [결과를 표로](Ctrl+D) → 결과 칸 왼쪽 위를 눌러 모두 고르기 → 복사(Ctrl+C). 머리 줄(s)은 복사되지 않는다.")
w("-- 예전 DB 는 읽기만 한다(아무것도 바꾸지 않음).")
w("SET NOCOUNT ON;")
w("DECLARE @L TABLE (n int IDENTITY(1,1) PRIMARY KEY, s nvarchar(max));")
w("DECLARE @sa TABLE (Id nvarchar(450) PRIMARY KEY);")
w("INSERT @sa SELECT DISTINCT ur.UserId FROM [Identity].[UserRoles] ur JOIN [Identity].[Role] r ON r.Id = ur.RoleId WHERE r.NormalizedName = N'SUPERADMIN';")
w("DECLARE @role TABLE (UserId nvarchar(450) PRIMARY KEY, RoleCode nvarchar(50));")
w("INSERT @role SELECT UserId, RoleCode FROM (SELECT UserId, RoleCode, ROW_NUMBER() OVER (PARTITION BY UserId ORDER BY CASE WHEN RoleCode = N'T1099' THEN 1 ELSE 0 END, updateDT DESC, Id DESC) k FROM dbo.DeptRoleUserAssoc WHERE UserId IS NOT NULL AND NULLIF(LTRIM(RTRIM(RoleCode)), N'') IS NOT NULL) x WHERE k = 1;")
w("DECLARE @users TABLE (Id nvarchar(450) PRIMARY KEY, UserName nvarchar(256));")
w("INSERT @users SELECT u.Id, u.UserName FROM [Identity].[User] u WHERE u.Id NOT IN (SELECT Id FROM @sa) AND NULLIF(LTRIM(RTRIM(u.UserName)), N'') IS NOT NULL;")
w("DECLARE @teach TABLE (Pid int PRIMARY KEY);")
w("INSERT @teach SELECT DISTINCT Pid FROM (SELECT TeacherPersonId Pid FROM dbo.ClassTeacherMap_2026 WHERE IsActive = 1 UNION SELECT SubTeacherPersonId FROM dbo.ClassTeacherMap_2026 WHERE IsActive = 1 AND SubTeacherPersonId IS NOT NULL) t;")
w("DECLARE @people TABLE (Pid int PRIMARY KEY, Name nvarchar(50), UserName nvarchar(256), RoleCode nvarchar(50), BM tinyint, BD tinyint, Ph1 nvarchar(20), Ph2 nvarchar(20));")
w("""INSERT @people
SELECT p.PersonId, LEFT(LTRIM(RTRIM(p.fullName)), 50), NULLIF(LTRIM(RTRIM(p.UserName)), N''),
       COALESCE(CASE WHEN p.PersonId IN (SELECT Pid FROM @teach) THEN N'T1002' END, r.RoleCode, CASE WHEN EXISTS (SELECT 1 FROM dbo.ClassInfo_2026 c WHERE c.PersonID = p.PersonId) THEN N'T1001' END, N'T1099'),
       CASE WHEN p.bMonth BETWEEN 1 AND 12 AND p.bDay BETWEEN 1 AND 31 THEN p.bMonth END, CASE WHEN p.bMonth BETWEEN 1 AND 12 AND p.bDay BETWEEN 1 AND 31 THEN p.bDay END,
       LEFT(CASE WHEN x.p1 NOT LIKE N'%[^0-9]%' AND LEN(x.p1) = 11 THEN STUFF(STUFF(x.p1, 8, 0, N'-'), 4, 0, N'-') ELSE x.p1 END, 20),
       LEFT(CASE WHEN x.p2 NOT LIKE N'%[^0-9]%' AND LEN(x.p2) = 11 THEN STUFF(STUFF(x.p2, 8, 0, N'-'), 4, 0, N'-') ELSE x.p2 END, 20)
FROM dbo.PersonMaster_2026 p
CROSS APPLY (SELECT NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100), p.PhoneNumber))), N'') p1, NULLIF(LTRIM(RTRIM(p.PhoneNumber2)), N'') p2) x
LEFT JOIN [Identity].[User] au ON au.UserName = p.UserName
LEFT JOIN @role r ON r.UserId = au.Id
WHERE NULLIF(LTRIM(RTRIM(p.fullName)), N'') IS NOT NULL
  AND (au.Id IS NULL OR au.Id NOT IN (SELECT Id FROM @sa));""")
w("DECLARE @cls TABLE (Y int, B int, Name nvarchar(50), PRIMARY KEY (Y, B));")
w("""INSERT @cls SELECT ClassYear, ClassBan, MAX(LEFT(Name, 50)) FROM (
    SELECT ClassYear, ClassBan, COALESCE(NULLIF(LTRIM(RTRIM(ClassName)), N''), CONCAT(ClassYear, N'학년 ', ClassBan, N'반')) Name FROM dbo.ClassMaster WHERE IsActive = 1
    UNION ALL SELECT DISTINCT c.ClassYear, c.ClassBan, CONCAT(c.ClassYear, N'학년 ', c.ClassBan, N'반') FROM dbo.ClassInfo_2026 c
      WHERE c.ClassYear IS NOT NULL AND c.ClassBan IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.ClassMaster m WHERE m.IsActive = 1 AND m.ClassYear = c.ClassYear AND m.ClassBan = c.ClassBan)
) z GROUP BY ClassYear, ClassBan;""")
# 머리 · 표 선언
w("INSERT @L(s) VALUES (N'-- 유년부 이관 넣기 SQL(예전 DB 에서 ' + CONVERT(nvarchar(30), SYSUTCDATETIME(), 126) + N' UTC 에 뽑음). 새 사이트 [SQL 실행]에서 먼저 [시험 실행], 결과를 보고 [저장].');")
w("INSERT @L(s) VALUES (N'DECLARE @now datetime2(3) = ''' + CONVERT(nvarchar(30), SYSUTCDATETIME(), 126) + N''';');")
w("INSERT @L(s) VALUES (" + lit("DECLARE @U TABLE (Id nvarchar(450) PRIMARY KEY, UserName nvarchar(256), Email nvarchar(256), EmailConfirmed bit, PasswordHash nvarchar(max), SecurityStamp nvarchar(max), PhoneNumber nvarchar(max), FirstName nvarchar(max), LastName nvarchar(max), UsernameChangeLimit int, InsertDT datetime, RoleCode nvarchar(50));") + ");")
w("INSERT @L(s) VALUES (" + lit("DECLARE @P TABLE (OldPid int PRIMARY KEY, Name nvarchar(50), UserName nvarchar(256), RoleCode nvarchar(50), BirthMonth tinyint, BirthDay tinyint, Phone1 nvarchar(20), Phone2 nvarchar(20));") + ");")
w("INSERT @L(s) VALUES (" + lit("DECLARE @C TABLE (ClassYear int, ClassBan int, Name nvarchar(50), PRIMARY KEY (ClassYear, ClassBan));") + ");")
w("INSERT @L(s) VALUES (" + lit("DECLARE @M TABLE (OldPid int, ClassYear int, ClassBan int, IsTeacher bit);") + ");")
# 자료
w("INSERT @L(s) SELECT " + row('@U', [q('u.Id'), q('u.UserName'), q('u.Email'), n('u.EmailConfirmed'), q('u.PasswordHash'), q('u.SecurityStamp'), q('u.PhoneNumber'), q('u.FirstName'), q('u.LastName'), n('u.UsernameChangeLimit'), d('u.insertDT'), q('r.RoleCode')])
  + " FROM [Identity].[User] u JOIN @users k ON k.Id = u.Id LEFT JOIN @role r ON r.UserId = u.Id ORDER BY u.UserName;")
w("INSERT @L(s) SELECT " + row('@P', [n('p.Pid'), q('p.Name'), q('p.UserName'), q('p.RoleCode'), n('p.BM'), n('p.BD'), q('p.Ph1'), q('p.Ph2')]) + " FROM @people p ORDER BY p.Pid;")
w("INSERT @L(s) SELECT " + row('@C', [n('c.Y'), n('c.B'), q('c.Name')]) + " FROM @cls c ORDER BY c.Y, c.B;")
w("""INSERT @L(s) SELECT """ + row('@M', [n('m.Pid'), n('m.Y'), n('m.B'), n('m.T')]) + """ FROM (
    SELECT c.PersonID Pid, c.ClassYear Y, c.ClassBan B, 0 T FROM dbo.ClassInfo_2026 c WHERE c.PersonID IS NOT NULL AND c.ClassYear IS NOT NULL AND c.ClassBan IS NOT NULL
    UNION SELECT t.TeacherPersonId, t.ClassYear, t.ClassBan, 1 FROM dbo.ClassTeacherMap_2026 t WHERE t.IsActive = 1
    UNION SELECT t.SubTeacherPersonId, t.ClassYear, t.ClassBan, 1 FROM dbo.ClassTeacherMap_2026 t WHERE t.IsActive = 1 AND t.SubTeacherPersonId IS NOT NULL
) m WHERE m.Pid IN (SELECT Pid FROM @people) ORDER BY m.T DESC, m.Pid;""")
# 넣기 본문
for i in range(0, len(body), 200):
    chunk = body[i:i + 200]
    w("INSERT @L(s) VALUES " + ",\n    ".join("(" + lit(l if l.strip() else ' ') + ")" for l in chunk) + ";")
w("SELECT s FROM @L ORDER BY n;")
(here.parent / 'yuyunbu-export.sql').write_text("\n".join(out) + "\n", encoding='utf-8')
print('ok', len(out), 'lines')
