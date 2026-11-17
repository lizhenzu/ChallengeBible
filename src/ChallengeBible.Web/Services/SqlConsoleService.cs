using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

/// <summary>SQL 실행 결과: 표들 · 영향 줄 수 · 메시지 · 저장 여부.</summary>
public sealed record SqlRunResult(bool Ok, bool Saved, string Message, IReadOnlyList<ResultTable> Tables, IReadOnlyList<string> Notes, long Ms, bool Truncated = false);

/// <summary>
/// 플랫폼 SQL 실행(ADR 0107, R-320, 2026-11-17 사용자 "모바일에서 SQL 실행"): 플랫폼 관리자만, 시스템 연결(모든 부서).
/// 늘 트랜잭션 안에서 실행하고 기본은 되돌린다(시험 실행). 저장은 확인 글자를 쓴 때만.
/// 관리자 보호(R-315): 실행 전 · 후 관리자 계정 · 권한 · 소속 · 명부 줄의 지문을 비교해 하나라도 바뀌면 저장하지 않고 되돌린다.
/// 쿼리 안의 COMMIT · ROLLBACK · BEGIN TRAN 은 이 보호를 넘을 수 있어 받지 않는다.
/// </summary>
public sealed partial class SqlConsoleService(ISystemDb systemDb, TimeProvider time)
{
    public const string ConfirmWord = "SAVE";
    public const string Action = "SqlConsole";
    public const int MaxRows = 5000;
    public const int TimeoutSeconds = 30;

    /// <summary>자주 쓰는 쿼리(확정 시안 SQL실행).</summary>
    public static readonly IReadOnlyList<(string Label, string Sql)> Presets =
    [
        ("부서별 소속", "SELECT t.Slug, t.Name, COUNT(d.Id) AS 소속\nFROM app.Tenant t\nLEFT JOIN dbo.DeptRoleUserAssoc d ON d.TenantId = t.Id\nGROUP BY t.Slug, t.Name\nORDER BY t.Name;"),
        ("할 일이 뜨는 이유", "-- 이름을 바꿔서 실행: 역할 코드 · 권한 · 업무 칩 · 담임 반 수\nSELECT t.Slug, u.UserName, u.LastName + u.FirstName AS 이름, d.RoleCode,\n  (SELECT STRING_AGG(r.Name, ', ') FROM [Identity].[UserRoles] ur JOIN [Identity].[Role] r ON r.Id = ur.RoleId WHERE ur.UserId = u.Id) AS 권한,\n  (SELECT COUNT(*) FROM app.MemberTag m WHERE m.UserId = u.Id AND m.TenantId = d.TenantId) AS 업무칩,\n  (SELECT COUNT(*) FROM app.Person p JOIN app.ClassMember cm ON cm.TenantId = p.TenantId AND cm.PersonId = p.Id\n   WHERE p.UserId = u.Id AND p.TenantId = d.TenantId AND cm.IsTeacher = 1) AS 담임반수\nFROM [Identity].[User] u\nJOIN dbo.DeptRoleUserAssoc d ON d.UserId = u.Id\nJOIN app.Tenant t ON t.Id = d.TenantId\nWHERE u.LastName + u.FirstName LIKE N'%조진찬%';"),
        ("가짜 계정 수", "SELECT COUNT(*) AS 가짜계정 FROM [Identity].[User] WHERE Email LIKE N'%@fake-seed.test';"),
        ("오늘 365 기도", "SELECT t.Name AS 부서, COUNT(*) AS 기도한사람\nFROM app.Prayer365Log l JOIN app.Tenant t ON t.Id = l.TenantId\nWHERE l.PrayDate = CAST(SWITCHOFFSET(SYSUTCDATETIME(), '+09:00') AS date)\nGROUP BY t.Name;"),
    ];

    [GeneratedRegex(@"\b(COMMIT|ROLLBACK)\b|\bBEGIN\s+(DISTRIBUTED\s+)?TRAN|\bSAVE\s+TRAN|IMPLICIT_TRANSACTIONS", RegexOptions.IgnoreCase)]
    private static partial Regex TxWords();

    /// <summary>트랜잭션을 직접 다루는 문장이 있으면 그 이유(받지 않는다).</summary>
    public static string? Reject(string sql) => TxWords().IsMatch(sql)
        ? "COMMIT · ROLLBACK · BEGIN TRAN 은 쓸 수 없어요. 저장 여부는 아래에서 골라 주세요." : null;

    // 관리자(Admin · SuperAdmin) 계정과 그 권한 · 소속 · 명부 줄 — 실행 전에 정한 아이디로 전 · 후를 비교한다(권한을 빼는 것도 잡는다)
    private const string AdminIdsSql = "SELECT ur.UserId FROM [Identity].[UserRoles] ur JOIN [Identity].[Role] r ON r.Id = ur.RoleId WHERE r.NormalizedName IN (N'ADMIN', N'SUPERADMIN')";
    private static string FingerprintSql(string ids) => $"""
        SELECT u.Id, u.UserName, u.NormalizedUserName, u.Email, u.PasswordHash, u.SecurityStamp, u.LockoutEnd, u.FirstName, u.LastName FROM [Identity].[User] u WHERE u.Id IN ({ids}) ORDER BY u.Id;
        SELECT ur.UserId, ur.RoleId FROM [Identity].[UserRoles] ur WHERE ur.UserId IN ({ids}) ORDER BY ur.UserId, ur.RoleId;
        SELECT d.Id, d.TenantId, d.UserId, d.RoleCode FROM dbo.DeptRoleUserAssoc d WHERE d.UserId IN ({ids}) ORDER BY d.Id;
        SELECT p.TenantId, p.Id, p.UserId, p.Name FROM app.Person p WHERE p.UserId IN ({ids}) ORDER BY p.TenantId, p.Id;
        """;

    public async Task<SqlRunResult> RunAsync(string actor, string sql, bool save, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        if (!await AccountService.IsActivePlatformAdminAsync(ctx, actor, ct)) throw new UnauthorizedAccessException("플랫폼 관리자만 할 수 있어요.");
        if (string.IsNullOrWhiteSpace(sql)) return new(false, false, "SQL 을 써 주세요.", [], [], 0);
        if (Reject(sql) is { } why) return new(false, false, why, [], [], 0);
        if (!ctx.Database.IsRelational()) return new(false, false, "시험용 메모리 DB 에서는 SQL 을 실행하지 않아요.", [], [], 0);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var tables = new List<ResultTable>();
        var notes = new List<string>();
        var truncated = false;
        await ctx.Database.OpenConnectionAsync(ct);   // 연결을 열 때 PlatformScope 가 들어간다(시스템 연결)
        var conn = ctx.Database.GetDbConnection();
        if (conn is Microsoft.Data.SqlClient.SqlConnection sc) sc.InfoMessage += (_, e) => notes.Add(e.Message);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var ids = await AdminIdListAsync(conn, tx, ct);
            var before = await FingerprintAsync(conn, tx, ids, ct);
            await using (var cmd = conn.CreateCommand())
            {
                (cmd.Transaction, cmd.CommandText, cmd.CommandTimeout) = (tx, sql, TimeoutSeconds);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                do
                {
                    var cols = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
                    var rows = new List<IReadOnlyList<string>>();
                    while (await reader.ReadAsync(ct))
                    {
                        if (rows.Count >= MaxRows) { truncated = true; continue; }
                        rows.Add(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "NULL" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? "").ToList());
                    }
                    if (cols.Count > 0) tables.Add(new(cols, rows));
                } while (await reader.NextResultAsync(ct));
                if (reader.RecordsAffected >= 0) notes.Add($"영향받은 줄 {reader.RecordsAffected:N0}");
            }
            var after = await FingerprintAsync(conn, tx, ids, ct);
            if (before != after)
            {
                await tx.RollbackAsync(ct);
                return new(false, false, "관리자 계정 · 권한 · 소속 · 명부 줄이 바뀌어서 되돌렸어요(관리자는 지우거나 바꾸지 않아요, R-315).", tables, notes, sw.ElapsedMilliseconds, truncated);
            }
            if (save) await tx.CommitAsync(ct); else await tx.RollbackAsync(ct);
        }
        catch (DbException e)
        {
            try { await tx.RollbackAsync(CancellationToken.None); } catch { }
            await AuditAsync(actor, "error", sql);
            return new(false, false, e.Message, tables, notes, sw.ElapsedMilliseconds, truncated);
        }
        finally { await ctx.Database.CloseConnectionAsync(); }
        await AuditAsync(actor, save ? "save" : "test", sql);
        return new(true, save, save ? "저장했어요." : "시험 실행 · 되돌렸어요(바뀐 것 없음).", tables, notes, sw.ElapsedMilliseconds, truncated);
    }

    private static async Task<string> AdminIdListAsync(DbConnection conn, DbTransaction tx, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        (cmd.Transaction, cmd.CommandText) = (tx, AdminIdsSql);
        var ids = new List<string>();
        await using (var r = await cmd.ExecuteReaderAsync(ct)) while (await r.ReadAsync(ct)) ids.Add(r.GetString(0));
        return ids.Count == 0 ? "N''" : string.Join(",", ids.Distinct().Select(i => "N'" + i.Replace("'", "''") + "'"));
    }

    private static async Task<string> FingerprintAsync(DbConnection conn, DbTransaction tx, string ids, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        (cmd.Transaction, cmd.CommandText) = (tx, FingerprintSql(ids));
        var sb = new StringBuilder();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        do { while (await r.ReadAsync(ct)) { for (var i = 0; i < r.FieldCount; i++) sb.Append(r.IsDBNull(i) ? "\u0000" : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)).Append('\u001f'); sb.Append('\n'); } sb.Append('\u001e'); }
        while (await r.NextResultAsync(ct));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>실행한 SQL 은 감사 로그에 남긴다(따로 저장 — 되돌린 실행도 남긴다).</summary>
    private async Task AuditAsync(string actor, string mode, string sql)
    {
        await using var ctx = systemDb.Create(actor);
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = Action, Detail = $"{mode}: " + (sql.Length > 900 ? sql[..900] + "…" : sql) });
        await ctx.SaveChangesAsync();
    }

    /// <summary>최근 실행 기록(감사 로그).</summary>
    public async Task<IReadOnlyList<(DateTime AtUtc, string Actor, string Detail)>> HistoryAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = systemDb.Create(actor);
        return (await ctx.AuditLogs.AsNoTracking().Where(a => a.Action == Action).OrderByDescending(a => a.Id).Take(10)
            .Select(a => new { a.OccurredUtc, a.ActorUserName, a.Detail }).ToListAsync(ct)).Select(a => (a.OccurredUtc, a.ActorUserName, a.Detail ?? "")).ToList();
    }
}
