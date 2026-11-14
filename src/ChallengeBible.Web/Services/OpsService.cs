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
    public const string MigrateAction = "MigrationsApplied";
    public const string MigrateConfirm = "APPLY";

    private async Task<AppDbContext> OpenAsync(string actor, CancellationToken ct)
    {
        var ctx = systemDb.Create(actor);
        if (!await AccountService.IsActivePlatformAdminAsync(ctx, actor, ct))
        {
            await ctx.DisposeAsync();
            throw new UnauthorizedAccessException("플랫폼 관리자만 볼 수 있어요.");
        }
        return ctx;
    }

    public async Task<IReadOnlyList<OpsCheck>> ChecksAsync(string actor, CancellationToken ct = default)
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
        var up = now - app.StartedAt;
        var started = TimeZoneInfo.ConvertTimeFromUtc(app.StartedAt, TimeZoneInfo.FindSystemTimeZoneById(PlatformTimeZone));
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
        "FamilyTenantSet" => "시상 부서",   // 옛 기록(0083 으로 기능은 없어짐)
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
        MigrateAction => "마이그레이션 적용",
        _ => action,
    };

    /// <summary>감사 로그(최근부터). action · tenantId 로 거르고, days 가 0 이면 전체 기간. take 건만.</summary>
    public async Task<(IReadOnlyList<OpsAuditRow> Rows, bool More, IReadOnlyList<string> Actions, IReadOnlyList<(int Id, string Name)> Tenants)> AuditAsync(
        string actor, string? action, int? tenantId, int days, int take, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        var q = from l in ctx.AuditLogs.AsNoTracking()
                join a in ctx.Users on l.ActorUserName equals a.UserName
                join t in ctx.Tenants on l.TenantId equals t.Id into tt
                from t in tt.DefaultIfEmpty()
                select new { l.Id, l.OccurredUtc, l.Action, l.Detail, l.TenantId, DisplayName = AppUser.FullName(a.LastName, a.FirstName), IsPlatformAdmin = ctx.UserRoles.Any(ur => ur.UserId == a.Id && ctx.IdentityRoleRows.Any(r => r.Id == ur.RoleId && r.Name == IdentityRoles.SuperAdmin)), TenantName = t == null ? null : t.Name };
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

    /// <summary>적용 안 된 마이그레이션 이름(시험용 메모리 DB 는 빈 목록).</summary>
    public async Task<IReadOnlyList<string>> PendingAsync(string actor, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        return ctx.Database.IsRelational() ? (await ctx.Database.GetPendingMigrationsAsync(ct)).ToList() : [];
    }

    /// <summary>
    /// 적용 안 된 마이그레이션 적용(2026-11-14 사용자: 휴대폰만으로 실DB 에 표를 만든다 — SQL Studio 는 보안 정책 문장을 막는다).
    /// 플랫폼 관리자만, 확인 글자 APPLY. 이 화면이 보여 준 목록과 지금 목록이 다르면 하지 않는다(그사이 바뀜). 감사 로그를 남긴다.
    /// </summary>
    public async Task<(bool Ok, string Message)> MigrateAsync(string actor, IReadOnlyList<string> expected, CancellationToken ct = default)
    {
        await using var ctx = await OpenAsync(actor, ct);
        if (!ctx.Database.IsRelational()) return (false, "시험용 메모리 DB 에서는 하지 않아요.");
        var pending = (await ctx.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count == 0) return (false, "적용할 마이그레이션이 없어요.");
        if (!pending.SequenceEqual(expected)) return (false, "그사이 목록이 바뀌었어요. 화면을 새로 고쳐 다시 확인해 주세요.");
        ctx.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        try
        {
            await ctx.Database.MigrateAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, $"적용하지 못했어요(마이그레이션마다 트랜잭션이라 실패한 것은 되돌아가요): {ex.GetType().Name} · {ex.Message}");
        }
        ctx.AuditLogs.Add(new AuditLog { OccurredUtc = time.GetUtcNow().UtcDateTime, ActorUserName = actor, Action = MigrateAction, Detail = string.Join(", ", pending.Select(Short)) });
        await ctx.SaveChangesAsync(ct);
        return (true, $"{pending.Count}개 적용했어요: {string.Join(", ", pending.Select(Short))}");
    }
}
