using System.Globalization;
using System.Text;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ChallengeBible.Web.Services;

/// <summary>회원 내보내기 목록 한 줄. Fake = 가짜 데이터 계정(@fake-seed.test).</summary>
public sealed record ExportMember(string UserName, string Name, string Depts, string Role, DateTime JoinedUtc, bool Fake);

/// <summary>만든 복원용 SQL: 내용 · 회원 이름들 · INSERT 줄 수.</summary>
public sealed record ExportResult(string Sql, IReadOnlyList<string> Names, int Inserts);

/// <summary>
/// 회원 내보내기(2026-11-21 사용자, 확정 시안 초기화-회원내보내기): 완전 초기화 전에 고른 회원을 복원용 INSERT SQL 로 만든다.
/// 담는 것: 계정(User · UserRoles · UserClaims · UserLogins) · 부서 소속 · 명부 · 반 배정 · 업무 꼬리표 · 설정 · 보호자 연결 · 가입 신청.
/// 미션 · 출석 · 선물 등 기록은 담지 않는다. 관리자(플랫폼 · 부서)는 목록에도 없고 만들지도 않는다(지우지 않는 계정, R-315).
/// SQL 내용은 서버에 저장하지 않고, 누가 · 언제 · 몇 명을 만들었는지만 감사 로그에 남긴다(비밀번호 해시 · 연락처가 들어 있음).
/// 열 이름 · 표 이름은 EF 모델에서 읽어서, 표에 열이 늘어도 그대로 따라간다.
/// </summary>
public sealed class MemberExportService(ISystemDb systemDb, TimeProvider time)
{
    public const string Action = "MemberExport";

    private static IQueryable<string> AdminIds(AppDbContext ctx) =>
        from ur in ctx.UserRoles join r in ctx.IdentityRoleRows on ur.RoleId equals r.Id
        where r.Name == IdentityRoles.Admin || r.Name == IdentityRoles.SuperAdmin select ur.UserId;

    /// <summary>관리자를 뺀 모든 계정(진짜 회원 먼저, 가입 최신 순).</summary>
    public async Task<IReadOnlyList<ExportMember>> ListAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        var admins = AdminIds(ctx);
        var users = await ctx.Users.AsNoTracking().Where(u => !admins.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName, u.LastName, u.FirstName, u.Email, u.InsertDT }).ToListAsync(ct);
        var depts = await (from d in ctx.DeptRoles.AsNoTracking() join t in ctx.Tenants.AsNoTracking() on d.TenantId equals t.Id
                           where d.UserId != null select new { d.UserId, d.TenantId, t.Name, d.RoleCode }).ToListAsync(ct);
        var codes = (await ctx.CodeData.AsNoTracking().Where(c => c.MajorCD == CodeData.RoleMajor && c.MinorCD != null)
            .Select(c => new { c.TenantId, c.MinorCD, c.MinorNM }).ToListAsync(ct))
            .GroupBy(c => (c.TenantId, c.MinorCD!.Trim())).ToDictionary(g => g.Key, g => g.First().MinorNM ?? "");
        string RoleOf(int tenant, string? code) => string.IsNullOrWhiteSpace(code) ? UserRole.LabelOf(UserRoleKind.Parent)
            : codes.GetValueOrDefault((tenant, code.Trim())) is { Length: > 0 } n ? n : UserRole.LabelOf(UserRole.KindOf(code));
        return users.Select(u =>
        {
            var mine = depts.Where(d => d.UserId == u.Id).ToList();
            var fake = u.Email != null && u.Email.EndsWith(TestToolsService.EmailDomain, StringComparison.OrdinalIgnoreCase);
            return new ExportMember(u.UserName ?? "", AppUser.FullName(u.LastName, u.FirstName), string.Join(", ", mine.Select(d => d.Name).Distinct()),
                string.Join(", ", mine.Select(d => RoleOf(d.TenantId, d.RoleCode)).Distinct()), u.InsertDT, fake);
        }).OrderBy(m => m.Fake).ThenByDescending(m => m.JoinedUtc).ThenBy(m => m.UserName).ToList();
    }

    /// <summary>고른 아이디들의 복원용 SQL. 관리자 · 없는 아이디는 조용히 뺀다. 아무도 없으면 null.</summary>
    public async Task<ExportResult?> BuildAsync(string actor, IEnumerable<string> userNames, CancellationToken ct = default)
    {
        var wanted = userNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct().ToList();
        if (wanted.Count == 0) return null;
        await using var ctx = systemDb.Create(actor);
        var admins = AdminIds(ctx);
        var users = await ctx.Users.Where(u => u.UserName != null && wanted.Contains(u.UserName) && !admins.Contains(u.Id)).OrderBy(u => u.UserName).ToListAsync(ct);
        if (users.Count == 0) return null;
        var ids = users.Select(u => u.Id).ToList();
        var names = users.Select(u => u.UserName!).ToList();
        var people = await ctx.People.Where(p => p.UserId != null && ids.Contains(p.UserId)).ToListAsync(ct);
        var personKeys = people.Select(p => (p.TenantId, p.Id)).ToHashSet();
        var personIds = people.Select(p => p.Id).ToList();
        var classMembers = (await ctx.ClassMembers.Where(m => personIds.Contains(m.PersonId)).ToListAsync(ct)).Where(m => personKeys.Contains((m.TenantId, m.PersonId))).ToList();

        var sb = new StringBuilder();
        var inserts = 0;
        sb.AppendLine($"-- 복원용 SQL · {time.GetUtcNow().UtcDateTime:yyyy-MM-dd HH:mm} UTC · {users.Count}명: {string.Join(", ", names)}");
        sb.AppendLine("-- 이 파일에는 비밀번호 해시 · 연락처가 들어 있어요. Git · 메신저에 올리지 마세요.");
        sb.AppendLine("-- 복원: 이 파일 전체를 실행(QUOTED_IDENTIFIER ON). 기록(미션 · 출석 · 선물 등)은 들어 있지 않아요.");
        sb.AppendLine("SET NOCOUNT ON;");
        sb.AppendLine("SET QUOTED_IDENTIFIER ON;");
        sb.AppendLine("EXEC sp_set_session_context N'PlatformScope', 1;");
        void Add<T>(IEnumerable<T> rows) where T : class => inserts += Write(ctx, sb, rows.ToList());
        Add(users);
        Add(await ctx.UserRoles.Where(r => ids.Contains(r.UserId)).ToListAsync(ct));
        Add(await ctx.Set<AppUserClaim>().Where(r => ids.Contains(r.UserId)).ToListAsync(ct));
        Add(await ctx.Set<AppUserLogin>().Where(r => ids.Contains(r.UserId)).ToListAsync(ct));
        Add(await ctx.DeptRoles.Where(d => d.UserId != null && ids.Contains(d.UserId)).ToListAsync(ct));
        Add(people);
        Add(classMembers);
        Add(await ctx.MemberTags.Where(t => ids.Contains(t.UserId)).ToListAsync(ct));
        Add(await ctx.UserSettings.Where(s => names.Contains(s.UserName)).ToListAsync(ct));
        Add(await ctx.GuardianLinks.Where(l => names.Contains(l.GuardianUserName) || names.Contains(l.StudentUserName)).ToListAsync(ct));
        Add(await ctx.SignupApplications.Where(s => ids.Contains(s.UserId)).ToListAsync(ct));
        sb.AppendLine("EXEC sp_set_session_context N'PlatformScope', NULL;");

        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = Action, Detail = $"{users.Count}명 · {inserts}줄" });   // 내용은 남기지 않는다
        await ctx.SaveChangesAsync(ct);
        return new ExportResult(sb.ToString(), users.Select(u => u.DisplayName is { Length: > 0 } d ? d : u.UserName!).ToList(), inserts);
    }

    /// <summary>한 표의 줄들을 INSERT 로. 자동 번호 열이 있으면 IDENTITY_INSERT 로 감싼다.</summary>
    private static int Write<T>(AppDbContext ctx, StringBuilder sb, List<T> rows) where T : class
    {
        if (rows.Count == 0) return 0;
        var et = ctx.Model.FindEntityType(typeof(T)) ?? throw new InvalidOperationException(typeof(T).Name);
        var table = et.GetTableName()!;
        var schema = et.GetSchema();
        var store = StoreObjectIdentifier.Table(table, schema);
        var full = schema is null ? $"[{table}]" : $"[{schema}].[{table}]";
        var props = et.GetProperties().Where(p => p.GetComputedColumnSql() is null && !(p.IsConcurrencyToken && p.ValueGenerated == ValueGenerated.OnAddOrUpdate)).ToList();
        var pk = et.FindPrimaryKey();
        var identity = props.Any(p => p.ValueGenerated == ValueGenerated.OnAdd && pk != null && pk.Properties.Contains(p)
                                      && (p.ClrType == typeof(int) || p.ClrType == typeof(long)) && p.GetDefaultValueSql() is null);
        var cols = string.Join(", ", props.Select(p => $"[{p.GetColumnName(store) ?? p.GetColumnName()}]"));
        if (identity) sb.AppendLine($"SET IDENTITY_INSERT {full} ON;");
        foreach (var row in rows)
        {
            var entry = ctx.Entry(row);
            var vals = props.Select(p =>
            {
                var v = entry.Property(p.Name).CurrentValue;
                if (p.GetValueConverter() is { } conv && v is not null) v = conv.ConvertToProvider(v);
                return Literal(v);
            });
            sb.AppendLine($"INSERT INTO {full} ({cols}) VALUES ({string.Join(", ", vals)});");
        }
        if (identity) sb.AppendLine($"SET IDENTITY_INSERT {full} OFF;");
        return rows.Count;
    }

    /// <summary>값 하나를 SQL 글자로(한글은 N'…', 날짜는 ISO).</summary>
    public static string Literal(object? v) => v switch
    {
        null => "NULL",
        string s => "N'" + s.Replace("'", "''") + "'",
        char c => "N'" + (c == '\'' ? "''" : c.ToString()) + "'",
        Guid g => "'" + g + "'",
        bool b => b ? "1" : "0",
        // datetime 열에 7자리 소수 글자를 바로 넣으면 변환 오류 — datetime2 로 바꿔 넣으면 열 형식에 맞춰 반올림된다
        DateTime d => "CAST('" + d.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture) + "' AS datetime2)",
        DateTimeOffset o => "CAST('" + o.ToString("yyyy-MM-dd HH:mm:ss.fffffff zzz", CultureInfo.InvariantCulture) + "' AS datetimeoffset)",
        DateOnly d => "'" + d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "'",
        TimeOnly t => "'" + t.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture) + "'",
        TimeSpan t => "'" + t.ToString("c", CultureInfo.InvariantCulture) + "'",
        byte[] bytes => "0x" + Convert.ToHexString(bytes),
        Enum e => Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => "N'" + v.ToString()!.Replace("'", "''") + "'",
    };
}
