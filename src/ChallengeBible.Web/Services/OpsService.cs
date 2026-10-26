using System.Diagnostics;
using ChallengeBible.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChallengeBible.Web.Services;

public enum CheckState { Ok, Warn, Bad }
public sealed record OpsCheck(string Title, CheckState State, string Value);
public sealed record OpsAuditRow(DateTime OccurredUtc, string Action, string Label, string Actor, string? Tenant, string Text);

/// <summary>
/// 플랫폼 운영 점검(확정 시안: 플랫폼-운영점검): 서버 상태 6칸 · 감사 로그. 시스템 연결로 읽기만 하고, 시작마다 플랫폼 관리자인지 DB 로 다시 확인한다.
/// 시험용 메모리 DB 에서는 DB 버전 · 마이그레이션 · RLS 를 "검사 안 함"으로 둔다.
/// </summary>
public sealed class OpsService(ISystemDb systemDb, AppInfo app, TimeProvider time)
{
    public const string PlatformTimeZone = PlatformHomeService.PlatformTimeZone;

    private async Task<AppDbContext> OpenAsync(int actor, CancellationToken ct)
    {
        var ctx = systemDb.Create(actor);
        if (!await ctx.Accounts.AnyAsync(a => a.Id == actor && a.IsPlatformAdmin && a.Status == "Active", ct))
        {
            await ctx.DisposeAsync();
            throw new UnauthorizedAccessException("플랫폼 관리자만 볼 수 있어요.");
        }
        return ctx;
    }

    public async Task<IReadOnlyList<OpsCheck>> ChecksAsync(int actor, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var relational = ctx.Database.IsRelational();
        var list = new List<OpsCheck>();

        // DB 연결 · 응답 시간
        var sw = Stopwatch.StartNew();
        string? version = null;
        bool ok;
        try
        {
            ok = await ctx.Database.CanConnectAsync(ct);
            if (ok && relational) version = (await ctx.Database.SqlQueryRaw<string>("SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS Value").ToListAsync(ct)).FirstOrDefault();
        }
        catch (Exception) { ok = false; }
        sw.Stop();
        list.Add(new("DB 연결", ok ? CheckState.Ok : CheckState.Bad,
            ok ? $"응답 {sw.ElapsedMilliseconds}ms" + (version is null ? relational ? "" : " · 시험용 메모리 DB" : $" · SQL Server {version}") : "연결 실패"));

        // 마이그레이션(게시만 하고 SQL 을 안 돌린 상태를 잡는다)
        if (!relational) list.Add(new("마이그레이션", CheckState.Ok, "검사 안 함 · 시험용 메모리 DB"));
        else if (!ok) list.Add(new("마이그레이션", CheckState.Bad, "DB 에 연결하지 못해 확인할 수 없어요"));
        else list.Add(await Safe("마이그레이션", async () =>
        {
            var pending = (await ctx.Database.GetPendingMigrationsAsync(ct)).ToList();
            var last = (await ctx.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault();
            return pending.Count == 0
                ? new("마이그레이션", CheckState.Ok, $"모두 적용 · 마지막 {Short(last)}")
                : new("마이그레이션", CheckState.Warn, $"적용 안 된 {pending.Count}개 · {string.Join(", ", pending.Select(Short))}");
        }));

        // RLS 정책
        if (!relational) list.Add(new("RLS 정책", CheckState.Ok, "검사 안 함 · 시험용 메모리 DB"));
        else if (!ok) list.Add(new("RLS 정책", CheckState.Bad, "DB 에 연결하지 못해 확인할 수 없어요"));
        else list.Add(await Safe("RLS 정책", async () =>
        {
            var on = (await ctx.Database.SqlQueryRaw<int>("SELECT CAST(is_enabled AS int) AS Value FROM sys.security_policies WHERE name = 'TenantIsolation'").ToListAsync(ct)).FirstOrDefault();
            var preds = (await ctx.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.security_predicates p JOIN sys.security_policies s ON s.object_id = p.object_id WHERE s.name = 'TenantIsolation'").ToListAsync(ct)).FirstOrDefault();
            return on == 1 ? new("RLS 정책", CheckState.Ok, $"켜짐 · sec.TenantIsolation 조건 {preds}개") : new("RLS 정책", CheckState.Bad, "꺼짐 또는 없음 · sec.TenantIsolation");
        }));

        // 서버 시각 · 가동 · 버전
        var now = time.GetUtcNow().UtcDateTime;
        var seoul = TimeZoneInfo.ConvertTimeFromUtc(now, TimeZoneInfo.FindSystemTimeZoneById(PlatformTimeZone));
        list.Add(new("서버 시각", CheckState.Ok, $"한국 {seoul:yyyy-MM-dd HH:mm} · UTC {now:HH:mm}"));
        var up = now - app.StartedUtc;
        var started = TimeZoneInfo.ConvertTimeFromUtc(app.StartedUtc, TimeZoneInfo.FindSystemTimeZoneById(PlatformTimeZone));
        list.Add(new("가동", CheckState.Ok, $"{(up.TotalDays >= 1 ? $"{(int)up.TotalDays}일 " : "")}{up.Hours}시간 {up.Minutes}분 · {started:M월 d일 HH:mm} 시작"));
        var built = TimeZoneInfo.ConvertTimeFromUtc(app.BuiltUtc, TimeZoneInfo.FindSystemTimeZoneById(PlatformTimeZone));
        list.Add(new("버전", CheckState.Ok, $"{(app.Commit.Length == 0 ? "커밋 정보 없음" : app.Commit[..Math.Min(7, app.Commit.Length)])} · 빌드 {built:M월 d일 HH:mm}"));
        return list;
    }

    /// <summary>점검 하나가 실패해도(권한 · 시간 초과) 화면은 열리게 "확인할 수 없어요"로 바꾼다. 오류 내용은 보여 주지 않는다.</summary>
    private static async Task<OpsCheck> Safe(string title, Func<Task<OpsCheck>> check)
    {
        try { return await check(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return new(title, CheckState.Warn, $"확인할 수 없어요 · {ex.GetType().Name}"); }
    }

    /// <summary>"20261024081107_FamilyTeams" → "FamilyTeams".</summary>
    private static string Short(string? migration) => migration is null ? "없음" : migration.IndexOf('_') is var i and > 0 ? migration[(i + 1)..] : migration;

    public static string Label(string action) => action switch
    {
        "PromotionApproved" => "진급 승인",
        "FamilyTenant" => "가족 부서",
        FamilyTenantService.SetAction => "시상 부서",
        "FamilyRequested" => "온가족 신청",
        "FamilyRequestDecided" => "온가족 신청 처리",
        "FamilyLinked" => "온가족 묶기",
        "FamilyUnlinked" => "온가족 빼기 · 해체",
        "FamilySaved" => "온가족 고치기",
        ImpersonationService.StartAction => "가장 시작",
        ImpersonationService.StopAction => "가장 복귀",
        PlatformRuleService.LogAction => "기본 규칙",
        BrandingService.LogAction => "이름 · 색",
        SeasonService.LogAction => "시즌",
        PlatformAccountService.ResetPasswordAction => "임시 비밀번호",
        PlatformAccountService.ResetMarksAction => "읽음 초기화",
        _ => action,
    };

    /// <summary>감사 로그(최근부터). action · tenantId 로 거르고, days 가 0 이면 전체 기간. take 건만.</summary>
    public async Task<(IReadOnlyList<OpsAuditRow> Rows, bool More, IReadOnlyList<string> Actions, IReadOnlyList<(int Id, string Name)> Tenants)> AuditAsync(
        int actor, string? action, int? tenantId, int days, int take, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var q = from l in ctx.AuditLogs.AsNoTracking()
                join a in ctx.Accounts on l.ActorAccountId equals a.Id
                join t in ctx.Tenants on l.TenantId equals t.Id into tt
                from t in tt.DefaultIfEmpty()
                select new { l.Id, l.OccurredUtc, l.Action, l.Detail, l.TenantId, a.DisplayName, a.IsPlatformAdmin, TenantName = t == null ? null : t.Name };
        if (!string.IsNullOrEmpty(action)) q = q.Where(x => x.Action == action);
        if (tenantId is int tid) q = q.Where(x => x.TenantId == tid);
        if (days > 0) { var from = time.GetUtcNow().UtcDateTime.AddDays(-days); q = q.Where(x => x.OccurredUtc >= from); }
        var rows = await q.OrderByDescending(x => x.OccurredUtc).ThenByDescending(x => x.Id).Take(take + 1).ToListAsync(ct);
        var actions = await ctx.AuditLogs.Select(l => l.Action).Distinct().ToListAsync(ct);
        var tenants = await ctx.Tenants.AsNoTracking().OrderBy(t => t.Name).Select(t => new { t.Id, t.Name }).ToListAsync(ct);
        return (rows.Take(take).Select(x => new OpsAuditRow(x.OccurredUtc, x.Action, Label(x.Action),
                    x.IsPlatformAdmin ? $"{x.DisplayName}(운영자)" : x.DisplayName, x.TenantName, PlatformHomeService.LogText(x.Action, x.Detail))).ToList(),
            rows.Count > take, actions.OrderBy(Label).ToList(), tenants.Select(t => (t.Id, t.Name)).ToList());
    }
}
