using System.Data;
using System.Data.Common;
using ChallengeBible.Domain.Time;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>SQL 결과 표 하나(머리 + 줄).</summary>
public sealed record ResultTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>실행 결과: 성공이면 표들, 실패면 오류 문구.</summary>
public sealed record FakeRunResult(bool Ok, bool Committed, string? Error, IReadOnlyList<ResultTable> Tables, long Ms);

/// <summary>상황별 로그인 버튼 하나. Code = 아이디 끝(admin · t01 · s01 · p01).</summary>
public sealed record FakeScenario(string Code, string Group, string Label, string Detail);

/// <summary>부서 하나의 가짜 계정(로그인 버튼). UserName = 부서주소-코드, 계정이 실제로 있을 때만.</summary>
public sealed record FakeLogin(FakeScenario Scenario, string UserName, string Name);
public sealed record FakeDept(int TenantId, string Name, string Slug, IReadOnlyList<FakeLogin> Logins);

/// <summary>
/// 임시 시험 도구(ADR 0095, 2026-11-14 사용자 요청 — 선물 시스템 시험용). 설정 `TestTools:Enabled` 가 true 일 때만 열린다(끄면 화면이 없는 것처럼 404).
/// 가짜 데이터 넣기 · 지우기는 플랫폼 관리자만(시스템 연결, 시작마다 DB 로 다시 확인) — SQL 은 tools/sql/gen-fake-seed.py 가 만든 TestTools/*.sql.
/// 상황별 로그인은 가짜 계정(이메일 @fake-seed.test)에만, 비밀번호 1234 로 일반 로그인과 같은 길(AccountService.LoginAsync)을 탄다.
/// </summary>
public sealed class TestToolsService(ISystemDb systemDb, IConfiguration config, TimeProvider time)
{
    public const string EmailDomain = "@fake-seed.test";
    public const string Password = "1234";
    public const string ConfirmWord = "FAKE";
    public const string CreateAction = "FakeDataCreate";
    public const string DeleteAction = "FakeDataDelete";

    public bool Enabled => config.GetValue<bool>("TestTools:Enabled");

    public static bool IsFakeEmail(string? email) => email is not null && email.EndsWith(EmailDomain, StringComparison.OrdinalIgnoreCase);

    /// <summary>상황 목록(gen-fake-seed.py 의 틀과 같아야 한다). 생일은 월/일, 점수는 올해 미션 점수.</summary>
    public static readonly IReadOnlyList<FakeScenario> Scenarios =
    [
        new("admin", "관리 · 선생님", "부서 관리자", "새친구반 담당 · 400점 · 구매 · 전달 처리"),
        new("t01", "관리 · 선생님", "선생님 1반", "통독 1독(12일 전) · 생일 11/18 · 점수 0"),
        new("t02", "관리 · 선생님", "선생님 2반", "50점 · 생일 12/2(어른이라 생일 선물 없음)"),
        new("t07", "관리 · 선생님", "선생님 7반", "1,200점 · 100점마다 박수(선물 없음)"),
        new("t10", "관리 · 선생님", "선생님 10반", "1,500점"),

        new("s01", "개인 점수 · 크레딧", "3,100점", "상한 30,000 크레딧 넘음 · 통독 3독(3일 전) · 생일 11/1"),
        new("s03", "개인 점수 · 크레딧", "2,600점", "크레딧 딱 30,000 · 친구팀 5,100 · 생일 11/10"),
        new("s04", "개인 점수 · 크레딧", "2,590점", "29,000 크레딧(상한 바로 아래) · 통독 99% · 생일 11/13"),
        new("s14", "개인 점수 · 크레딧", "1,000점", "14,000 크레딧 · 새친구 데려옴 · 생일 12/14"),
        new("s15", "개인 점수 · 크레딧", "990점", "13,000 크레딧 · 통독 2독(20일 전) · 생일 11/21"),
        new("s21", "개인 점수 · 크레딧", "500점", "3주 결석 · 새친구 데려옴"),
        new("s27", "개인 점수 · 크레딧", "199점", "5,000 크레딧 + 자투리 99점"),
        new("s31", "개인 점수 · 크레딧", "100점 딱", "처음 5,000 크레딧"),
        new("s32", "개인 점수 · 크레딧", "99점", "크레딧 없음(1점 모자람)"),
        new("s37", "개인 점수 · 크레딧", "10점", "크레딧 없음 · 가족팀 600"),
        new("s38", "개인 점수 · 크레딧", "0점", "미션 안 함 · 미배정반"),

        new("s05", "생일", "생일 11/14", "2,500점 · 친구팀 5,100(1,000 선물 받음)"),
        new("s02", "생일", "생일 11/5(지남)", "2,750점 · 가족팀 10,050"),
        new("s06", "생일", "생일 11/15", "2,300점 · 가족팀 10,050"),
        new("s16", "생일", "생일 11/19", "생일 선물 기간 밖(7일 전 넘음) · 890점"),
        new("s08", "생일", "생일 11/28", "생일 선물 기간 안 · 가족팀 7,100"),
        new("s09", "생일", "생일 12/1", "생일 선물 기간 안 · 친구팀 2,950"),
        new("s13", "생일", "생일 12/10", "기간 안 · 통독 1독(10일 전)"),
        new("s17", "생일", "생일 12/15", "기간 밖(30일 전 넘음) · 가족팀 2,050"),
        new("s19", "생일", "생일 없음", "명부에 생일 없음 · 친구팀 1,000 · 통독 10%"),

        new("s07", "성경통독", "2독 완료(5일 전)", "완독 선물 2독 · 생일 11/20 · 새친구 데려옴"),
        new("s22", "성경통독", "1독 완료(40일 전)", "완독 선물 기한(30일) 지남 → 다시 열기"),
        new("s12", "성경통독", "읽는 중 50%", "친구팀 2,950 · 1,250점"),
        new("s29", "성경통독", "읽는 중 75%", "150점"),
        new("s33", "성경통독", "읽는 중 1장", "95점"),
        new("p02", "성경통독", "학부모 1독 완료(15일 전)", "어른 완독은 박수만 · 가족팀 1,050"),

        new("p01", "가족팀 · 친구팀", "가족팀 600점(엄마)", "1,000 전 · 학생 s37"),
        new("s30", "가족팀 · 친구팀", "가족팀 1,050점(학생)", "1,000 단계 막 넘음"),
        new("p03", "가족팀 · 친구팀", "가족팀 1,990점(아빠)", "2,000 바로 아래 · 학생 둘"),
        new("p10", "가족팀 · 친구팀", "가족팀 2,050점(엄마)", "2,000 막 넘음"),
        new("p04", "가족팀 · 친구팀", "가족팀 3,020점(엄마)", "1,000 선물 받음(팀 잠금) · 엄마 · 아빠"),
        new("p06", "가족팀 · 친구팀", "가족팀 4,980점(엄마)", "5,000 바로 아래"),
        new("p07", "가족팀 · 친구팀", "가족팀 7,100점(아빠)", "1,000 · 2,000 선물 받음"),
        new("p08", "가족팀 · 친구팀", "가족팀 10,050점(엄마)", "최고 단계 넘음 · 1,000 선물 받음"),
        new("s24", "가족팀 · 친구팀", "친구팀 880점", "1,000 전"),
        new("s23", "가족팀 · 친구팀", "친구팀 1,000점 딱", "단계 경계"),
        new("s09", "가족팀 · 친구팀", "친구팀 2,950점", "3,000 바로 아래"),
        new("s03", "가족팀 · 친구팀", "친구팀 5,100점", "1,000 선물 받음(팀 잠금)"),
    ];

    // ── 가짜 데이터 넣기 · 지우기 ──

    /// <summary>
    /// 가짜 데이터 넣기(create) · 지우기(delete). commit = false 면 시험 실행(끝에 되돌림). 플랫폼 관리자만.
    /// 넣기는 플랫폼 관리자(SuperAdmin) 계정만 남기고 모든 계정 · 기록을 지운 뒤 넣는다.
    /// </summary>
    public async Task<FakeRunResult> RunAsync(string actor, bool create, bool commit, CancellationToken ct = default)
    {
        if (!Enabled) throw new InvalidOperationException("시험 도구가 꺼져 있어요.");
        await using var ctx = systemDb.Create(actor);
        if (!await AccountService.IsActivePlatformAdminAsync(ctx, actor, ct)) throw new UnauthorizedAccessException("플랫폼 관리자만 할 수 있어요.");
        if (!ctx.Database.IsRelational()) return new(false, false, "시험용 메모리 DB 에서는 SQL 을 실행하지 않아요.", [], 0);

        var sql = Script(create ? "fake-create.sql" : "fake-delete.sql");
        var today = TenantClock.ToTenantDate(time.GetUtcNow(), PlatformHomeService.PlatformTimeZone);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var tables = new List<ResultTable>();
        try
        {
            await ctx.Database.OpenConnectionAsync(ct);   // 연결을 열 때 PlatformScope 가 들어간다
            await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 600;
            Add(cmd, "@pCommit", DbType.Boolean, commit);
            Add(cmd, "@pToday", DbType.Date, today.ToDateTime(TimeOnly.MinValue));
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            do
            {
                var cols = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
                var rows = new List<IReadOnlyList<string>>();
                while (await reader.ReadAsync(ct))
                    rows.Add(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? "").ToList());
                if (cols.Count > 0) tables.Add(new(cols, rows));
            } while (await reader.NextResultAsync(ct));
        }
        catch (DbException e)
        {
            return new(false, false, e.Message, tables, sw.ElapsedMilliseconds);
        }
        finally
        {
            await ctx.Database.CloseConnectionAsync();
        }

        if (commit)
        {
            ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = create ? CreateAction : DeleteAction, Detail = "시험 도구(/test-tools)" });
            await ctx.SaveChangesAsync(ct);
        }
        return new(true, commit, null, tables, sw.ElapsedMilliseconds);
    }

    private static void Add(DbCommand cmd, string name, DbType type, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.DbType = type;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    /// <summary>앱에 넣은 SQL(EmbeddedResource TestTools/*.sql).</summary>
    public static string Script(string file)
    {
        var asm = typeof(TestToolsService).Assembly;
        var name = asm.GetManifestResourceNames().Single(n => n.EndsWith("TestTools." + file, StringComparison.Ordinal));
        using var s = asm.GetManifestResourceStream(name)!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    // ── 상황별 로그인 ──

    /// <summary>이 사람이 시험 도구 화면을 볼 수 있는가: 플랫폼 관리자이거나 가짜 계정.</summary>
    public async Task<bool> CanUseAsync(string? userName, CancellationToken ct = default)
    {
        if (!Enabled || string.IsNullOrEmpty(userName)) return false;
        await using var ctx = systemDb.Create(userName);
        if (await AccountService.IsActivePlatformAdminAsync(ctx, userName, ct)) return true;
        var email = await ctx.Users.AsNoTracking().Where(u => u.UserName == userName).Select(u => u.Email).FirstOrDefaultAsync(ct);
        return IsFakeEmail(email);
    }

    public async Task<bool> IsPlatformAdminAsync(string userName, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(userName);
        return await AccountService.IsActivePlatformAdminAsync(ctx, userName, ct);
    }

    /// <summary>부서마다 상황별 로그인 버튼(계정이 있는 것만). 가짜 계정이 하나도 없는 부서는 뺀다.</summary>
    public async Task<(IReadOnlyList<FakeDept> Depts, int FakeAccounts)> LoginsAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        var fakes = await ctx.Users.AsNoTracking().Where(u => u.Email != null && u.Email.EndsWith(EmailDomain))
            .Select(u => new { u.UserName, u.FirstName, u.LastName }).ToListAsync(ct);
        var byName = fakes.Where(f => f.UserName != null).ToDictionary(f => f.UserName!, f => AppUser.FullName(f.LastName, f.FirstName));
        var tenants = await ctx.Tenants.AsNoTracking().Where(t => !t.IsLobby).OrderBy(t => t.Id).Select(t => new { t.Id, t.Name, t.Slug }).ToListAsync(ct);
        var depts = new List<FakeDept>();
        foreach (var t in tenants)
        {
            var logins = Scenarios.Select(s => (s, user: $"{t.Slug.ToLowerInvariant()}-{s.Code}"))
                .Where(x => byName.ContainsKey(x.user)).Select(x => new FakeLogin(x.s, x.user, byName[x.user])).ToList();
            if (logins.Count > 0) depts.Add(new(t.Id, t.Name, t.Slug, logins));
        }
        return (depts, fakes.Count);
    }

    /// <summary>가짜 계정이면 그 계정을 돌려준다(비밀번호 1234 로 일반 로그인과 같은 확인). 아니면 null.</summary>
    public async Task<AppUser?> FakeLoginAsync(AccountService accounts, string userName, CancellationToken ct = default)
    {
        if (!Enabled) return null;
        await using (var ctx = systemDb.Create(userName))
        {
            var email = await ctx.Users.AsNoTracking().Where(u => u.UserName == userName).Select(u => u.Email).FirstOrDefaultAsync(ct);
            if (!IsFakeEmail(email)) return null;
        }
        var r = await accounts.LoginAsync(userName, Password, ct);
        return r.Account;
    }
}
