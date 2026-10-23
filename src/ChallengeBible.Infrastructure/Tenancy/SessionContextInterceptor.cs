using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ChallengeBible.Infrastructure.Tenancy;

/// <summary>
/// 연결을 열 때마다 SESSION_CONTEXT 에 TenantId · AccountId · PlatformScope 를 읽기 전용으로 넣는다(물리 설계 3-1).
/// 값이 없으면 넣지 않는다 — 그러면 RLS 가 영역 데이터를 하나도 보여 주지 않는다.
/// </summary>
public sealed class SessionContextInterceptor(Func<SessionValues> values) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = Build(connection, values());
        cmd?.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var cmd = Build(connection, values());
        if (cmd is not null) await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DbCommand? Build(DbConnection connection, SessionValues v)
    {
        var parts = new List<(string Key, object Value)>();
        if (v.TenantId is int t) parts.Add(("TenantId", t));
        if (v.AccountId is int a) parts.Add(("AccountId", a));
        if (v.PlatformScope) parts.Add(("PlatformScope", 1));
        if (parts.Count == 0) return null;

        var cmd = connection.CreateCommand();
        var sql = new System.Text.StringBuilder();
        for (var i = 0; i < parts.Count; i++)
        {
            sql.Append($"EXEC sp_set_session_context @key = N'{parts[i].Key}', @value = @v{i}, @read_only = 1;");
            var p = cmd.CreateParameter();
            p.ParameterName = $"@v{i}";
            p.Value = parts[i].Value;
            cmd.Parameters.Add(p);
        }
        cmd.CommandText = sql.ToString();
        return cmd;
    }
}

public readonly record struct SessionValues(int? TenantId, int? AccountId, bool PlatformScope);
